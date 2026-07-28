using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace AdsGamepadService.Input
{
    /* Reads one wired PlayStation 5 DualSense controller over raw HID. A
       background thread blocks on the interrupt pipe, which delivers a report
       roughly every four milliseconds while the pad is connected, and stores
       the newest decoded state. Update just takes that snapshot, so the ADS
       callback never waits on the device. Reads and writes use two separate
       handles: on a single synchronous handle Windows serializes the
       operations, and a rumble write would queue behind the blocked read.

       A DualSense that holds a Bluetooth session with another host keeps
       streaming USB reports with live motion data but frozen controls. The
       reader detects that state and logs a warning, because to the PLC it is
       indistinguishable from an idle pad. */
    [SupportedOSPlatform("windows")]
    internal sealed class DualSenseGamepad : IGamepad, IDisposable
    {
        private const string VidPidMatch = "vid_054c&pid_0ce6";
        private const int ReconnectDelayMs = 1000;
        private const long StaleReportMs = 500;
        private const long FrozenControlsWarnMs = 10000;
        private const int OutputReportLength = 48;
        private const byte UsbOutputReportId = 0x02;

        private sealed record Snapshot(DualSenseState State, long Tick);

        private readonly ILogger _logger;
        private readonly Thread _reader;
        private readonly object _writeSync = new();
        private volatile bool _stopping;
        private volatile Snapshot? _latest;
        private SafeFileHandle? _readHandle;
        private SafeFileHandle? _writeHandle;

        private DualSenseState _current;
        private bool _connected;

        public DualSenseGamepad(int controllerNumber, ILogger logger)
        {
            ControllerNumber = controllerNumber;
            _logger = logger;
            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"DualSense slot {controllerNumber}",
            };
            _reader.Start();
        }

        public int ControllerNumber { get; }

        public bool Connected => _connected;

        public void Update()
        {
            Snapshot? snapshot = _latest;
            if (snapshot is not null && IsFresh(Environment.TickCount64, snapshot.Tick))
            {
                _current = snapshot.State;
                _connected = true;
            }
            else
            {
                _current = default;
                _connected = false;
            }
        }

        /* A report older than half a second means the reader lost the pad;
           the slot then reads as disconnected with zeroed values, the same
           fail safe rule the wire contract applies everywhere. */
        internal static bool IsFresh(long nowTick, long reportTick)
        {
            return nowTick - reportTick < StaleReportMs;
        }

        /* The published X carries the physical Y axis and the published Y the
           physical X axis, matching what the XInput backend has always done,
           so a program moves the same way no matter which pad fills the slot. */
        public float LeftStickY => GamepadMath.StickPercent(_current.ThumbLX);

        public float LeftStickX => GamepadMath.StickPercent(_current.ThumbLY);

        public float RightStickY => GamepadMath.StickPercent(_current.ThumbRX);

        public float RightStickX => GamepadMath.StickPercent(_current.ThumbRY);

        public float LeftTrigger => GamepadMath.TriggerPercent(_current.LeftTrigger);

        public float RightTrigger => GamepadMath.TriggerPercent(_current.RightTrigger);

        public ushort ButtonBits => _current.WireButtons;

        /* The pad runs from the USB cable. The battery detail bytes of the
           report are not decoded yet, so the wire reports a wired pad with a
           full level, the most truthful constant available. */
        public GamepadBatteryType BatteryType => _connected ? GamepadBatteryType.Wired : GamepadBatteryType.None;

        public GamepadBatteryLevel BatteryLevel => _connected ? GamepadBatteryLevel.Full : GamepadBatteryLevel.None;

        // Test hook so the wire mapping is verifiable without hardware
        internal void ApplySnapshot(in DualSenseState state)
        {
            _current = state;
            _connected = true;
        }

        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
            if (!_connected)
            {
                return;
            }

            /* Output report 0x02: flag byte 1 selects the classic rumble
               path, byte 3 is the right motor, byte 4 the left motor. */
            byte[] report = new byte[OutputReportLength];
            report[0] = UsbOutputReportId;
            report[1] = 0x03;
            report[3] = GamepadMath.RumbleMotorByte(rightMotorPercent);
            report[4] = GamepadMath.RumbleMotorByte(leftMotorPercent);

            /* The lock pairs with the reader closing the write handle, and
               the catch covers a close that lands inside the call anyway. A
               rumble command lost to a disconnecting pad is the right
               outcome, an exception into the ADS dispatch is not. */
            lock (_writeSync)
            {
                SafeFileHandle? handle = _writeHandle;
                if (handle is null || handle.IsInvalid || handle.IsClosed)
                {
                    return;
                }
                try
                {
                    HidNative.WriteFile(handle, report, (uint)report.Length, out _, 0);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /* Shutdown has no way to abort a synchronous read: the interop layer
           keeps the handle alive until the call returns. A streaming pad
           returns within milliseconds and the loop then sees the stop flag;
           a silent device is bounded by the join timeout and the leftover
           background thread dies with the process. */
        public void Dispose()
        {
            _stopping = true;
            _readHandle?.Dispose();
            CloseWriteHandle();
            _reader.Join(TimeSpan.FromSeconds(2));
        }

        private void CloseWriteHandle()
        {
            lock (_writeSync)
            {
                _writeHandle?.Dispose();
                _writeHandle = null;
            }
        }

        private void ReadLoop()
        {
            while (!_stopping)
            {
                if (!OpenDevice())
                {
                    Thread.Sleep(ReconnectDelayMs);
                    continue;
                }

                _logger.LogInformation("DualSense connected on slot {Slot}.", ControllerNumber);
                try
                {
                    ReadReports(_readHandle!);
                }
                finally
                {
                    _latest = null;
                    _readHandle?.Dispose();
                    _readHandle = null;
                    CloseWriteHandle();
                    if (!_stopping)
                    {
                        _logger.LogInformation("DualSense disconnected from slot {Slot}.", ControllerNumber);
                    }
                }

                /* Also waits out a device that opens but will not read, so a
                   broken state cannot spin the loop or flood the log. */
                if (!_stopping)
                {
                    Thread.Sleep(ReconnectDelayMs);
                }
            }
        }

        private void ReadReports(SafeFileHandle handle)
        {
            byte[] buffer = new byte[DualSenseReport.UsbInputReportLength];
            /* Control bytes are 1 to 6 and 8 to 10. Byte 7 is a sequence
               counter that changes with every report, so it must stay out of
               the comparison or the frozen state could never be detected. */
            byte[] lastControls = new byte[9];
            long lastControlChange = Environment.TickCount64;
            bool frozenWarned = false;

            while (!_stopping)
            {
                uint read;
                try
                {
                    if (!HidNative.ReadFile(handle, buffer, (uint)buffer.Length, out read, 0))
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The service is shutting down and closed the handle
                    return;
                }
                if (read == 0 || !DualSenseReport.TryParse(buffer.AsSpan(0, (int)read), out DualSenseState state))
                {
                    continue;
                }

                _latest = new Snapshot(state, Environment.TickCount64);

                Span<byte> controls = stackalloc byte[9];
                buffer.AsSpan(1, 6).CopyTo(controls);
                buffer.AsSpan(8, 3).CopyTo(controls[6..]);
                if (!controls.SequenceEqual(lastControls))
                {
                    controls.CopyTo(lastControls);
                    lastControlChange = Environment.TickCount64;
                    frozenWarned = false;
                }
                else if (!frozenWarned && Environment.TickCount64 - lastControlChange > FrozenControlsWarnMs)
                {
                    frozenWarned = true;
                    _logger.LogWarning(
                        "DualSense on slot {Slot} streams reports but its controls have not changed for {Seconds} seconds. " +
                        "If the pad does not react, unpair it from every Bluetooth host and reconnect the cable.",
                        ControllerNumber, FrozenControlsWarnMs / 1000);
                }
            }
        }

        private bool OpenDevice()
        {
            /* Only the gamepad collection on USB interface three of a real
               pad is accepted. A Bluetooth DualSense enumerates with a
               different path shape and never matches the filter, and other
               matches would be wrappers or clones with unverified reports. */
            string? path = null;
            foreach (string candidate in HidNative.ListHidInterfacePaths())
            {
                if (candidate.Contains(VidPidMatch, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Contains("mi_03", StringComparison.OrdinalIgnoreCase))
                {
                    path = candidate;
                    break;
                }
            }
            if (path is null)
            {
                return false;
            }

            SafeFileHandle readHandle = HidNative.CreateFile(
                path,
                HidNative.GENERIC_READ,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                0, HidNative.OPEN_EXISTING, 0, 0);
            if (readHandle.IsInvalid)
            {
                readHandle.Dispose();
                return false;
            }

            SafeFileHandle writeHandle = HidNative.CreateFile(
                path,
                HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                0, HidNative.OPEN_EXISTING, 0, 0);
            if (writeHandle.IsInvalid)
            {
                writeHandle.Dispose();
                readHandle.Dispose();
                return false;
            }

            _readHandle = readHandle;
            lock (_writeSync)
            {
                _writeHandle = writeHandle;
            }
            return true;
        }
    }
}
