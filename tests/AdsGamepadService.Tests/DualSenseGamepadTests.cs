using AdsGamepadService.Input;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdsGamepadService.Tests
{
    /* Locks the wire facing rules of the DualSense backend through the same
       kind of snapshot hook the XInput backend tests use, so no hardware is
       needed and no reader thread has to run. */
    public class DualSenseGamepadTests
    {
        private static DualSenseGamepad Pad(in DualSenseState state)
        {
            var pad = new DualSenseGamepad(3, NullLogger.Instance);
            pad.ApplySnapshot(in state);
            return pad;
        }

        /* The axis swap: the published Y properties read the physical X
           values and the published X properties the physical Y values,
           identical to the XInput backend, so both pad families move a
           program the same way. */
        [Fact]
        public void PublishedAxesCarryTheSwappedPhysicalAxes()
        {
            var state = new DualSenseState(
                ThumbLX: 32767, ThumbLY: -32768, ThumbRX: 16000, ThumbRY: -16000,
                LeftTrigger: 0, RightTrigger: 0, WireButtons: 0);
            using var pad = Pad(in state);

            Assert.Equal(GamepadMath.StickPercent(32767), pad.LeftStickY);
            Assert.Equal(GamepadMath.StickPercent(-32768), pad.LeftStickX);
            Assert.Equal(GamepadMath.StickPercent(16000), pad.RightStickY);
            Assert.Equal(GamepadMath.StickPercent(-16000), pad.RightStickX);
        }

        [Fact]
        public void ConnectedPadReportsWiredFullBattery()
        {
            using var pad = Pad(new DualSenseState(0, 0, 0, 0, 0, 0, 0));
            Assert.Equal(GamepadBatteryType.Wired, pad.BatteryType);
            Assert.Equal(GamepadBatteryLevel.Full, pad.BatteryLevel);
        }

        [Fact]
        public void ButtonBitsPassThroughUnmasked()
        {
            using var pad = Pad(new DualSenseState(0, 0, 0, 0, 0, 0, WireButtons: 0x0C20));
            Assert.Equal(0x0C20, pad.ButtonBits);
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(499, 0, true)]
        [InlineData(500, 0, false)]
        [InlineData(10000, 9600, true)]
        public void SnapshotFreshnessGatesTheConnectedState(long now, long reportTick, bool fresh)
        {
            Assert.Equal(fresh, DualSenseGamepad.IsFresh(now, reportTick));
        }

        [Fact]
        public void UpdateWithoutAnyReportReadsDisconnectedZeros()
        {
            using var pad = new DualSenseGamepad(3, NullLogger.Instance);
            pad.Update();

            Assert.False(pad.Connected);
            Assert.Equal(0, pad.ButtonBits);
            Assert.Equal(0.0f, pad.LeftStickY);
            Assert.Equal(GamepadBatteryType.None, pad.BatteryType);
            Assert.Equal(GamepadBatteryLevel.None, pad.BatteryLevel);
        }
    }
}
