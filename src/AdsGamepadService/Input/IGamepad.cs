namespace AdsGamepadService.Input
{
    /* Battery classification as published to the PLC. None means the backend
       could not read battery information, in which case no wire bit is set. */
    internal enum GamepadBatteryType
    {
        None,
        Disconnected,
        Wired,
        Alkaline,
        Nimh,
        Unknown,
    }

    internal enum GamepadBatteryLevel
    {
        None,
        Empty,
        Low,
        Medium,
        Full,
    }

    /* One physical controller as the ADS layer sees it. Update takes a fresh
       hardware snapshot; the properties then read that snapshot until the
       next Update call. Values use the units of the PLC wire contract:
       sticks are percent in the range -100 to 100, triggers 0 to 100, and
       ButtonBits uses the XInput button bit layout. */
    internal interface IGamepad
    {
        int ControllerNumber { get; }

        bool Connected { get; }

        void Update();

        float LeftStickY { get; }

        float LeftStickX { get; }

        float RightStickY { get; }

        float RightStickX { get; }

        float LeftTrigger { get; }

        float RightTrigger { get; }

        ushort ButtonBits { get; }

        GamepadBatteryType BatteryType { get; }

        GamepadBatteryLevel BatteryLevel { get; }

        void Rumble(float leftMotorPercent, float rightMotorPercent);
    }
}
