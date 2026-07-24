using System.Runtime.InteropServices;

namespace AdsGamepadService.Tests
{
    /* The PLC reads this struct as raw bytes over ADS. These offsets are the
       frozen wire contract shared with the PLC library and can never move. */
    public class WireContractTests
    {
        [Fact]
        public void InputStructIsExactly32Bytes()
        {
            Assert.Equal(32, Marshal.SizeOf<AdsControllerServer.AdsGamepadInputs>());
        }

        [Theory]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.ControllerNumber), 0)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.LeftStickY), 4)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.LeftStickX), 8)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.RightStickY), 12)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.RightStickX), 16)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.LeftTrigger), 20)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.RightTrigger), 24)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.Buttons), 28)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadInputs.States), 30)]
        public void FieldOffsetsMatchThePlcStruct(string fieldName, int expectedOffset)
        {
            nint offset = Marshal.OffsetOf<AdsControllerServer.AdsGamepadInputs>(fieldName);
            Assert.Equal(expectedOffset, (int)offset);
        }
    }
}
