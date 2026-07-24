using System.Runtime.Versioning;
using static AdsGamepadService.Input.XInputNative;

namespace AdsGamepadService.Input
{
    /* Reads one Xbox controller through XInput 1.4. This replaces the retired
       C++ wrapper DLL and reproduces its numeric behavior exactly, including
       the axis naming: the values published as Y come from the XInput X axis
       and the values published as X come from the XInput Y axis. Every
       released PLC program was written against that mapping, so it stays. */
    [SupportedOSPlatform("windows")]
    internal sealed class XInputGamepad : IGamepad
    {
        /* XInput device enumeration for an absent controller is expensive, so
           a disconnected controller is probed again only after this delay. */
        private const long DisconnectedProbeIntervalMs = 1000;

        private readonly uint _userIndex;
        private XINPUT_STATE _state;
        private bool _connected;
        private GamepadBatteryType _batteryType;
        private GamepadBatteryLevel _batteryLevel;
        private long _nextProbeAt;

        public XInputGamepad(int controllerNumber)
        {
            ControllerNumber = controllerNumber;
            // XInput user indexes start at zero, the PLC contract starts at one
            _userIndex = (uint)(controllerNumber - 1);
        }

        public int ControllerNumber { get; }

        public bool Connected => _connected;

        public void Update()
        {
            long now = Environment.TickCount64;
            if (!_connected && now < _nextProbeAt)
            {
                return;
            }

            uint result = XInputGetState(_userIndex, out XINPUT_STATE state);
            if (result == ERROR_SUCCESS)
            {
                ApplySnapshot(in state);
                ReadBattery();
            }
            else
            {
                _state = default;
                _connected = false;
                _batteryType = GamepadBatteryType.None;
                _batteryLevel = GamepadBatteryLevel.None;
                _nextProbeAt = now + DisconnectedProbeIntervalMs;
            }
        }

        public float LeftStickY => GamepadMath.StickPercent(_state.Gamepad.sThumbLX);

        public float LeftStickX => GamepadMath.StickPercent(_state.Gamepad.sThumbLY);

        public float RightStickY => GamepadMath.StickPercent(_state.Gamepad.sThumbRX);

        public float RightStickX => GamepadMath.StickPercent(_state.Gamepad.sThumbRY);

        public float LeftTrigger => GamepadMath.TriggerPercent(_state.Gamepad.bLeftTrigger);

        public float RightTrigger => GamepadMath.TriggerPercent(_state.Gamepad.bRightTrigger);

        public ushort ButtonBits => GamepadMath.WireButtons(_state.Gamepad.wButtons);

        public GamepadBatteryType BatteryType => _batteryType;

        public GamepadBatteryLevel BatteryLevel => _batteryLevel;

        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
            if (!_connected)
            {
                return;
            }

            var vibration = new XINPUT_VIBRATION
            {
                wLeftMotorSpeed = GamepadMath.RumbleMotorSpeed(leftMotorPercent),
                wRightMotorSpeed = GamepadMath.RumbleMotorSpeed(rightMotorPercent),
            };
            XInputSetState(_userIndex, ref vibration);
        }

        // Test hook so the snapshot mapping is verifiable without hardware
        internal void ApplySnapshot(in XINPUT_STATE state)
        {
            _state = state;
            _connected = true;
        }

        private void ReadBattery()
        {
            uint result = XInputGetBatteryInformation(_userIndex, BATTERY_DEVTYPE_GAMEPAD, out XINPUT_BATTERY_INFORMATION info);
            if (result != ERROR_SUCCESS)
            {
                _batteryType = GamepadBatteryType.None;
                _batteryLevel = GamepadBatteryLevel.None;
                return;
            }

            _batteryType = info.BatteryType switch
            {
                BATTERY_TYPE_DISCONNECTED => GamepadBatteryType.Disconnected,
                BATTERY_TYPE_WIRED => GamepadBatteryType.Wired,
                BATTERY_TYPE_ALKALINE => GamepadBatteryType.Alkaline,
                BATTERY_TYPE_NIMH => GamepadBatteryType.Nimh,
                BATTERY_TYPE_UNKNOWN => GamepadBatteryType.Unknown,
                _ => GamepadBatteryType.None,
            };
            _batteryLevel = info.BatteryLevel switch
            {
                BATTERY_LEVEL_EMPTY => GamepadBatteryLevel.Empty,
                BATTERY_LEVEL_LOW => GamepadBatteryLevel.Low,
                BATTERY_LEVEL_MEDIUM => GamepadBatteryLevel.Medium,
                BATTERY_LEVEL_FULL => GamepadBatteryLevel.Full,
                _ => GamepadBatteryLevel.None,
            };
        }
    }
}
