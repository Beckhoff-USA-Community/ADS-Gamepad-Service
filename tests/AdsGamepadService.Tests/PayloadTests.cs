using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    internal sealed class FakeGamepad : IGamepad
    {
        public int ControllerNumber { get; init; } = 1;
        public bool Connected { get; set; }
        public float LeftStickY { get; set; }
        public float LeftStickX { get; set; }
        public float RightStickY { get; set; }
        public float RightStickX { get; set; }
        public float LeftTrigger { get; set; }
        public float RightTrigger { get; set; }
        public ushort ButtonBits { get; set; }
        public GamepadBatteryType BatteryType { get; set; }
        public GamepadBatteryLevel BatteryLevel { get; set; }

        public int UpdateCalls { get; private set; }
        public (float Left, float Right)? LastRumble { get; private set; }

        public void Update()
        {
            UpdateCalls++;
        }

        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
            LastRumble = (leftMotorPercent, rightMotorPercent);
        }
    }

    /* End to end checks of the bytes a PLC read receives, assembled exactly
       as the original service produced them. */
    public class PayloadTests
    {
        [Fact]
        public void DisconnectedControllerSerializesToAllZeros()
        {
            var gamepad = new FakeGamepad { ControllerNumber = 2, Connected = false };
            byte[] payload = AdsControllerServer.SerializeInputs(AdsControllerServer.BuildInputs(gamepad));
            Assert.Equal(new byte[32], payload);
        }

        [Fact]
        public void ConnectedControllerSerializesFieldsAtTheContractOffsets()
        {
            var gamepad = new FakeGamepad
            {
                ControllerNumber = 3,
                Connected = true,
                LeftStickY = 12.5f,
                LeftStickX = -25.0f,
                RightStickY = 50.0f,
                RightStickX = -75.0f,
                LeftTrigger = 10.0f,
                RightTrigger = 90.0f,
                ButtonBits = 0x9001,
                BatteryType = GamepadBatteryType.Alkaline,
                BatteryLevel = GamepadBatteryLevel.Full,
            };

            byte[] payload = AdsControllerServer.SerializeInputs(AdsControllerServer.BuildInputs(gamepad));

            Assert.Equal(3, BitConverter.ToInt32(payload, 0));
            Assert.Equal(12.5f, BitConverter.ToSingle(payload, 4));
            Assert.Equal(-25.0f, BitConverter.ToSingle(payload, 8));
            Assert.Equal(50.0f, BitConverter.ToSingle(payload, 12));
            Assert.Equal(-75.0f, BitConverter.ToSingle(payload, 16));
            Assert.Equal(10.0f, BitConverter.ToSingle(payload, 20));
            Assert.Equal(90.0f, BitConverter.ToSingle(payload, 24));
            Assert.Equal(unchecked((short)0x9001), BitConverter.ToInt16(payload, 28));
            // Bit 0 connected, bit 3 alkaline, bit 9 full
            Assert.Equal((short)(1 | (1 << 3) | (1 << 9)), BitConverter.ToInt16(payload, 30));
        }

        [Fact]
        public void StatesPacksConnectedBatteryTypeAndBatteryLevelBits()
        {
            var gamepad = new FakeGamepad
            {
                Connected = true,
                BatteryType = GamepadBatteryType.Alkaline,
                BatteryLevel = GamepadBatteryLevel.Full,
            };

            var inputs = AdsControllerServer.BuildInputs(gamepad);

            // Bit 0 connected, bit 3 alkaline, bit 9 full
            Assert.Equal((short)(1 | (1 << 3) | (1 << 9)), inputs.States);
        }

        [Theory]
        [InlineData((int)GamepadBatteryType.Disconnected, 1)]
        [InlineData((int)GamepadBatteryType.Wired, 2)]
        [InlineData((int)GamepadBatteryType.Alkaline, 3)]
        [InlineData((int)GamepadBatteryType.Nimh, 4)]
        [InlineData((int)GamepadBatteryType.Unknown, 5)]
        public void BatteryTypeBitsMatchTheContract(int type, int expectedBit)
        {
            var gamepad = new FakeGamepad { Connected = true, BatteryType = (GamepadBatteryType)type };
            var inputs = AdsControllerServer.BuildInputs(gamepad);
            Assert.Equal((short)(1 | (1 << expectedBit)), inputs.States);
        }

        [Theory]
        [InlineData((int)GamepadBatteryLevel.Empty, 6)]
        [InlineData((int)GamepadBatteryLevel.Low, 7)]
        [InlineData((int)GamepadBatteryLevel.Medium, 8)]
        [InlineData((int)GamepadBatteryLevel.Full, 9)]
        public void BatteryLevelBitsMatchTheContract(int level, int expectedBit)
        {
            var gamepad = new FakeGamepad { Connected = true, BatteryLevel = (GamepadBatteryLevel)level };
            var inputs = AdsControllerServer.BuildInputs(gamepad);
            Assert.Equal((short)(1 | (1 << expectedBit)), inputs.States);
        }

        [Fact]
        public void FailedBatteryReadSetsOnlyTheConnectedBit()
        {
            var gamepad = new FakeGamepad
            {
                Connected = true,
                BatteryType = GamepadBatteryType.None,
                BatteryLevel = GamepadBatteryLevel.None,
            };

            var inputs = AdsControllerServer.BuildInputs(gamepad);
            Assert.Equal((short)1, inputs.States);
        }
    }
}
