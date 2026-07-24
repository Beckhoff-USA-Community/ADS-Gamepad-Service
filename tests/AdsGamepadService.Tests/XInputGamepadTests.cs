using AdsGamepadService.Input;
using static AdsGamepadService.Input.XInputNative;

namespace AdsGamepadService.Tests
{
    /* Pins the snapshot to property mapping, most importantly the historical
       axis naming: the value published as Y comes from the XInput X axis and
       the value published as X comes from the XInput Y axis. Every released
       PLC program was written against that mapping. */
    public class XInputGamepadTests
    {
        private static XInputGamepad GamepadWithSnapshot(XINPUT_GAMEPAD gamepad)
        {
            var pad = new XInputGamepad(1);
            var state = new XINPUT_STATE { dwPacketNumber = 1, Gamepad = gamepad };
            pad.ApplySnapshot(in state);
            return pad;
        }

        [Fact]
        public void PublishedYAxesComeFromXInputXAxes()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                sThumbLX = 20000,
                sThumbLY = 0,
                sThumbRX = -20000,
                sThumbRY = 0,
            });

            Assert.Equal(GamepadMath.StickPercent(20000), pad.LeftStickY);
            Assert.Equal(0.0f, pad.LeftStickX);
            Assert.Equal(GamepadMath.StickPercent(-20000), pad.RightStickY);
            Assert.Equal(0.0f, pad.RightStickX);
        }

        [Fact]
        public void PublishedXAxesComeFromXInputYAxes()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                sThumbLX = 0,
                sThumbLY = 15000,
                sThumbRX = 0,
                sThumbRY = -15000,
            });

            Assert.Equal(0.0f, pad.LeftStickY);
            Assert.Equal(GamepadMath.StickPercent(15000), pad.LeftStickX);
            Assert.Equal(0.0f, pad.RightStickY);
            Assert.Equal(GamepadMath.StickPercent(-15000), pad.RightStickX);
        }

        [Fact]
        public void TriggersAndButtonsMapStraightThrough()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                bLeftTrigger = 200,
                bRightTrigger = 15,
                wButtons = 0x1011,
            });

            Assert.Equal(GamepadMath.TriggerPercent(200), pad.LeftTrigger);
            Assert.Equal(0.0f, pad.RightTrigger);
            Assert.Equal((ushort)0x1011, pad.ButtonBits);
        }

        [Fact]
        public void SnapshotMarksTheControllerConnected()
        {
            var pad = GamepadWithSnapshot(default);
            Assert.True(pad.Connected);
            Assert.Equal(1, pad.ControllerNumber);
        }

        /* Each of the 14 defined buttons pinned one at a time, so a future
           backend that rebuilds the button word per button cannot transpose
           two of them without a test failing. */
        [Theory]
        [InlineData(0x0001)]
        [InlineData(0x0002)]
        [InlineData(0x0004)]
        [InlineData(0x0008)]
        [InlineData(0x0010)]
        [InlineData(0x0020)]
        [InlineData(0x0040)]
        [InlineData(0x0080)]
        [InlineData(0x0100)]
        [InlineData(0x0200)]
        [InlineData(0x1000)]
        [InlineData(0x2000)]
        [InlineData(0x4000)]
        [InlineData(0x8000)]
        public void EachButtonBitMapsToItsOwnWireBit(int mask)
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD { wButtons = (ushort)mask });
            Assert.Equal((ushort)mask, pad.ButtonBits);
        }

        [Fact]
        public void UnusedButtonBitsAreMaskedAtTheGamepadLevel()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD { wButtons = 0xFFFF });
            Assert.Equal((ushort)0xF3FF, pad.ButtonBits);
        }

        /* Raw 8000 sits between the left stick deadzone constant 7849 and the
           standard right stick constant 8689. The original service gated all
           four axes on the left stick constant, so 8000 must read nonzero on
           every axis. A rewrite that corrects the right stick to 8689 breaks
           these assertions, which is exactly the point. */
        [Fact]
        public void AllFourAxesUseTheLeftStickDeadzoneConstant()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                sThumbLX = 8000,
                sThumbLY = 8000,
                sThumbRX = 8000,
                sThumbRY = -8000,
            });

            float expected = GamepadMath.StickPercent(8000);
            Assert.NotEqual(0.0f, expected);
            Assert.Equal(expected, pad.LeftStickY);
            Assert.Equal(expected, pad.LeftStickX);
            Assert.Equal(expected, pad.RightStickY);
            Assert.Equal(GamepadMath.StickPercent(-8000), pad.RightStickX);
        }

        [Fact]
        public void DeadzoneBoundaryReadsZeroOnEveryAxis()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                sThumbLX = 7849,
                sThumbLY = -7849,
                sThumbRX = 7849,
                sThumbRY = -7849,
            });

            Assert.Equal(0.0f, pad.LeftStickY);
            Assert.Equal(0.0f, pad.LeftStickX);
            Assert.Equal(0.0f, pad.RightStickY);
            Assert.Equal(0.0f, pad.RightStickX);
        }

        [Fact]
        public void EachTriggerRoutesThroughItsOwnThreshold()
        {
            var pad = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                bLeftTrigger = 31,
                bRightTrigger = 30,
            });

            Assert.Equal(GamepadMath.TriggerPercent(31), pad.LeftTrigger);
            Assert.Equal(0.0f, pad.RightTrigger);

            var flipped = GamepadWithSnapshot(new XINPUT_GAMEPAD
            {
                bLeftTrigger = 30,
                bRightTrigger = 31,
            });

            Assert.Equal(0.0f, flipped.LeftTrigger);
            Assert.Equal(GamepadMath.TriggerPercent(31), flipped.RightTrigger);
        }
    }
}
