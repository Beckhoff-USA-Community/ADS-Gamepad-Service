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
        byte Sequence = 0,
        byte BatteryRaw = 0,
        bool BatteryKnown = false);

    /* Standard CRC32 as the DualSense uses on its Bluetooth reports: the
       reflected 0xEDB88320 polynomial, seeded with one transport byte before
       the payload. The pad rejects Bluetooth output reports with a wrong
       checksum, and the reader drops damaged input frames the same way. */
    internal static class DualSenseCrc
    {
        private static readonly uint[] Table = BuildTable();

        // Transport prefix bytes, one per HID report direction
        internal const byte InputPrefix = 0xA1;
        internal const byte OutputPrefix = 0xA2;

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        internal static uint Compute(byte prefix, ReadOnlySpan<byte> payload)
        {
            uint crc = 0xFFFFFFFFu;
            crc = Table[(crc ^ prefix) & 0xFF] ^ (crc >> 8);
            foreach (byte b in payload)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }

    internal static class DualSenseReport
    {
        internal const byte UsbInputReportId = 0x01;
        internal const int UsbInputReportLength = 64;
        internal const byte BtInputReportId = 0x31;
        internal const int BtInputReportLength = 78;
        internal const int BtOutputReportLength = 78;
        private const byte BtOutputTag = 0x10;

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

        /* The battery byte sits at offset 53; its encoding was verified
           against a real pad through a full charge cycle. Low nibble is the
           capacity in steps of ten percent, 0 to 10. High nibble is the
           state: 0 discharging, 1 charging, 2 full. Higher state values
           are error conditions the pad can report; they read as unknown. */
        private const int BatteryReportLength = 55;
        private const int BatteryOffset = 53;

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

        /* Accepts the USB input report 0x01 and the Bluetooth full mode
           report 0x31. The Bluetooth frame carries one link sequence byte
           after the report id and then the USB payload unchanged, so both
           forms parse through the same field offsets shifted by one. A
           Bluetooth frame with a wrong checksum parses as false and is
           dropped. The simplified 0x01 form a Bluetooth pad sends before
           the mode switch must never reach this parser: Windows pads it to
           the full 78 byte report length, so it would pass a length check
           and parse here as a USB layout with wrong values. The reader
           therefore gates Bluetooth frames by report id, and that gate is
           load bearing. */
        /* The reader's transport gate: on Bluetooth only full 0x31 frames
           may reach TryParse, because Windows pads the simplified 0x01 form
           to full length where it would wrongly parse as a USB layout. */
        internal static bool ShouldParseFrame(bool isBluetooth, byte reportId)
        {
            return !isBluetooth || reportId == BtInputReportId;
        }

        internal static bool TryParse(ReadOnlySpan<byte> report, out DualSenseState state)
        {
            state = default;
            int o;
            if (report.Length >= 11 && report[0] == UsbInputReportId)
            {
                o = 0;
            }
            else if (report.Length >= BtInputReportLength && report[0] == BtInputReportId)
            {
                uint expected = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(BtInputReportLength - 4, 4));
                if (DualSenseCrc.Compute(DualSenseCrc.InputPrefix, report.Slice(0, BtInputReportLength - 4)) != expected)
                {
                    return false;
                }
                o = 1;
            }
            else
            {
                return false;
            }

            ushort buttons = HatToDPad[Math.Min(report[8 + o] & 0x0F, 8)];

            byte face = (byte)(report[8 + o] & 0xF0);
            if ((face & Cross) != 0) buttons |= WireA;
            if ((face & Circle) != 0) buttons |= WireB;
            if ((face & Square) != 0) buttons |= WireX;
            if ((face & Triangle) != 0) buttons |= WireY;

            byte b9 = report[9 + o];
            if ((b9 & L1) != 0) buttons |= WireLeftShoulder;
            if ((b9 & R1) != 0) buttons |= WireRightShoulder;
            if ((b9 & L3) != 0) buttons |= WireLeftThumb;
            if ((b9 & R3) != 0) buttons |= WireRightThumb;
            if ((b9 & Options) != 0) buttons |= WireStart | WireOptions;
            if ((b9 & Create) != 0) buttons |= WireBack | WireCreate;

            ushort extButtons = 0;
            short gyroX = 0, gyroY = 0, gyroZ = 0, accelX = 0, accelY = 0, accelZ = 0;
            GamepadTouchPoint touch0 = default, touch1 = default;
            if (report.Length >= ExtendedReportLength + o)
            {
                byte b10 = report[10 + o];
                if ((b10 & Ps) != 0) extButtons |= ExtPs;
                if ((b10 & Mute) != 0) extButtons |= ExtMute;
                if ((b10 & TouchpadClick) != 0) extButtons |= ExtTouchpadClick;

                gyroX = ReadInt16(report, 16 + o);
                gyroY = ReadInt16(report, 18 + o);
                gyroZ = ReadInt16(report, 20 + o);
                accelX = ReadInt16(report, 22 + o);
                accelY = ReadInt16(report, 24 + o);
                accelZ = ReadInt16(report, 26 + o);

                touch0 = ParseTouchPoint(report.Slice(33 + o, 4));
                touch1 = ParseTouchPoint(report.Slice(37 + o, 4));
            }

            state = new DualSenseState(
                ThumbLX: AxisToThumb(report[1 + o]),
                ThumbLY: AxisToThumbInverted(report[2 + o]),
                ThumbRX: AxisToThumb(report[3 + o]),
                ThumbRY: AxisToThumbInverted(report[4 + o]),
                LeftTrigger: report[5 + o],
                RightTrigger: report[6 + o],
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
                Sequence: report.Length >= ExtendedReportLength + o ? report[7 + o] : (byte)0,
                BatteryRaw: report.Length >= BatteryReportLength + o ? report[BatteryOffset + o] : (byte)0,
                BatteryKnown: report.Length >= BatteryReportLength + o);
            return true;
        }

        /* Builds the Bluetooth rumble report. The frame is the report id, a
           rolling sequence in the upper nibble of the next byte, a fixed tag,
           then the same payload the USB output report 0x02 carries after its
           id, padded to length with the checksum in the last four bytes.
           The pad ignores Bluetooth output reports with a wrong checksum. */
        internal static byte[] BuildBtRumbleReport(byte sequence, byte rightMotor, byte leftMotor)
        {
            byte[] report = new byte[BtOutputReportLength];
            report[0] = 0x31;
            report[1] = (byte)((sequence & 0x0F) << 4);
            report[2] = BtOutputTag;
            report[3] = 0x03; // enable both classic rumble motors
            report[5] = rightMotor;
            report[6] = leftMotor;
            uint crc = DualSenseCrc.Compute(DualSenseCrc.OutputPrefix, report.AsSpan(0, BtOutputReportLength - 4));
            report[74] = (byte)crc;
            report[75] = (byte)(crc >> 8);
            report[76] = (byte)(crc >> 16);
            report[77] = (byte)(crc >> 24);
            return report;
        }

        /* Decodes the battery byte into the wire values of the extended
           block. Capacity steps map to the middle of their ten percent band,
           the value the pad itself rounds to; a full pad always reads 100.
           An error state from the pad reads as not valid, and the block
           then serves zeroes rather than a guess. */
        internal static bool TryDecodeBattery(byte raw, out byte percent, out bool charging, out bool full)
        {
            int capacity = raw & 0x0F;
            int state = raw >> 4;
            switch (state)
            {
                case 0:
                case 1:
                    percent = (byte)Math.Min(capacity * 10 + 5, 100);
                    charging = state == 1;
                    full = false;
                    return true;
                case 2:
                    percent = 100;
                    charging = false;
                    full = true;
                    return true;
                default:
                    percent = 0;
                    charging = false;
                    full = false;
                    return false;
            }
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
