namespace AdsGamepadService.Input
{
    /* Decoded state of one DualSense USB input report, already converted into
       the value domain of the XInput backend so the shared wire math applies
       unchanged: stick axes as signed 16 bit values, triggers as bytes, and
       buttons in the wire bit layout. The byte layout was verified field by
       field against a real controller. */
    internal readonly record struct DualSenseState(
        short ThumbLX,
        short ThumbLY,
        short ThumbRX,
        short ThumbRY,
        byte LeftTrigger,
        byte RightTrigger,
        ushort WireButtons,
        ushort ExtButtons = 0,
        short GyroX = 0,
        short GyroY = 0,
        short GyroZ = 0,
        short AccelX = 0,
        short AccelY = 0,
        short AccelZ = 0,
        GamepadTouchPoint Touch0 = default,
        GamepadTouchPoint Touch1 = default,
        byte Sequence = 0);

    internal static class DualSenseReport
    {
        internal const byte UsbInputReportId = 0x01;
        internal const int UsbInputReportLength = 64;

        /* Wire button bits, contract v1 layout. Bits 0 to 9 and 12 to 15
           follow the XInput meaning. Bits 10 and 11 were reserved through
           contract 1.1; from 1.2 on the PlayStation backend publishes the
           Create and Options buttons there in addition to their mapping
           onto Back and Start, so a program can tell them apart. */
        private const ushort WireDPadUp = 0x0001;
        private const ushort WireDPadDown = 0x0002;
        private const ushort WireDPadLeft = 0x0004;
        private const ushort WireDPadRight = 0x0008;
        private const ushort WireStart = 0x0010;
        private const ushort WireBack = 0x0020;
        private const ushort WireLeftThumb = 0x0040;
        private const ushort WireRightThumb = 0x0080;
        private const ushort WireLeftShoulder = 0x0100;
        private const ushort WireRightShoulder = 0x0200;
        private const ushort WireCreate = 0x0400;
        private const ushort WireOptions = 0x0800;
        private const ushort WireA = 0x1000;
        private const ushort WireB = 0x2000;
        private const ushort WireX = 0x4000;
        private const ushort WireY = 0x8000;

        // Face button bits in the high nibble of report byte 8
        private const byte Square = 0x10;
        private const byte Cross = 0x20;
        private const byte Circle = 0x40;
        private const byte Triangle = 0x80;

        // Report byte 9
        private const byte L1 = 0x01;
        private const byte R1 = 0x02;
        private const byte L3 = 0x40;
        private const byte R3 = 0x80;
        private const byte Create = 0x10;
        private const byte Options = 0x20;

        // Report byte 10
        private const byte Ps = 0x01;
        private const byte TouchpadClick = 0x02;
        private const byte Mute = 0x04;

        /* Extended button bits, contract v1.3 layout. */
        internal const ushort ExtPs = 0x0001;
        internal const ushort ExtMute = 0x0002;
        internal const ushort ExtTouchpadClick = 0x0004;

        /* Motion and touch live past the classic control bytes; a report
           short of this length keeps the extended fields zeroed. */
        private const int ExtendedReportLength = 41;

        /* Hat nibble to dpad bits. 0 is up, values run clockwise through the
           diagonals, 8 is released. */
        private static readonly ushort[] HatToDPad =
        {
            WireDPadUp,
            WireDPadUp | WireDPadRight,
            WireDPadRight,
            WireDPadRight | WireDPadDown,
            WireDPadDown,
            WireDPadDown | WireDPadLeft,
            WireDPadLeft,
            WireDPadLeft | WireDPadUp,
            0,
        };

        internal static bool TryParse(ReadOnlySpan<byte> report, out DualSenseState state)
        {
            state = default;
            if (report.Length < 11 || report[0] != UsbInputReportId)
            {
                return false;
            }

            ushort buttons = HatToDPad[Math.Min(report[8] & 0x0F, 8)];

            byte face = (byte)(report[8] & 0xF0);
            if ((face & Cross) != 0) buttons |= WireA;
            if ((face & Circle) != 0) buttons |= WireB;
            if ((face & Square) != 0) buttons |= WireX;
            if ((face & Triangle) != 0) buttons |= WireY;

            byte b9 = report[9];
            if ((b9 & L1) != 0) buttons |= WireLeftShoulder;
            if ((b9 & R1) != 0) buttons |= WireRightShoulder;
            if ((b9 & L3) != 0) buttons |= WireLeftThumb;
            if ((b9 & R3) != 0) buttons |= WireRightThumb;
            if ((b9 & Options) != 0) buttons |= WireStart | WireOptions;
            if ((b9 & Create) != 0) buttons |= WireBack | WireCreate;

            ushort extButtons = 0;
            short gyroX = 0, gyroY = 0, gyroZ = 0, accelX = 0, accelY = 0, accelZ = 0;
            GamepadTouchPoint touch0 = default, touch1 = default;
            if (report.Length >= ExtendedReportLength)
            {
                byte b10 = report[10];
                if ((b10 & Ps) != 0) extButtons |= ExtPs;
                if ((b10 & Mute) != 0) extButtons |= ExtMute;
                if ((b10 & TouchpadClick) != 0) extButtons |= ExtTouchpadClick;

                gyroX = ReadInt16(report, 16);
                gyroY = ReadInt16(report, 18);
                gyroZ = ReadInt16(report, 20);
                accelX = ReadInt16(report, 22);
                accelY = ReadInt16(report, 24);
                accelZ = ReadInt16(report, 26);

                touch0 = ParseTouchPoint(report.Slice(33, 4));
                touch1 = ParseTouchPoint(report.Slice(37, 4));
            }

            state = new DualSenseState(
                ThumbLX: AxisToThumb(report[1]),
                ThumbLY: AxisToThumbInverted(report[2]),
                ThumbRX: AxisToThumb(report[3]),
                ThumbRY: AxisToThumbInverted(report[4]),
                LeftTrigger: report[5],
                RightTrigger: report[6],
                WireButtons: buttons,
                ExtButtons: extButtons,
                GyroX: gyroX,
                GyroY: gyroY,
                GyroZ: gyroZ,
                AccelX: accelX,
                AccelY: accelY,
                AccelZ: accelZ,
                Touch0: touch0,
                Touch1: touch1,
                Sequence: report.Length >= ExtendedReportLength ? report[7] : (byte)0);
            return true;
        }

        private static short ReadInt16(ReadOnlySpan<byte> report, int offset)
        {
            return (short)(report[offset] | (report[offset + 1] << 8));
        }

        /* One 4 byte touch packet: the top bit of the first byte set means no
           contact, the low bits count contacts; the coordinates are packed as
           twelve bits each, x low byte first, then a shared middle byte. */
        private static GamepadTouchPoint ParseTouchPoint(ReadOnlySpan<byte> packet)
        {
            bool active = (packet[0] & 0x80) == 0;
            if (!active)
            {
                return default;
            }
            return new GamepadTouchPoint(
                Active: true,
                ContactId: (byte)(packet[0] & 0x7F),
                X: (ushort)(packet[1] | ((packet[2] & 0x0F) << 8)),
                Y: (ushort)((packet[2] >> 4) | (packet[3] << 4)));
        }

        /* Byte axis to the full signed 16 bit range: 0 becomes -32768 and 255
           becomes 32767, by repeating the byte into both halves before the
           shift. The rest position near 128 lands close to zero. */
        private static short AxisToThumb(byte raw)
        {
            return (short)(((raw << 8) | raw) - 32768);
        }

        /* Same conversion for the vertical axes, inverted: HID reports 0 at
           the top while the wire contract is positive upward like XInput. */
        private static short AxisToThumbInverted(byte raw)
        {
            return (short)(32767 - ((raw << 8) | raw));
        }
    }
}
