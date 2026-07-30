using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AdsGamepadService.Input;
using Microsoft.Extensions.Logging;
using TwinCAT.Ads;
using TwinCAT.Ads.Server;

namespace AdsGamepadService
{
    /* ADS server that publishes gamepad state to PLC clients.

       The wire contract is frozen and versioned with the PLC library:
       Read:  IndexGroup selects the controller (0x10000, 0x20000, 0x30000,
              0x40000 for controllers 1 to 4). IndexOffset and the requested
              length are ignored; the reply is always the 32 byte struct below.
              A disconnected controller answers success with 32 zero bytes and
              the PLC detects it through States bit 0.
       Write: rumble command, exactly 8 bytes (two 32 bit floats), matched on
              the sum of IndexGroup and IndexOffset (0x10010 to 0x40010).
       Info:  IndexGroup 0xF000 answers with the 32 byte service info block
              (contract version, service version, capabilities). Added in
              contract v1.1; the controller blocks above are untouched.
       Ext:   IndexGroup controller number times 0x10000 plus 0x100 answers
              with the 96 byte extended block (extra buttons, motion, touch,
              report counter). Added in contract v1.3. A slot whose backend
              has no extended data answers success with 96 zero bytes, the
              same fail safe shape as a disconnected controller. */
    public class AdsControllerServer : AdsServer
    {
        /* Wire image of one controller. The layout is pinned so the 32 byte
           contract can never shift with compiler or runtime changes. */
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct AdsGamepadInputs
        {
            public int ControllerNumber;
            public float LeftStickY;
            public float LeftStickX;
            public float RightStickY;
            public float RightStickX;
            public float LeftTrigger;
            public float RightTrigger;
            public short Buttons;
            public short States;
        }

        /* Wire image of the service info block, contract v1.1. Serialized
           into a 32 byte reply; the bytes past this struct stay zero. */
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct AdsServiceInfo
        {
            public ushort ContractMajor;
            public ushort ContractMinor;
            public ushort ServiceMajor;
            public ushort ServiceMinor;
            public ushort ServicePatch;
            public ushort Reserved;
            public uint Capabilities;
        }

        /* One touchpad contact in the extended block, six bytes. */
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct AdsTouchPoint
        {
            public byte Active;
            public byte ContactId;
            public ushort X;
            public ushort Y;
        }

        /* Wire image of the extended block, contract v1.3; the battery
           fields carry data since v1.4. Serialized into a 96 byte reply;
           everything past this struct is reserved and stays zero until a
           later contract minor assigns meaning. */
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct AdsGamepadExtInputs
        {
            public ushort ExtSize;
            public ushort ExtLayoutVersion;
            public ushort ExtButtons;
            public ushort ExtFlags;
            public short GyroX;
            public short GyroY;
            public short GyroZ;
            public short AccelX;
            public short AccelY;
            public short AccelZ;
            public AdsTouchPoint Touch0;
            public AdsTouchPoint Touch1;
            public uint Sequence;
            public byte BatteryPercent;
            public byte BatteryFlags;
            public ushort Reserved;
        }

        internal const int MaximumControllers = 4;
        internal const int InputStructSize = 32;
        internal const int ServiceInfoBlockSize = 32;
        internal const int ServiceInfoStructSize = 16;
        internal const int ExtBlockSize = 96;
        internal const int ExtStructSize = 40;
        internal const ushort ExtBlockLayoutVersion = 1;
        internal const ushort ExtFlagDataPresent = 0x0001;
        /* Since contract 1.4: the battery fields carry decoded data. The
           flag is per read, so a pad state the service cannot interpret
           falls back to zeroes without lying. */
        internal const ushort ExtFlagBatteryValid = 0x0002;
        internal const byte BatteryFlagCharging = 0x01;
        internal const byte BatteryFlagFull = 0x02;

        /* 0xF000 sits outside every controller read group and every rumble
           sum, so contract v1 clients never collide with it. */
        internal const uint ServiceInfoIndexGroup = 0xF000;
        internal const ushort ContractVersionMajor = 1;
        internal const ushort ContractVersionMinor = 4;
        internal const uint CapabilityXInputBackend = 1u << 0;
        internal const uint CapabilityDualSenseBackend = 1u << 1;
        internal const uint CapabilityExtendedBlock = 1u << 2;

        private const uint ReadGroupStride = 0x10000;
        private const uint RumbleCommandOffset = 0x10;
        /* The extended groups 0x10100 to 0x40100 are disjoint from the
           controller groups, the info group, and every rumble sum. */
        private const uint ExtReadGroupOffset = 0x100;

        private readonly ILogger _logger;
        private readonly IGamepad[] _gamepads;
        private readonly bool[] _lastConnected = new bool[MaximumControllers];

        /* ADS request callbacks can run concurrently. Gamepad polling and
           rumble output share mutable state, so they are serialized here. */
        private readonly object _sync = new();

        static AdsControllerServer()
        {
            if (Marshal.SizeOf<AdsGamepadInputs>() != InputStructSize)
            {
                throw new InvalidOperationException(
                    $"AdsGamepadInputs must marshal to exactly {InputStructSize} bytes, got {Marshal.SizeOf<AdsGamepadInputs>()}.");
            }
            if (Marshal.SizeOf<AdsServiceInfo>() != ServiceInfoStructSize)
            {
                throw new InvalidOperationException(
                    $"AdsServiceInfo must marshal to exactly {ServiceInfoStructSize} bytes, got {Marshal.SizeOf<AdsServiceInfo>()}.");
            }
            if (Marshal.SizeOf<AdsGamepadExtInputs>() != ExtStructSize)
            {
                throw new InvalidOperationException(
                    $"AdsGamepadExtInputs must marshal to exactly {ExtStructSize} bytes, got {Marshal.SizeOf<AdsGamepadExtInputs>()}.");
            }
        }

        public AdsControllerServer(ushort port, string portName, ILoggerFactory loggerFactory, int maxControllers = MaximumControllers, string[]? slotSources = null)
            : this(port, portName, loggerFactory, CreateDefaultGamepads(maxControllers, slotSources, loggerFactory))
        {
        }

        internal AdsControllerServer(ushort port, string portName, ILoggerFactory loggerFactory, IGamepad[] gamepads)
            : base(port, portName, loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<AdsControllerServer>();
            _gamepads = gamepads;
        }

        /* The PlayStation backend owns a reader thread and a device handle;
           they go down with the server. */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IGamepad gamepad in _gamepads)
                {
                    (gamepad as IDisposable)?.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        /* Slots above maxControllers get a disabled placeholder instead of a
           hardware backend, so their index groups still answer with the
           normal disconnected payload and the wire surface never changes
           shape. An active slot reads the backend its configuration names;
           missing entries mean XInput polling the pad with the same number
           as the slot, the behavior of every release before 2.2.0. */
        private static IGamepad[] CreateDefaultGamepads(int maxControllers, string[]? slotSources, ILoggerFactory loggerFactory)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxControllers, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxControllers, MaximumControllers);

            /* XInput exists only on Windows. On Linux such a slot answers as
               disconnected, with one log line naming the reason, so a config
               written for a Windows box degrades loudly instead of silently. */
            ILogger factoryLogger = loggerFactory.CreateLogger<AdsControllerServer>();
            var gamepads = new IGamepad[MaximumControllers];
            for (int i = 0; i < MaximumControllers; ++i)
            {
                if (i >= maxControllers)
                {
                    gamepads[i] = new DisabledGamepad(i + 1);
                    continue;
                }
                string source = slotSources is not null && i < slotSources.Length ? slotSources[i] : ServiceOptions.SourceXInput;
                if (string.Equals(source, ServiceOptions.SourceDualSense, StringComparison.OrdinalIgnoreCase))
                {
                    gamepads[i] = new DualSenseGamepad(i + 1, loggerFactory.CreateLogger<DualSenseGamepad>());
                }
                else if (OperatingSystem.IsWindows())
                {
                    gamepads[i] = new XInputGamepad(i + 1);
                }
                else
                {
                    factoryLogger.LogWarning(
                        "Slot {Slot} uses the XInput backend, which exists on Windows only. The slot answers as disconnected on this system.",
                        i + 1);
                    gamepads[i] = new DisabledGamepad(i + 1);
                }
            }
            return gamepads;
        }

        protected override void OnConnected()
        {
            _logger.LogInformation("ADS server registered at address {Address}.", base.ServerAddress);
        }

        protected override Task<ResultReadBytes> OnReadAsync(AmsAddress sender, uint invokeId, uint indexGroup, uint indexOffset, int readLength, CancellationToken cancel)
        {
            /* Matched on IndexGroup alone like the controller reads below;
               IndexOffset and the requested length are ignored the same way. */
            if (indexGroup == ServiceInfoIndexGroup)
            {
                return Task.FromResult(ResultReadBytes.CreateSuccess(SerializeServiceInfo(BuildServiceInfo()).AsMemory()));
            }

            if (TryGetControllerIndexForExtRead(indexGroup, out int extIndex))
            {
                return Task.FromResult(ResultReadBytes.CreateSuccess(BuildExtPayload(_gamepads[extIndex]).AsMemory()));
            }

            if (!TryGetControllerIndexForRead(indexGroup, out int index))
            {
                return Task.FromResult(ResultReadBytes.CreateError(AdsErrorCode.DeviceInvalidGroup));
            }

            byte[] payload = BuildInputPayload(_gamepads[index]);
            return Task.FromResult(ResultReadBytes.CreateSuccess(payload.AsMemory()));
        }

        protected override Task<ResultWrite> OnWriteAsync(AmsAddress target, uint invokeId, uint indexGroup, uint indexOffset, ReadOnlyMemory<byte> writeData, CancellationToken cancel)
        {
            lock (_sync)
            {
                // Refreshing every controller on any write matches the original implementation
                for (int i = 0; i < _gamepads.Length; ++i)
                {
                    _gamepads[i].Update();
                }

                if (!TryGetControllerIndexForRumble(indexGroup, indexOffset, out int index))
                {
                    return Task.FromResult(ResultWrite.CreateError(AdsErrorCode.DeviceServiceNotSupported));
                }

                IGamepad gamepad = _gamepads[index];
                if (writeData.Length != 8 || !gamepad.Connected)
                {
                    return Task.FromResult(ResultWrite.CreateError(AdsErrorCode.DeviceInvalidParam));
                }

                ReadOnlySpan<byte> data = writeData.Span;
                float leftMotor = BinaryPrimitives.ReadSingleLittleEndian(data);
                float rightMotor = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(4));
                gamepad.Rumble(leftMotor, rightMotor);
                return Task.FromResult(ResultWrite.CreateSuccess());
            }
        }

        /* Extended reads select the controller by IndexGroup alone:
           0x10100 to 0x40100 for controllers 1 to 4, contract v1.3. */
        internal static bool TryGetControllerIndexForExtRead(uint indexGroup, out int index)
        {
            if (indexGroup % ReadGroupStride == ExtReadGroupOffset)
            {
                uint number = indexGroup / ReadGroupStride;
                if (number >= 1 && number <= MaximumControllers)
                {
                    index = (int)number - 1;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /* Read requests select the controller by IndexGroup alone:
           0x10000 to 0x40000 for controllers 1 to 4. */
        internal static bool TryGetControllerIndexForRead(uint indexGroup, out int index)
        {
            if (indexGroup % ReadGroupStride == 0)
            {
                uint number = indexGroup / ReadGroupStride;
                if (number >= 1 && number <= MaximumControllers)
                {
                    index = (int)number - 1;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /* Rumble writes are matched on the unchecked sum of IndexGroup and
           IndexOffset, 0x10010 to 0x40010 for controllers 1 to 4. The PLC
           library sends IndexGroup 0x10000 times the controller number with
           IndexOffset 16, but any split that sums to the same value has always
           been accepted, so the sum check stays for compatibility. */
        internal static bool TryGetControllerIndexForRumble(uint indexGroup, uint indexOffset, out int index)
        {
            uint sum = unchecked(indexGroup + indexOffset);
            for (uint number = 1; number <= MaximumControllers; ++number)
            {
                if (sum == number * ReadGroupStride + RumbleCommandOffset)
                {
                    index = (int)number - 1;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        private byte[] BuildInputPayload(IGamepad gamepad)
        {
            lock (_sync)
            {
                UpdateAndLogTransition(gamepad);
                return SerializeInputs(BuildInputs(gamepad));
            }
        }

        private byte[] BuildExtPayload(IGamepad gamepad)
        {
            lock (_sync)
            {
                UpdateAndLogTransition(gamepad);
                return SerializeExtInputs(BuildExtInputs(gamepad));
            }
        }

        /* Both read paths poll the backend, so a program that only reads the
           extended block still drives the connect and disconnect log lines;
           the stored state keeps each transition to a single line. */
        private void UpdateAndLogTransition(IGamepad gamepad)
        {
            gamepad.Update();

            int slot = gamepad.ControllerNumber - 1;
            if (gamepad.Connected != _lastConnected[slot])
            {
                _lastConnected[slot] = gamepad.Connected;
                if (gamepad.Connected)
                {
                    _logger.LogInformation("Controller {Number} connected.", gamepad.ControllerNumber);
                }
                else
                {
                    _logger.LogInformation("Controller {Number} disconnected.", gamepad.ControllerNumber);
                }
            }
        }

        /* Pure assembly of the wire struct from one controller snapshot. A
           disconnected controller yields an all zero struct on purpose; the
           PLC detects disconnects through States bit 0, not through errors. */
        internal static AdsGamepadInputs BuildInputs(IGamepad gamepad)
        {
            var inputs = default(AdsGamepadInputs);
            if (gamepad.Connected)
            {
                inputs.ControllerNumber = gamepad.ControllerNumber;
                inputs.LeftStickY = gamepad.LeftStickY;
                inputs.LeftStickX = gamepad.LeftStickX;
                inputs.RightStickY = gamepad.RightStickY;
                inputs.RightStickX = gamepad.RightStickX;
                inputs.LeftTrigger = gamepad.LeftTrigger;
                inputs.RightTrigger = gamepad.RightTrigger;
                inputs.Buttons = unchecked((short)gamepad.ButtonBits);
                inputs.States = BuildStates(gamepad);
            }
            return inputs;
        }

        internal static byte[] SerializeInputs(in AdsGamepadInputs inputs)
        {
            byte[] payload = new byte[InputStructSize];
            MemoryMarshal.Write(payload, in inputs);
            return payload;
        }

        /* Pure assembly of the extended block. A slot whose backend has no
           extended data, and a disconnected pad, both yield the all zero
           struct: consumers branch on the data present flag, never on noise,
           and zeroes are the fail safe state everywhere in this contract. */
        internal static AdsGamepadExtInputs BuildExtInputs(IGamepad gamepad)
        {
            var inputs = default(AdsGamepadExtInputs);
            if (gamepad is IExtendedGamepad extended && gamepad.Connected && extended.TryGetExtended(out GamepadExtendedState state))
            {
                inputs.ExtSize = ExtBlockSize;
                inputs.ExtLayoutVersion = ExtBlockLayoutVersion;
                inputs.ExtButtons = state.ExtButtons;
                inputs.ExtFlags = ExtFlagDataPresent;
                inputs.GyroX = state.GyroX;
                inputs.GyroY = state.GyroY;
                inputs.GyroZ = state.GyroZ;
                inputs.AccelX = state.AccelX;
                inputs.AccelY = state.AccelY;
                inputs.AccelZ = state.AccelZ;
                inputs.Touch0 = ToWireTouch(state.Touch0);
                inputs.Touch1 = ToWireTouch(state.Touch1);
                inputs.Sequence = state.Sequence;
                if (state.BatteryValid)
                {
                    inputs.ExtFlags |= ExtFlagBatteryValid;
                    inputs.BatteryPercent = state.BatteryPercent;
                    inputs.BatteryFlags = (byte)((state.BatteryCharging ? BatteryFlagCharging : 0)
                        | (state.BatteryFull ? BatteryFlagFull : 0));
                }
            }
            return inputs;
        }

        private static AdsTouchPoint ToWireTouch(in GamepadTouchPoint point)
        {
            return new AdsTouchPoint
            {
                Active = point.Active ? (byte)1 : (byte)0,
                ContactId = point.ContactId,
                X = point.X,
                Y = point.Y,
            };
        }

        internal static byte[] SerializeExtInputs(in AdsGamepadExtInputs inputs)
        {
            byte[] payload = new byte[ExtBlockSize];
            MemoryMarshal.Write(payload, in inputs);
            return payload;
        }

        /* Pure assembly of the service info block. The service version is
           read from the assembly at runtime, so the project Version property
           stays the single source of that number. The capability bits state
           which input backends this build contains, not which slots use
           them, so both are always set. */
        internal static AdsServiceInfo BuildServiceInfo()
        {
            Version version = typeof(AdsControllerServer).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            return new AdsServiceInfo
            {
                ContractMajor = ContractVersionMajor,
                ContractMinor = ContractVersionMinor,
                ServiceMajor = (ushort)version.Major,
                ServiceMinor = (ushort)version.Minor,
                ServicePatch = (ushort)Math.Max(version.Build, 0),
                Reserved = 0,
                Capabilities = CapabilityXInputBackend | CapabilityDualSenseBackend | CapabilityExtendedBlock,
            };
        }

        internal static byte[] SerializeServiceInfo(in AdsServiceInfo info)
        {
            byte[] payload = new byte[ServiceInfoBlockSize];
            MemoryMarshal.Write(payload, in info);
            return payload;
        }

        private static short BuildStates(IGamepad gamepad)
        {
            int states = 1 << 0; // bit 0, connected
            states |= gamepad.BatteryType switch
            {
                GamepadBatteryType.Disconnected => 1 << 1,
                GamepadBatteryType.Wired => 1 << 2,
                GamepadBatteryType.Alkaline => 1 << 3,
                GamepadBatteryType.Nimh => 1 << 4,
                GamepadBatteryType.Unknown => 1 << 5,
                _ => 0,
            };
            states |= gamepad.BatteryLevel switch
            {
                GamepadBatteryLevel.Empty => 1 << 6,
                GamepadBatteryLevel.Low => 1 << 7,
                GamepadBatteryLevel.Medium => 1 << 8,
                GamepadBatteryLevel.Full => 1 << 9,
                _ => 0,
            };
            return unchecked((short)states);
        }
    }
}
