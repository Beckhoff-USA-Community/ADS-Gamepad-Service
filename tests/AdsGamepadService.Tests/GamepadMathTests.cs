using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    /* Characterization tests for the conversion math. The expected values
       replicate the retired C++ XInput wrapper. If one of these tests fails,
       the change alters the numbers every deployed PLC program receives and
       must ship as a versioned contract change, not a refactor. */
    public class GamepadMathTests
    {
        [Fact]
        public void StickInsideDeadzoneReadsZero()
        {
            Assert.Equal(0.0f, GamepadMath.StickPercent(0));
            Assert.Equal(0.0f, GamepadMath.StickPercent(7849));
            Assert.Equal(0.0f, GamepadMath.StickPercent(-7849));
            Assert.Equal(0.0f, GamepadMath.StickPercent(4000));
            Assert.Equal(0.0f, GamepadMath.StickPercent(-4000));
        }

        [Fact]
        public void StickOutsideDeadzoneIsRawScaledWithNoRenormalization()
        {
            // The output steps from zero straight to the scaled raw value
            Assert.Equal((7850 / 32768.0f) * 100.0f, GamepadMath.StickPercent(7850));
            Assert.Equal((-7850 / 32768.0f) * 100.0f, GamepadMath.StickPercent(-7850));
            Assert.Equal((20000 / 32768.0f) * 100.0f, GamepadMath.StickPercent(20000));
            Assert.Equal((32767 / 32768.0f) * 100.0f, GamepadMath.StickPercent(32767));
            Assert.Equal(-100.0f, GamepadMath.StickPercent(-32768));
        }

        [Fact]
        public void TriggerAtOrBelowThresholdReadsZero()
        {
            Assert.Equal(0.0f, GamepadMath.TriggerPercent(0));
            Assert.Equal(0.0f, GamepadMath.TriggerPercent(29));
            Assert.Equal(0.0f, GamepadMath.TriggerPercent(30));
        }

        [Fact]
        public void TriggerAboveThresholdIsRawScaled()
        {
            Assert.Equal((31 / 255.0f) * 100.0f, GamepadMath.TriggerPercent(31));
            Assert.Equal((128 / 255.0f) * 100.0f, GamepadMath.TriggerPercent(128));
            Assert.Equal(100.0f, GamepadMath.TriggerPercent(255));
        }

        [Fact]
        public void RumbleConvertsPercentToFullMotorRange()
        {
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(0.0f));
            Assert.Equal((ushort)32767, GamepadMath.RumbleMotorSpeed(50.0f));
            Assert.Equal((ushort)65535, GamepadMath.RumbleMotorSpeed(100.0f));
        }

        [Fact]
        public void RumbleDoesNotClampOutOfRangeInput()
        {
            /* The original wrapper truncated through a 32 bit integer into a
               16 bit motor value, so out of range input wraps instead of
               clamping. The PLC library clamps before sending, which makes
               this path reachable only by other ADS clients. */
            Assert.Equal((ushort)65534, GamepadMath.RumbleMotorSpeed(200.0f));
            Assert.Equal((ushort)32769, GamepadMath.RumbleMotorSpeed(-50.0f));
            Assert.Equal((ushort)32768, GamepadMath.RumbleMotorSpeed(3276800.0f));
        }

        [Fact]
        public void RumbleBeyondIntegerRangeTurnsTheMotorOff()
        {
            /* The old native conversion produced 0x80000000 for NaN and any
               value outside the 32 bit integer range, which truncates to
               motor speed zero. Current .NET saturates instead, so this
               behavior is reproduced explicitly and locked here. */
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(4000000.0f));
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(-4000000.0f));
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(float.PositiveInfinity));
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(float.NegativeInfinity));
            Assert.Equal((ushort)0, GamepadMath.RumbleMotorSpeed(float.NaN));
        }

        [Fact]
        public void WireButtonsMasksTheTwoUnusedBits()
        {
            Assert.Equal((ushort)0xF3FF, GamepadMath.WireButtons(0xFFFF));
            Assert.Equal((ushort)0x0000, GamepadMath.WireButtons(0x0400));
            Assert.Equal((ushort)0x0000, GamepadMath.WireButtons(0x0800));
            Assert.Equal((ushort)0x8000, GamepadMath.WireButtons(0x8000));
            Assert.Equal((ushort)0x1001, GamepadMath.WireButtons(0x1001));
        }
    }
}
