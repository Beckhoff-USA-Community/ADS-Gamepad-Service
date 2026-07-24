using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdsGamepadService.Input
{
    /* Thin interop layer over the Windows XInput 1.4 API.
       Struct layouts and constants mirror Xinput.h exactly. */
    [SupportedOSPlatform("windows")]
    internal static partial class XInputNative
    {
        internal const uint ERROR_SUCCESS = 0;

        internal const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;

        internal const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        internal const byte BATTERY_TYPE_WIRED = 0x01;
        internal const byte BATTERY_TYPE_ALKALINE = 0x02;
        internal const byte BATTERY_TYPE_NIMH = 0x03;
        internal const byte BATTERY_TYPE_UNKNOWN = 0xFF;

        internal const byte BATTERY_LEVEL_EMPTY = 0x00;
        internal const byte BATTERY_LEVEL_LOW = 0x01;
        internal const byte BATTERY_LEVEL_MEDIUM = 0x02;
        internal const byte BATTERY_LEVEL_FULL = 0x03;

        [StructLayout(LayoutKind.Sequential)]
        internal struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [LibraryImport("xinput1_4.dll")]
        internal static partial uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [LibraryImport("xinput1_4.dll")]
        internal static partial uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [LibraryImport("xinput1_4.dll")]
        internal static partial uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);
    }
}
