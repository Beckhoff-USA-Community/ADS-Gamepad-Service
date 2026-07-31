using Microsoft.Extensions.Logging;

namespace AdsGamepadService.Input
{
    /* Reads one PlayStation 5 DualSense controller over raw HID, on
       Windows through the HID class API and on Linux through the hidraw
       device node; the report bytes are identical on both. The pad connects
       over USB or Bluetooth with the cable preferred; on Linux the
       Bluetooth path additionally needs the kernel Bluetooth stack, which
       the standard Beckhoff kernel does not ship. A background
       thread blocks on the device, which delivers a report roughly every
       four milliseconds while the pad is connected, and stores the newest
       decoded state. Update just takes that snapshot, so the ADS callback
       never waits on the device.

       A DualSense that holds a Bluetooth session with another host keeps
       streaming USB reports with live motion data but frozen controls. The
       reader detects that state and logs a warning, because to the PLC it is
       indistinguishable from an idle pad. */
    internal sealed class DualSenseGamepad : IGamepad, IExtendedGamepad, IDisposable
    {
        private const string WindowsPathMatch = "vid_054c&pid_0ce6";
        // The Bluetooth HID node carries the ids in this path shape instead
        private const string WindowsBtPathMatch = "_vid&0002054c_pid&0ce6";
        // Bus 0003 is the pad on USB, bus 0005 the same pad over Bluetooth
        private const string LinuxUsbHidIdMatch = "0003:0000054C:00000CE6";
        private const string LinuxBtHidIdMatch = "0005:0000054C:00000CE6";
        // Feature report 0x05 holds calibration; reading it also switches a
        // Bluetooth pad from its simplified reports to the full 0x31 frames
        private const byte BtModeSwitchFeatureId = 0x05;
        private const int BtModeSwitchFeatureLength = 41;
        private const int ReconnectDelayMs = 1000;
        private const long StaleReportMs = 500;
        private const long FrozenControlsWarnMs = 10000;
        private const int OutputReportLength = 48;
        private const byte UsbOutputReportId = 0x02;

        private sealed record Snapshot(DualSenseState State, uint Sequence, long Tick);

        private readonly ILogger _logger;
        private readonly Thread _reader;
        private readonly Thread _writer;
        private readonly object _channelSync = new();
        private readonly object _rumbleSync = new();
        private readonly AutoResetEvent _rumbleSignal = new(false);
        private volatile bool _stopping;
        private volatile Snapshot? _latest;
        private IHidChannel? _channel;
        private (byte Left, byte Right)? _pendingRumble;
        /* Set when a frozen USB stream suggests the pad is alive on its
           Bluetooth side; the next open then tries Bluetooth first. */
        private volatile bool _retryPreferBluetooth;
        private volatile bool _bluetoothTransport;
        // Keeps the permission warning to one line per denial streak
        private bool _accessDeniedLogged;

        private DualSenseState _current;
        private bool _connected;
        private uint _extSequence;
        private byte _btOutputSequence;

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
            /* Rumble goes through its own thread so the ADS write callback
               never waits on the transport. A Bluetooth output report can
               pend for seconds while a link dies, and the callback runs
               under the server lock every slot's reads share. */
            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = $"DualSense rumble slot {controllerNumber}",
            };
            _writer.Start();
        }

        public int ControllerNumber { get; }

        public bool Connected => _connected;

        public void Update()
        {
            Snapshot? snapshot = _latest;
            if (snapshot is not null && IsFresh(Environment.TickCount64, snapshot.Tick))
            {
                _current = snapshot.State;
                _extSequence = snapshot.Sequence;
                _connected = true;
            }
            else
            {
                _current = default;
                _extSequence = 0;
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

        /* The masked difference counts dropped reports and survives the
           wrap, so the wide counter only ever moves forward. The counter is
           eight bits on USB and four bits on Bluetooth, hence the mask. */
        internal static uint AdvanceWideSequence(uint wideSequence, byte lastSequence, byte nextSequence, byte counterMask = 0xFF)
        {
            return wideSequence + (uint)((nextSequence - lastSequence) & counterMask);
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

        /* Over USB the classic States word reports a wired pad with a full
           level, unchanged since the backend shipped, and the real numbers
           live in the extended block. Over Bluetooth that constant would
           hide a draining battery from every v1 program, so the level is
           mapped from the real charge instead; the type reads unknown
           because the enum predates lithium pads. */
        public GamepadBatteryType BatteryType
        {
            get
            {
                if (!_connected)
                {
                    return GamepadBatteryType.None;
                }
                return _bluetoothTransport ? GamepadBatteryType.Unknown : GamepadBatteryType.Wired;
            }
        }

        public GamepadBatteryLevel BatteryLevel
        {
            get
            {
                if (!_connected)
                {
                    return GamepadBatteryLevel.None;
                }
                if (!_bluetoothTransport)
                {
                    return GamepadBatteryLevel.Full;
                }
                if (!_current.BatteryKnown ||
                    !DualSenseReport.TryDecodeBattery(_current.BatteryRaw, out byte percent, out _, out _))
                {
                    return GamepadBatteryLevel.None;
                }
                return MapBatteryLevel(percent);
            }
        }

        internal static GamepadBatteryLevel MapBatteryLevel(byte percent)
        {
            if (percent >= 85) return GamepadBatteryLevel.Full;
            if (percent >= 45) return GamepadBatteryLevel.Medium;
            if (percent >= 15) return GamepadBatteryLevel.Low;
            return GamepadBatteryLevel.Empty;
        }

        /* Extended data for the contract v1.3 block. The sequence counter is
           the pad's own report counter widened service side, so a PLC
           watchdog can tell a live stream from a stuck one without dealing
           with an eight bit wrap every second. */
        public bool TryGetExtended(out GamepadExtendedState state)
        {
            if (!_connected)
            {
                state = default;
                return false;
            }
            byte percent = 0;
            bool charging = false;
            bool full = false;
            bool batteryValid = _current.BatteryKnown
                && DualSenseReport.TryDecodeBattery(_current.BatteryRaw, out percent, out charging, out full);
            state = new GamepadExtendedState(
                ExtButtons: _current.ExtButtons,
                GyroX: _current.GyroX,
                GyroY: _current.GyroY,
                GyroZ: _current.GyroZ,
                AccelX: _current.AccelX,
                AccelY: _current.AccelY,
                AccelZ: _current.AccelZ,
                Touch0: _current.Touch0,
                Touch1: _current.Touch1,
                Sequence: _extSequence,
                BatteryPercent: percent,
                BatteryCharging: charging,
                BatteryFull: full,
                BatteryValid: batteryValid);
            return true;
        }

        // Test hook so the wire mapping is verifiable without hardware
        internal void ApplySnapshot(in DualSenseState state, uint sequence = 0)
        {
            _current = state;
            _extSequence = sequence;
            _connected = true;
        }

        /* Never blocks: the newest command wins and the writer thread does
           the transport work. Dropping a superseded rumble value is correct,
           only the latest intensity matters. */
        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
            if (!_connected)
            {
                return;
            }

            byte right = GamepadMath.RumbleMotorByte(rightMotorPercent);
            byte left = GamepadMath.RumbleMotorByte(leftMotorPercent);
            lock (_rumbleSync)
            {
                _pendingRumble = (left, right);
            }
            _rumbleSignal.Set();
        }

        private void WriteLoop()
        {
            while (!_stopping)
            {
                _rumbleSignal.WaitOne(500);
                if (_stopping)
                {
                    return;
                }
                (byte Left, byte Right)? pending;
                lock (_rumbleSync)
                {
                    pending = _pendingRumble;
                    _pendingRumble = null;
                }
                if (pending is null)
                {
                    continue;
                }

                IHidChannel? channel;
                lock (_channelSync)
                {
                    channel = _channel;
                }
                if (channel is null)
                {
                    continue;
                }

                byte[] report;
                if (channel.IsBluetooth)
                {
                    report = DualSenseReport.BuildBtRumbleReport(_btOutputSequence++, pending.Value.Right, pending.Value.Left);
                }
                else
                {
                    /* Output report 0x02: flag byte 1 selects the classic
                       rumble path, byte 3 the right motor, byte 4 the left. */
                    report = new byte[OutputReportLength];
                    report[0] = UsbOutputReportId;
                    report[1] = 0x03;
                    report[3] = pending.Value.Right;
                    report[4] = pending.Value.Left;
                }
                /* Outside every lock: a pending Bluetooth write on a dying
                   link stalls only this thread, and closing the channel
                   cancels it. */
                channel.WriteReport(report);
            }
        }

        /* Shutdown has no way to abort a blocked synchronous read; a
           streaming pad returns within milliseconds and the loop then sees
           the stop flag, and a silent device is bounded by the join timeout
           with the leftover background thread dying with the process. */
        public void Dispose()
        {
            _stopping = true;
            _rumbleSignal.Set();
            CloseChannel();
            _reader.Join(TimeSpan.FromSeconds(2));
            _writer.Join(TimeSpan.FromSeconds(2));
            _rumbleSignal.Dispose();
        }

        private void CloseChannel()
        {
            lock (_channelSync)
            {
                _channel?.Dispose();
                _channel = null;
            }
        }

        /* A Bluetooth pad must be switched to its full report mode before the
           reports carry anything beyond sticks; a channel where the switch
           fails is treated as a failed open and retried. */
        private IHidChannel? OpenChannel(out bool accessDenied)
        {
            bool preferBluetooth = _retryPreferBluetooth;
            _retryPreferBluetooth = false;
            accessDenied = false;
            IHidChannel? channel;
            if (OperatingSystem.IsWindows())
            {
                channel = WindowsHidChannel.Open(WindowsPathMatch, WindowsBtPathMatch, preferBluetooth);
            }
            else
            {
                channel = LinuxHidChannel.Open(LinuxUsbHidIdMatch, LinuxBtHidIdMatch, out accessDenied, preferBluetooth);
            }
            if (channel is not null && channel.IsBluetooth && !RequestFullReports(channel))
            {
                channel.Dispose();
                return null;
            }
            return channel;
        }

        private static bool RequestFullReports(IHidChannel channel)
        {
            byte[] feature = new byte[BtModeSwitchFeatureLength];
            feature[0] = BtModeSwitchFeatureId;
            return channel.TryReadFeature(feature);
        }

        private void ReadLoop()
        {
            while (!_stopping)
            {
                IHidChannel? channel = OpenChannel(out bool accessDenied);
                if (channel is null)
                {
                    /* Permission trouble would otherwise look identical to an
                       absent pad; one warning per denial streak makes it
                       visible in the journal. An absent pad ends the streak,
                       so a replugged pad that is still denied warns again. */
                    if (accessDenied && !_accessDeniedLogged)
                    {
                        _accessDeniedLogged = true;
                        _logger.LogWarning(
                            "DualSense on slot {Slot} was found but opening it was denied. Check that the udev rule is installed and replug the pad or retrigger the rules.",
                            ControllerNumber);
                    }
                    else if (!accessDenied)
                    {
                        _accessDeniedLogged = false;
                    }
                    Thread.Sleep(ReconnectDelayMs);
                    continue;
                }
                _accessDeniedLogged = false;
                lock (_channelSync)
                {
                    _channel = channel;
                }
                _bluetoothTransport = channel.IsBluetooth;

                _logger.LogInformation(
                    "DualSense connected on slot {Slot} over {Transport}.",
                    ControllerNumber, channel.IsBluetooth ? "Bluetooth" : "USB");
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
            byte[] buffer = new byte[DualSenseReport.BtInputReportLength];
            /* Field offsets shift by one on Bluetooth, where a link sequence
               byte follows the report id. */
            int o = channel.IsBluetooth ? 1 : 0;
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
            /* The pad's eight bit report counter widened to 32 bits. Byte
               arithmetic on the delta keeps dropped reports counted and
               handles the wrap; the counter restarts with each session. */
            uint wideSequence = 0;
            byte lastSequence = 0;
            bool sequenceSeeded = false;
            int simplifiedFrames = 0;
            byte counterMask = channel.IsBluetooth ? (byte)0x0F : (byte)0xFF;

            while (!_stopping)
            {
                int read = channel.ReadReport(buffer);
                if (read < 0)
                {
                    return;
                }
                if (read == 0)
                {
                    continue;
                }
                /* A Bluetooth pad that fell back to its simplified reports,
                   for example after a link drop, carries almost no data in
                   them; ask for the full frames again instead of parsing
                   them, spaced out so a stubborn pad cannot busy loop us. */
                if (!DualSenseReport.ShouldParseFrame(channel.IsBluetooth, buffer[0]))
                {
                    if (simplifiedFrames++ % 250 == 0)
                    {
                        RequestFullReports(channel);
                    }
                    continue;
                }
                if (!DualSenseReport.TryParse(buffer.AsSpan(0, read), out DualSenseState state))
                {
                    continue;
                }
                simplifiedFrames = 0;

                if (sequenceSeeded)
                {
                    wideSequence = AdvanceWideSequence(wideSequence, lastSequence, state.Sequence, counterMask);
                }
                else
                {
                    sequenceSeeded = true;
                    wideSequence = state.Sequence;
                }
                lastSequence = state.Sequence;

                _latest = new Snapshot(state, wideSequence, Environment.TickCount64);

                buffer.AsSpan(1 + o, 6).CopyTo(controls);
                buffer.AsSpan(8 + o, 3).CopyTo(controls[6..]);
                if (!controls.SequenceEqual(lastControls))
                {
                    controls.CopyTo(lastControls);
                    lastControlChange = Environment.TickCount64;
                    frozenWarned = false;
                }
                else if (!frozenWarned && Environment.TickCount64 - lastControlChange > FrozenControlsWarnMs)
                {
                    frozenWarned = true;
                    /* Frozen controls on a USB stream usually mean the pad
                       holds a Bluetooth session, possibly with this very
                       machine now that Bluetooth is a supported transport.
                       When the open scan saw a Bluetooth node, ending the
                       session makes the next open try it first, which heals
                       the local case on its own. Without one the retry
                       could only reopen the same USB node forever, so the
                       stream stays up and the warning below tells the
                       operator what to actually do. */
                    if (!channel.IsBluetooth && channel.BluetoothAvailable)
                    {
                        _logger.LogWarning(
                            "DualSense on slot {Slot} streams USB reports but its controls are frozen, which usually means an active Bluetooth session. Trying Bluetooth.",
                            ControllerNumber);
                        _retryPreferBluetooth = true;
                        return;
                    }
                    _logger.LogWarning(
                        "DualSense on slot {Slot} streams reports but its controls have not changed for {Seconds} seconds. " +
                        "If the pad does not react, unpair it from every Bluetooth host and reconnect the cable.",
                        ControllerNumber, FrozenControlsWarnMs / 1000);
                }
            }
        }
    }
}
