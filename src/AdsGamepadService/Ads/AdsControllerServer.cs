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
              contract v1.1; the controller blocks above are untouched. */
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

        internal const int MaximumControllers = 4;
        internal const int InputStructSize = 32;
        internal const int ServiceInfoBlockSize = 32;
        internal const int ServiceInfoStructSize = 16;

        /* 0xF000 sits outside every controller read group and every rumble
           sum, so contract v1 clients never collide with it. */
        internal const uint ServiceInfoIndexGroup = 0xF000;
        internal const ushort ContractVersionMajor = 1;
        internal const ushort ContractVersionMinor = 1;
        internal const uint CapabilityXInputBackend = 1u << 0;

        private const uint ReadGroupStride = 0x10000;
        private const uint RumbleCommandOffset = 0x10;

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
        }

        public AdsControllerServer(ushort port, string portName, ILoggerFactory loggerFactory, int maxControllers = MaximumControllers)
            : this(port, portName, loggerFactory, CreateDefaultGamepads(maxControllers))
        {
        }

        internal AdsControllerServer(ushort port, string portName, ILoggerFactory loggerFactory, IGamepad[] gamepads)
            : base(port, portName, loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<AdsControllerServer>();
            _gamepads = gamepads;
        }

        /* Slots above maxControllers get a disabled placeholder instead of an
           XInput backend, so their index groups still answer with the normal
           disconnected payload and the wire surface never changes shape. */
        private static IGamepad[] CreateDefaultGamepads(int maxControllers)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxControllers, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxControllers, MaximumControllers);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Only the Windows XInput backend exists today.");
            }

            var gamepads = new IGamepad[MaximumControllers];
            for (int i = 0; i < MaximumControllers; ++i)
            {
                gamepads[i] = i < maxControllers
                    ? new XInputGamepad(i + 1)
                    : new DisabledGamepad(i + 1);
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

                return SerializeInputs(BuildInputs(gamepad));
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

        /* Pure assembly of the service info block. The service version is
           read from the assembly at runtime, so the project Version property
           stays the single source of that number. The only input backend
           today is XInput, so capability bit 0 is always set. */
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
                Capabilities = CapabilityXInputBackend,
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
