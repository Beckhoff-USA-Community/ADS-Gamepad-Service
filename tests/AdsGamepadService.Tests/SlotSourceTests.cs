using AdsGamepadService;
using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    public class SlotSourceTests
    {
        private static ServiceOptions Options(params string[] sources)
        {
            return new ServiceOptions { SlotSources = sources };
        }

        [Fact]
        public void DefaultConfigurationValidatesWithoutSlotSources()
        {
            Assert.Empty(new ServiceOptions().Validate());
        }

        [Theory]
        [InlineData("XInput", "XInput", "XInput", "XInput")]
        [InlineData("DualSense")]
        [InlineData("xinput", "dualsense")] // case does not matter
        public void ValidSourceListsPass(params string[] sources)
        {
            Assert.Empty(Options(sources).Validate());
        }

        [Fact]
        public void UnknownSourceIsRejected()
        {
            string[] errors = Options("XInput", "Gamecube").Validate();
            Assert.Contains(errors, e => e.Contains("Gamecube"));
        }

        [Fact]
        public void TwoDualSenseSlotsAreRejected()
        {
            string[] errors = Options("DualSense", "DualSense").Validate();
            Assert.Contains(errors, e => e.Contains("one slot"));
        }

        [Fact]
        public void FiveEntriesAreRejected()
        {
            string[] errors = Options("XInput", "XInput", "XInput", "XInput", "XInput").Validate();
            Assert.Contains(errors, e => e.Contains("4 slots"));
        }

        /* Bits 10 and 11 must reach the wire untouched from contract 1.2 on.
           The server takes ButtonBits as the backend delivers them; only the
           XInput backend masks, inside its own mapping. */
        [Fact]
        public async Task BackendButtonBitsTenAndElevenReachTheWire()
        {
            var pads = new IGamepad[]
            {
                new FakeGamepad { ControllerNumber = 1, Connected = true, ButtonBits = 0x0C20 },
                new FakeGamepad { ControllerNumber = 2 },
                new FakeGamepad { ControllerNumber = 3 },
                new FakeGamepad { ControllerNumber = 4 },
            };
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.ReadAsync(0x10000, 0, 32);

            byte[] data = result.Data.ToArray();
            Assert.Equal(0x0C20, BitConverter.ToUInt16(data, 28));
        }
    }
}
