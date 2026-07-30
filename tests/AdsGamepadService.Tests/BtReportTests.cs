using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    /* Bluetooth report handling, locked against frames captured from a real
       pad. The golden frame below is a byte for byte capture of one 0x31
       input report including its checksum, so these tests prove the CRC
       implementation against the pad's own arithmetic, not against itself. */
    public class BtReportTests
    {
        /* One full mode input frame from the capture session: pad at rest,
           discharging battery at full capacity, no touches. */
        private static readonly byte[] GoldenBtFrame = Convert.FromHexString(
            "31618180817E0000010800000000" + "8C7006" + "000000000000" +
            "1B000A202E05" + "C1EDED12" + "0F" + "8000000080000000" + "00" +
            "0909" + "0000000000" + "F7F9ED12" + "0A0000" + "07297F167C7232A7" +
            "000000000000000000" + "22E4D6A6");

        [Fact]
        public void GoldenFrameIsExactlySeventyEightBytes()
        {
            Assert.Equal(DualSenseReport.BtInputReportLength, GoldenBtFrame.Length);
        }

        [Fact]
        public void GoldenBtFrameParsesWithVerifiedChecksum()
        {
            Assert.True(DualSenseReport.TryParse(GoldenBtFrame, out DualSenseState state));
            // Sticks at rest near center, from bytes 2 to 5 of the frame
            Assert.Equal((0x81 << 8 | 0x81) - 32768, state.ThumbLX);
            Assert.Equal(32767 - (0x80 << 8 | 0x80), state.ThumbLY);
            Assert.Equal(0, state.LeftTrigger);
            Assert.Equal(0, state.RightTrigger);
            Assert.Equal(0, state.WireButtons & 0x000F);
            // The discharging battery byte the ramp session never saw on USB
            Assert.True(state.BatteryKnown);
            Assert.Equal(0x0A, state.BatteryRaw);
            Assert.True(DualSenseReport.TryDecodeBattery(state.BatteryRaw, out byte percent, out bool charging, out bool full));
            Assert.Equal(100, percent);
            Assert.False(charging);
            Assert.False(full);
        }

        /* Over Bluetooth the classic counter byte is static; the pad steps
           the upper nibble of byte 1 per frame instead, measured on hardware
           at 794 frames per second with byte 8 frozen the whole time. */
        [Fact]
        public void BtSequenceComesFromTheLinkCounterNibble()
        {
            Assert.True(DualSenseReport.TryParse(GoldenBtFrame, out DualSenseState state));
            Assert.Equal(0x61 >> 4, state.Sequence);
        }

        [Fact]
        public void CorruptedBtFrameIsRejected()
        {
            byte[] frame = (byte[])GoldenBtFrame.Clone();
            frame[10] ^= 0x01;
            Assert.False(DualSenseReport.TryParse(frame, out _));
        }

        [Fact]
        public void CorruptedChecksumIsRejected()
        {
            byte[] frame = (byte[])GoldenBtFrame.Clone();
            frame[77] ^= 0x01;
            Assert.False(DualSenseReport.TryParse(frame, out _));
        }

        [Fact]
        public void ShortBtFrameIsRejected()
        {
            Assert.False(DualSenseReport.TryParse(GoldenBtFrame.AsSpan(0, 40).ToArray(), out _));
        }

        [Fact]
        public void BtRumbleReportCarriesTheDocumentedLayoutAndAValidChecksum()
        {
            byte[] report = DualSenseReport.BuildBtRumbleReport(sequence: 5, rightMotor: 0x40, leftMotor: 0x80);

            Assert.Equal(DualSenseReport.BtOutputReportLength, report.Length);
            Assert.Equal(0x31, report[0]);
            Assert.Equal(5 << 4, report[1]);
            Assert.Equal(0x10, report[2]);
            Assert.Equal(0x03, report[3]);
            Assert.Equal(0x40, report[5]);
            Assert.Equal(0x80, report[6]);

            uint expected = DualSenseCrc.Compute(DualSenseCrc.OutputPrefix, report.AsSpan(0, 74));
            uint stored = BitConverter.ToUInt32(report, 74);
            Assert.Equal(expected, stored);
        }

        [Fact]
        public void BtRumbleSequenceWrapsIntoTheUpperNibble()
        {
            byte[] report = DualSenseReport.BuildBtRumbleReport(sequence: 0x17, rightMotor: 0, leftMotor: 0);
            Assert.Equal(0x70, report[1]);
        }

        /* The transport gate is load bearing: Windows pads the simplified
           0x01 Bluetooth frame to full length, where the parser would accept
           it as a USB layout with wrong values. The gate must reject every
           non 0x31 id on Bluetooth and everything on USB must pass through. */
        [Theory]
        [InlineData(true, 0x31, true)]
        [InlineData(true, 0x01, false)]
        [InlineData(true, 0x00, false)]
        [InlineData(false, 0x01, true)]
        [InlineData(false, 0x31, true)]
        public void TransportGateOnlyPassesFullFramesOnBluetooth(bool isBluetooth, byte reportId, bool expected)
        {
            Assert.Equal(expected, DualSenseReport.ShouldParseFrame(isBluetooth, reportId));
        }

        /* Proof of why the gate exists: a padded simplified frame DOES parse
           as a USB layout and would deliver wrong stick data to the wire if
           the gate ever regressed to a length check. */
        [Fact]
        public void PaddedSimplifiedFrameWouldParseAsWrongUsbData()
        {
            byte[] padded = new byte[64];
            padded[0] = 0x01;      // simplified Bluetooth frame, padded by Windows
            padded[1] = 0x81;      // stick byte, same position as USB by luck
            padded[5] = 0x08;      // Bluetooth buttons byte where USB has the left trigger
            Assert.True(DualSenseReport.TryParse(padded, out DualSenseState state));
            Assert.Equal(8, state.LeftTrigger);
        }

        /* Over Bluetooth the classic battery bits map from the real charge
           instead of the wired constant, so a v1 program sees the drain. */
        [Theory]
        [InlineData(100, nameof(GamepadBatteryLevel.Full))]
        [InlineData(85, nameof(GamepadBatteryLevel.Full))]
        [InlineData(84, nameof(GamepadBatteryLevel.Medium))]
        [InlineData(45, nameof(GamepadBatteryLevel.Medium))]
        [InlineData(44, nameof(GamepadBatteryLevel.Low))]
        [InlineData(15, nameof(GamepadBatteryLevel.Low))]
        [InlineData(14, nameof(GamepadBatteryLevel.Empty))]
        [InlineData(5, nameof(GamepadBatteryLevel.Empty))]
        public void BluetoothBatteryLevelMapsFromTheRealCharge(byte percent, string expected)
        {
            Assert.Equal(Enum.Parse<GamepadBatteryLevel>(expected), DualSenseGamepad.MapBatteryLevel(percent));
        }

        /* The USB paths must be byte identical to before: a USB report still
           parses at offset zero. */
        [Fact]
        public void UsbFrameStillParsesAtOffsetZero()
        {
            byte[] report = new byte[64];
            report[0] = 0x01;
            report[1] = 0xFF;
            report[8] = 0x08;
            report[53] = 0x17;
            Assert.True(DualSenseReport.TryParse(report, out DualSenseState state));
            Assert.Equal((0xFF << 8 | 0xFF) - 32768, state.ThumbLX);
            Assert.Equal(0x17, state.BatteryRaw);
        }
    }
}
