using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    /* Characterization of the DualSense USB report decoding. The golden
       vectors below are byte sequences captured from a real wired pad, so
       these tests lock the decoder to observed hardware behavior, the same
       way the XInput math is locked to the retired wrapper. Only the first
       eleven bytes matter to the decoder; the sensor tail is stubbed. */
    public class DualSenseReportTests
    {
        private static byte[] Report(byte lx = 0x80, byte ly = 0x80, byte rx = 0x80, byte ry = 0x80,
            byte l2 = 0, byte r2 = 0, byte b8 = 0x08, byte b9 = 0, byte b10 = 0)
        {
            byte[] report = new byte[DualSenseReport.UsbInputReportLength];
            report[0] = DualSenseReport.UsbInputReportId;
            report[1] = lx; report[2] = ly; report[3] = rx; report[4] = ry;
            report[5] = l2; report[6] = r2;
            report[8] = b8; report[9] = b9; report[10] = b10;
            return report;
        }

        [Fact]
        public void CapturedIdleReportDecodesToRestState()
        {
            // Captured from the pad at rest: sticks near center, hat released
            byte[] idle = Report(lx: 0x84, ly: 0x84, rx: 0x82, ry: 0x80);

            Assert.True(DualSenseReport.TryParse(idle, out DualSenseState state));
            Assert.Equal(0, state.WireButtons);
            Assert.Equal(0, state.LeftTrigger);
            Assert.Equal(0, state.RightTrigger);
            // Center bytes land within the wire deadzone on every axis
            Assert.Equal(0.0f, GamepadMath.StickPercent(state.ThumbLX));
            Assert.Equal(0.0f, GamepadMath.StickPercent(state.ThumbLY));
            Assert.Equal(0.0f, GamepadMath.StickPercent(state.ThumbRX));
            Assert.Equal(0.0f, GamepadMath.StickPercent(state.ThumbRY));
        }

        [Theory]
        // Face buttons as captured: byte 8 high nibble over the idle hat 8
        [InlineData(0x28, 0x00, 0x1000)] // Cross maps to A
        [InlineData(0x48, 0x00, 0x2000)] // Circle maps to B
        [InlineData(0x18, 0x00, 0x4000)] // Square maps to X
        [InlineData(0x88, 0x00, 0x8000)] // Triangle maps to Y
        // Byte 9 as captured
        [InlineData(0x08, 0x01, 0x0100)] // L1 maps to the left shoulder
        [InlineData(0x08, 0x02, 0x0200)] // R1 maps to the right shoulder
        [InlineData(0x08, 0x40, 0x0040)] // L3 maps to the left thumb
        [InlineData(0x08, 0x80, 0x0080)] // R3 maps to the right thumb
        [InlineData(0x08, 0x10, 0x0420)] // Create maps to Back plus bit 10
        [InlineData(0x08, 0x20, 0x0810)] // Options maps to Start plus bit 11
        public void CapturedButtonBytesMapToTheWireBits(byte b8, byte b9, ushort expected)
        {
            Assert.True(DualSenseReport.TryParse(Report(b8: b8, b9: b9), out DualSenseState state));
            Assert.Equal(expected, state.WireButtons);
        }

        [Theory]
        // Hat nibble as captured: 0 up, clockwise, 8 released
        [InlineData(0x00, 0x0001)]
        [InlineData(0x01, 0x0009)]
        [InlineData(0x02, 0x0008)]
        [InlineData(0x03, 0x000A)]
        [InlineData(0x04, 0x0002)]
        [InlineData(0x05, 0x0006)]
        [InlineData(0x06, 0x0004)]
        [InlineData(0x07, 0x0005)]
        [InlineData(0x08, 0x0000)]
        public void HatNibbleDecodesToDPadBits(byte hat, ushort expected)
        {
            Assert.True(DualSenseReport.TryParse(Report(b8: hat), out DualSenseState state));
            Assert.Equal(expected, state.WireButtons);
        }

        [Theory]
        // Full sweep values observed in the capture: 0x00 and 0xFF on every axis
        [InlineData((byte)0x00, (short)-32768)]
        [InlineData((byte)0xFF, (short)32767)]
        [InlineData((byte)0x80, (short)128)]
        public void HorizontalAxesSpanTheFullThumbRange(byte raw, short expected)
        {
            Assert.True(DualSenseReport.TryParse(Report(lx: raw), out DualSenseState left));
            Assert.Equal(expected, left.ThumbLX);
            Assert.True(DualSenseReport.TryParse(Report(rx: raw), out DualSenseState right));
            Assert.Equal(expected, right.ThumbRX);
        }

        [Theory]
        // Vertical axes invert: HID top is 0, the wire is positive upward
        [InlineData((byte)0x00, (short)32767)]
        [InlineData((byte)0xFF, (short)-32768)]
        [InlineData((byte)0x80, (short)-129)]
        public void VerticalAxesInvertIntoTheThumbRange(byte raw, short expected)
        {
            Assert.True(DualSenseReport.TryParse(Report(ly: raw), out DualSenseState left));
            Assert.Equal(expected, left.ThumbLY);
            Assert.True(DualSenseReport.TryParse(Report(ry: raw), out DualSenseState right));
            Assert.Equal(expected, right.ThumbRY);
        }

        [Fact]
        public void TriggersPassThroughAsBytes()
        {
            Assert.True(DualSenseReport.TryParse(Report(l2: 0xFF, r2: 0x1E), out DualSenseState state));
            Assert.Equal(0xFF, state.LeftTrigger);
            // 0x1E is exactly the wire trigger threshold and reads as zero percent
            Assert.Equal(0x1E, state.RightTrigger);
            Assert.Equal(100.0f, GamepadMath.TriggerPercent(state.LeftTrigger));
            Assert.Equal(0.0f, GamepadMath.TriggerPercent(state.RightTrigger));
        }

        [Theory]
        [InlineData(0x31)] // Bluetooth report id, not decoded by this backend
        [InlineData(0x00)]
        public void OtherReportIdsAreRejected(byte id)
        {
            byte[] report = Report();
            report[0] = id;
            Assert.False(DualSenseReport.TryParse(report, out _));
        }

        [Fact]
        public void TruncatedReportsAreRejected()
        {
            Assert.False(DualSenseReport.TryParse(new byte[] { 0x01, 0x80, 0x80 }, out _));
        }

        [Theory]
        [InlineData(0.0f, (byte)0)]
        [InlineData(100.0f, (byte)255)]
        [InlineData(50.0f, (byte)127)]
        // Out of range input wraps through the cast instead of clamping
        [InlineData(101.0f, (byte)1)]
        [InlineData(-1.0f, (byte)254)]
        [InlineData(float.NaN, (byte)0)]
        public void RumbleMotorByteMatchesTheUnclampedConversion(float percent, byte expected)
        {
            Assert.Equal(expected, GamepadMath.RumbleMotorByte(percent));
        }
    }
}
