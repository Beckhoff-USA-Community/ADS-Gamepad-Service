using AdsGamepadService.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TwinCAT.Ads;
using TwinCAT.Ads.Server;

namespace AdsGamepadService.Tests
{
    /* Exposes the protected ADS request handlers so the frozen request and
       response behavior can be exercised without a router connection. */
    internal sealed class TestableAdsControllerServer : AdsControllerServer
    {
        private static readonly AmsAddress Sender = new AmsAddress(AmsNetId.Parse("1.2.3.4.5.6"), 851);

        public TestableAdsControllerServer(IGamepad[] gamepads)
            : base(25733, "TestServer", NullLoggerFactory.Instance, gamepads)
        {
        }

        public TestableAdsControllerServer(int maxControllers)
            : base(25733, "TestServer", NullLoggerFactory.Instance, maxControllers)
        {
        }

        public Task<ResultReadBytes> ReadAsync(uint indexGroup, uint indexOffset, int readLength)
            => OnReadAsync(Sender, 1, indexGroup, indexOffset, readLength, CancellationToken.None);

        public Task<ResultWrite> WriteAsync(uint indexGroup, uint indexOffset, byte[] writeData)
            => OnWriteAsync(Sender, 1, indexGroup, indexOffset, writeData, CancellationToken.None);
    }

    /* Handler level checks of the frozen wire behavior: error code selection,
       the disconnected read answering success with zeros, ignored read
       arguments, rumble byte order, and the update side effect on writes. */
    public class HandlerTests
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
        public async Task ReadOfConnectedControllerReturnsThePayloadBytes()
        {
            var pads = FourPads(p => { p.Connected = true; p.LeftTrigger = 55.0f; });
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.ReadAsync(0x10000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(32, result.Data.Length);
            Assert.Equal(1, BitConverter.ToInt32(result.Data.ToArray(), 0));
            Assert.Equal(55.0f, BitConverter.ToSingle(result.Data.ToArray(), 20));
        }

        [Theory]
        [InlineData(0x1234u, 4)]
        [InlineData(0u, 100)]
        [InlineData(0xFFFFu, 0)]
        public async Task ReadIgnoresOffsetAndRequestedLength(uint indexOffset, int readLength)
        {
            var pads = FourPads(p => p.Connected = true);
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.ReadAsync(0x10000, indexOffset, readLength);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(32, result.Data.Length);
        }

        [Fact]
        public async Task DisconnectedReadReturnsSuccessWithAllZeros()
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.ReadAsync(0x30000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(new byte[32], result.Data.ToArray());
        }

        [Fact]
        public async Task UnknownReadGroupReturnsDeviceInvalidGroup()
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.ReadAsync(0x50000, 0, 32);

            Assert.Equal(AdsErrorCode.DeviceInvalidGroup, result.ErrorCode);
        }

        [Fact]
        public async Task RumbleWriteReachesTheControllerWithLeftMotorFirst()
        {
            var pads = FourPads(p => p.Connected = true);
            using var server = new TestableAdsControllerServer(pads);
            byte[] data = new byte[8];
            BitConverter.GetBytes(25.5f).CopyTo(data, 0);
            BitConverter.GetBytes(75.25f).CopyTo(data, 4);

            var result = await server.WriteAsync(0x10000, 0x10, data);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal((25.5f, 75.25f), pads[0].LastRumble);
        }

        [Fact]
        public async Task RumbleWritePassesOutOfRangeValuesUnclamped()
        {
            var pads = FourPads(p => p.Connected = true);
            using var server = new TestableAdsControllerServer(pads);
            byte[] data = new byte[8];
            BitConverter.GetBytes(200.0f).CopyTo(data, 0);
            BitConverter.GetBytes(-50.0f).CopyTo(data, 4);

            var result = await server.WriteAsync(0x10000, 0x10, data);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal((200.0f, -50.0f), pads[0].LastRumble);
        }

        [Theory]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(0)]
        public async Task WrongLengthRumbleWriteReturnsDeviceInvalidParam(int length)
        {
            var pads = FourPads(p => p.Connected = true);
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.WriteAsync(0x10000, 0x10, new byte[length]);

            Assert.Equal(AdsErrorCode.DeviceInvalidParam, result.ErrorCode);
            Assert.Null(pads[0].LastRumble);
        }

        [Fact]
        public async Task RumbleToDisconnectedControllerReturnsDeviceInvalidParam()
        {
            var pads = FourPads();
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.WriteAsync(0x10000, 0x10, new byte[8]);

            Assert.Equal(AdsErrorCode.DeviceInvalidParam, result.ErrorCode);
            Assert.Null(pads[0].LastRumble);
        }

        [Fact]
        public async Task UnknownWriteReturnsDeviceServiceNotSupported()
        {
            var pads = FourPads(p => p.Connected = true);
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.WriteAsync(0x10000, 0x11, new byte[8]);

            Assert.Equal(AdsErrorCode.DeviceServiceNotSupported, result.ErrorCode);
        }

        [Fact]
        public async Task AnyWriteUpdatesAllFourControllers()
        {
            var pads = FourPads();
            using var server = new TestableAdsControllerServer(pads);

            await server.WriteAsync(0x70000, 0, new byte[8]);

            Assert.All(pads, p => Assert.Equal(1, p.UpdateCalls));
        }

        /* With MaxControllers reduced, the slots above the count go through
           the real construction path and must still answer reads with the
           normal disconnected payload, never with an error. */
        [Fact]
        public async Task SlotsAboveTheConfiguredCountAnswerAsDisconnected()
        {
            using var server = new TestableAdsControllerServer(2);

            var third = await server.ReadAsync(0x30000, 0, 32);
            var fourth = await server.ReadAsync(0x40000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, third.ErrorCode);
            Assert.Equal(new byte[32], third.Data.ToArray());
            Assert.Equal(AdsErrorCode.NoError, fourth.ErrorCode);
            Assert.Equal(new byte[32], fourth.Data.ToArray());
        }
    }
}
