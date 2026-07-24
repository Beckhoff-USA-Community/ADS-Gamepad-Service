namespace AdsGamepadService.Input
{
    /* Placeholder for controller slots above the configured count. It always
       reads as disconnected, so the PLC sees the same zeroed data it would
       get for an absent controller and the wire behavior never changes. */
    internal sealed class DisabledGamepad : IGamepad
    {
        public DisabledGamepad(int controllerNumber)
        {
            ControllerNumber = controllerNumber;
        }

        public int ControllerNumber { get; }

        public bool Connected => false;

        public float LeftStickY => 0.0f;

        public float LeftStickX => 0.0f;

        public float RightStickY => 0.0f;

        public float RightStickX => 0.0f;

        public float LeftTrigger => 0.0f;

        public float RightTrigger => 0.0f;

        public ushort ButtonBits => 0;

        public GamepadBatteryType BatteryType => GamepadBatteryType.None;

        public GamepadBatteryLevel BatteryLevel => GamepadBatteryLevel.None;

        public void Update()
        {
        }

        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
        }
    }
}
