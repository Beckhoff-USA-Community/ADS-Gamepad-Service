using Microsoft.Extensions.Logging;

namespace AdsGamepadService.Input
{
    /* Reads one wired PlayStation 5 DualSense controller over raw HID, on
       Windows through the HID class API and on Linux through the hidraw
       device node; the report bytes are identical on both. A background
       thread blocks on the device, which delivers a report roughly every
       four milliseconds while the pad is connected, and stores the newest
       decoded state. Update just takes that snapshot, so the ADS callback
       never waits on the device.

       A DualSense that holds a Bluetooth session with another host keeps
       streaming USB reports with live motion data but frozen controls. The
       reader detects that state and logs a warning, because to the PLC it is
       indistinguishable from an idle pad. */
    internal sealed class DualSenseGamepad : IGamepad, IDisposable
    {
        private const string WindowsPathMatch = "vid_054c&pid_0ce6";
        // Bus 0003 is USB; the prefix excludes Bluetooth pads like mi_03 does on Windows
        private const string LinuxHidIdMatch = "0003:0000054C:00000CE6";
        private const int ReconnectDelayMs = 1000;
        private const long StaleReportMs = 500;
        private const long FrozenControlsWarnMs = 10000;
        private const int OutputReportLength = 48;
        private const byte UsbOutputReportId = 0x02;

        private sealed record Snapshot(DualSenseState State, long Tick);

        private readonly ILogger _logger;
        private readonly Thread _reader;
        private readonly object _channelSync = new();
        private volatile bool _stopping;
        private volatile Snapshot? _latest;
        private IHidChannel? _channel;

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

            lock (_channelSync)
            {
                _channel?.WriteReport(report);
            }
        }

        /* Shutdown has no way to abort a blocked synchronous read; a
           streaming pad returns within milliseconds and the loop then sees
           the stop flag, and a silent device is bounded by the join timeout
           with the leftover background thread dying with the process. */
        public void Dispose()
        {
            _stopping = true;
            CloseChannel();
            _reader.Join(TimeSpan.FromSeconds(2));
        }

        private void CloseChannel()
        {
            lock (_channelSync)
            {
                _channel?.Dispose();
                _channel = null;
            }
        }

        private static IHidChannel? OpenChannel()
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsHidChannel.Open(WindowsPathMatch);
            }
            return LinuxHidChannel.Open(LinuxHidIdMatch);
        }

        private void ReadLoop()
        {
            while (!_stopping)
            {
                IHidChannel? channel = OpenChannel();
                if (channel is null)
                {
                    Thread.Sleep(ReconnectDelayMs);
                    continue;
                }
                lock (_channelSync)
                {
                    _channel = channel;
                }

                _logger.LogInformation("DualSense connected on slot {Slot}.", ControllerNumber);
                try
                {
                    ReadReports(channel);
                }
                catch (Exception ex)
                {
                    /* Nothing in the loop is expected to throw, but an
                       unhandled exception on this thread would take the whole
                       service down. One reconnect cycle is the better price. */
                    _logger.LogError(ex, "DualSense reader on slot {Slot} failed unexpectedly.", ControllerNumber);
                }
                finally
                {
                    _latest = null;
                    CloseChannel();
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

        private void ReadReports(IHidChannel channel)
        {
            byte[] buffer = new byte[DualSenseReport.UsbInputReportLength];
            /* Control bytes are 1 to 6 and 8 to 10. Byte 7 is a sequence
               counter that changes with every report, so it must stay out of
               the comparison or the frozen state could never be detected. */
            byte[] lastControls = new byte[9];
            /* Allocated once outside the loop: stackalloc space is only
               reclaimed on method return, so per iteration it would grow the
               stack until the thread dies. */
            Span<byte> controls = stackalloc byte[9];
            long lastControlChange = Environment.TickCount64;
            bool frozenWarned = false;

            while (!_stopping)
            {
                int read = channel.ReadReport(buffer);
                if (read < 0)
                {
                    return;
                }
                if (read == 0 || !DualSenseReport.TryParse(buffer.AsSpan(0, read), out DualSenseState state))
                {
                    continue;
                }

                _latest = new Snapshot(state, Environment.TickCount64);

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
    }
}
