using System.Runtime.InteropServices;
using TwinCAT.Ads;

namespace AdsGamepadService.Tests
{
    /* Service info block added in wire contract v1.1: a read of IndexGroup
       0xF000 answers with 32 bytes of version and capability data. The
       contract v1 controller blocks and error paths must stay untouched. */
    public class ServiceInfoTests
    {
        private static FakeGamepad[] FourPads(Action<FakeGamepad>? configureFirst = null)
        {
            var pads = new FakeGamepad[4];
            for (int i = 0; i < 4; ++i)
            {
                pads[i] = new FakeGamepad { ControllerNumber = i + 1 };
            }
            configureFirst?.Invoke(pads[0]);
            return pads;
        }

        [Fact]
        public void InfoStructIsExactly16Bytes()
        {
            Assert.Equal(16, Marshal.SizeOf<AdsControllerServer.AdsServiceInfo>());
        }

        [Theory]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.ContractMajor), 0)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.ContractMinor), 2)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.ServiceMajor), 4)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.ServiceMinor), 6)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.ServicePatch), 8)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.Reserved), 10)]
        [InlineData(nameof(AdsControllerServer.AdsServiceInfo.Capabilities), 12)]
        public void InfoFieldOffsetsMatchTheContract(string fieldName, int expectedOffset)
        {
            nint offset = Marshal.OffsetOf<AdsControllerServer.AdsServiceInfo>(fieldName);
            Assert.Equal(expectedOffset, (int)offset);
        }

        [Fact]
        public void ServiceVersionFieldsComeFromTheAssemblyVersion()
        {
            Version version = typeof(AdsControllerServer).Assembly.GetName().Version!;

            var info = AdsControllerServer.BuildServiceInfo();

            Assert.Equal((ushort)version.Major, info.ServiceMajor);
            Assert.Equal((ushort)version.Minor, info.ServiceMinor);
            Assert.Equal((ushort)version.Build, info.ServicePatch);
        }

        /* Every field at its contract offset, with disconnected pads on
           purpose: the info block must not depend on controller state. */
        [Fact]
        public async Task InfoReadReturnsTheFullBlockAtTheContractOffsets()
        {
            using var server = new TestableAdsControllerServer(FourPads());
            Version version = typeof(AdsControllerServer).Assembly.GetName().Version!;

            var result = await server.ReadAsync(0xF000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            byte[] data = result.Data.ToArray();
            Assert.Equal(32, data.Length);
            Assert.Equal(1, BitConverter.ToUInt16(data, 0));
            Assert.Equal(3, BitConverter.ToUInt16(data, 2));
            Assert.Equal((ushort)version.Major, BitConverter.ToUInt16(data, 4));
            Assert.Equal((ushort)version.Minor, BitConverter.ToUInt16(data, 6));
            Assert.Equal((ushort)version.Build, BitConverter.ToUInt16(data, 8));
            Assert.Equal(0, BitConverter.ToUInt16(data, 10));
            // Bit 0 XInput, bit 1 DualSense, bit 2 extended block served
            Assert.Equal(7u, BitConverter.ToUInt32(data, 12));
            Assert.Equal(new byte[16], data[16..]);
        }

        [Theory]
        [InlineData(0u, 0)]
        [InlineData(0u, 4)]
        [InlineData(0x1234u, 100)]
        [InlineData(0xFFFFu, 32)]
        public async Task InfoReadIgnoresOffsetAndRequestedLength(uint indexOffset, int readLength)
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.ReadAsync(0xF000, indexOffset, readLength);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(32, result.Data.Length);
        }

        [Theory]
        [InlineData(0xE000u)]
        [InlineData(0xF001u)]
        [InlineData(0xFFFFu)]
        [InlineData(0x1F000u)]
        public async Task GroupsNearTheInfoGroupStillReturnDeviceInvalidGroup(uint indexGroup)
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.ReadAsync(indexGroup, 0, 32);

            Assert.Equal(AdsErrorCode.DeviceInvalidGroup, result.ErrorCode);
        }

        [Fact]
        public async Task WriteToTheInfoGroupReturnsDeviceServiceNotSupported()
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.WriteAsync(0xF000, 0, new byte[8]);

            Assert.Equal(AdsErrorCode.DeviceServiceNotSupported, result.ErrorCode);
        }

        [Fact]
        public void InfoGroupMatchesNoControllerReadAndNoRumbleSum()
        {
            Assert.False(AdsControllerServer.TryGetControllerIndexForRead(0xF000u, out _));
            Assert.False(AdsControllerServer.TryGetControllerIndexForRumble(0xF000u, 0u, out _));
        }

        [Fact]
        public async Task ControllerReadsAreUntouchedByTheInfoBlock()
        {
            var pads = FourPads(p => { p.Connected = true; p.RightTrigger = 42.0f; });
            using var server = new TestableAdsControllerServer(pads);

            var info = await server.ReadAsync(0xF000, 0, 32);
            var controller = await server.ReadAsync(0x10000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, info.ErrorCode);
            Assert.Equal(AdsErrorCode.NoError, controller.ErrorCode);
            byte[] data = controller.Data.ToArray();
            Assert.Equal(32, data.Length);
            Assert.Equal(1, BitConverter.ToInt32(data, 0));
            Assert.Equal(42.0f, BitConverter.ToSingle(data, 24));
        }
    }
}
