using AdsGamepadService.Input;

namespace AdsGamepadService.Tests
{
    /* The pure parts of the Linux channel: the ioctl request arithmetic for
       HIDIOCGFEATURE and the transport choice between a USB and a Bluetooth
       hidraw node. Neither touches the operating system, so they run on any
       test platform. */
    public class LinuxHidChannelTests
    {
        /* HIDIOCGFEATURE(len) from linux/hidraw.h expands to
           _IOC(read|write, 'H', 0x07, len); these are the expanded values
           for the lengths the service actually uses. */
        [Theory]
        [InlineData(41, 0xC0294807u)]
        [InlineData(64, 0xC0404807u)]
        [InlineData(1, 0xC0014807u)]
        public void FeatureRequestCodeMatchesKernelHeader(int length, uint expected)
        {
            Assert.Equal(expected, (uint)LinuxHidChannel.FeatureRequestCode(length));
        }

        [Fact]
        public void FeatureRequestCodeMasksOversizedLengthToFourteenBits()
        {
            // The size field is 14 bits wide; anything larger must not spill
            // into the type and direction bits.
            Assert.Equal(
                LinuxHidChannel.FeatureRequestCode(0x4001),
                LinuxHidChannel.FeatureRequestCode(1));
        }

        [Fact]
        public void UsbNodeWinsByDefault()
        {
            var choice = LinuxHidChannel.ChooseNode("/dev/hidraw0", "/dev/hidraw1", preferBluetooth: false);
            Assert.NotNull(choice);
            Assert.Equal("/dev/hidraw0", choice.Value.Node);
            Assert.False(choice.Value.IsBluetooth);
        }

        [Fact]
        public void BluetoothNodeWinsWhenPreferred()
        {
            var choice = LinuxHidChannel.ChooseNode("/dev/hidraw0", "/dev/hidraw1", preferBluetooth: true);
            Assert.NotNull(choice);
            Assert.Equal("/dev/hidraw1", choice.Value.Node);
            Assert.True(choice.Value.IsBluetooth);
        }

        [Fact]
        public void BluetoothNodeIsTheFallbackWithoutUsb()
        {
            var choice = LinuxHidChannel.ChooseNode(null, "/dev/hidraw1", preferBluetooth: false);
            Assert.NotNull(choice);
            Assert.Equal("/dev/hidraw1", choice.Value.Node);
            Assert.True(choice.Value.IsBluetooth);
        }

        /* The cell the frozen stream retry lands in: Bluetooth preferred
           after a frozen USB session ended, and only Bluetooth present. */
        [Fact]
        public void BluetoothOnlyWithPreferenceStaysBluetooth()
        {
            var choice = LinuxHidChannel.ChooseNode(null, "/dev/hidraw1", preferBluetooth: true);
            Assert.NotNull(choice);
            Assert.Equal("/dev/hidraw1", choice.Value.Node);
            Assert.True(choice.Value.IsBluetooth);
        }

        [Fact]
        public void UsbNodeIsTheFallbackWhenBluetoothPreferredButAbsent()
        {
            var choice = LinuxHidChannel.ChooseNode("/dev/hidraw0", null, preferBluetooth: true);
            Assert.NotNull(choice);
            Assert.Equal("/dev/hidraw0", choice.Value.Node);
            Assert.False(choice.Value.IsBluetooth);
        }

        [Fact]
        public void NoNodesMeansNoChoice()
        {
            Assert.Null(LinuxHidChannel.ChooseNode(null, null, preferBluetooth: false));
            Assert.Null(LinuxHidChannel.ChooseNode(null, null, preferBluetooth: true));
        }
    }
}
