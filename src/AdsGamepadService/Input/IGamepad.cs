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

    /* One touchpad contact as published in the extended block. Coordinates
       are the raw pad range, 0 to 1919 across and 0 to 1079 down. */
    internal readonly record struct GamepadTouchPoint(
        bool Active,
        byte ContactId,
        ushort X,
        ushort Y);

    /* Extended controller data beyond the classic gamepad surface, contract
       v1.3. ExtButtons uses the extended bit layout: bit 0 PS, bit 1 Mute,
       bit 2 touchpad click. Motion values are raw sensor units on purpose;
       interpreting them is application code, like everything else. */
    internal readonly record struct GamepadExtendedState(
        ushort ExtButtons,
        short GyroX,
        short GyroY,
        short GyroZ,
        short AccelX,
        short AccelY,
        short AccelZ,
        GamepadTouchPoint Touch0,
        GamepadTouchPoint Touch1,
        uint Sequence);

    /* Implemented by backends that can supply extended data. A slot whose
       backend lacks this interface answers the extended read with zeroes. */
    internal interface IExtendedGamepad
    {
        /* False while the pad is disconnected; the caller then serves the
           zeroed fail safe block like everywhere else in the contract. */
        bool TryGetExtended(out GamepadExtendedState state);
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
