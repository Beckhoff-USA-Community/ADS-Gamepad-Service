using System.Runtime.InteropServices;
using AdsGamepadService.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TwinCAT.Ads;

namespace AdsGamepadService.Tests
{
    /* A backend that supplies extended data, for driving the v1.3 block
       through the same handler path the DualSense backend uses. */
    internal sealed class FakeExtendedGamepad : IGamepad, IExtendedGamepad
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
        public GamepadExtendedState Extended { get; set; }

        public void Update()
        {
        }

        public void Rumble(float leftMotorPercent, float rightMotorPercent)
        {
        }

        public bool TryGetExtended(out GamepadExtendedState state)
        {
            state = Connected ? Extended : default;
            return Connected;
        }
    }

    /* Extended block added in wire contract v1.3: a read of IndexGroup
       controller number times 0x10000 plus 0x100 answers with 96 bytes.
       The v1 controller blocks, the rumble path, and the info block must
       stay untouched. */
    public class ExtendedBlockTests
    {
        private static IGamepad[] FourPads(IGamepad? first = null)
        {
            var pads = new IGamepad[4];
            for (int i = 0; i < 4; ++i)
            {
                pads[i] = new FakeGamepad { ControllerNumber = i + 1 };
            }
            if (first is not null)
            {
                pads[0] = first;
            }
            return pads;
        }

        private static GamepadExtendedState SampleExtended()
        {
            return new GamepadExtendedState(
                ExtButtons: DualSenseReport.ExtPs | DualSenseReport.ExtTouchpadClick,
                GyroX: 1234,
                GyroY: -1234,
                GyroZ: 42,
                AccelX: -42,
                AccelY: 8100,
                AccelZ: -8100,
                Touch0: new GamepadTouchPoint(Active: true, ContactId: 5, X: 1919, Y: 1079),
                Touch1: default,
                Sequence: 0x00012345);
        }

        [Fact]
        public void ExtStructIsExactly40Bytes()
        {
            Assert.Equal(40, Marshal.SizeOf<AdsControllerServer.AdsGamepadExtInputs>());
        }

        [Theory]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.ExtSize), 0)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.ExtLayoutVersion), 2)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.ExtButtons), 4)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.ExtFlags), 6)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.GyroX), 8)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.GyroY), 10)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.GyroZ), 12)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.AccelX), 14)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.AccelY), 16)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.AccelZ), 18)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.Touch0), 20)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.Touch1), 26)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.Sequence), 32)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.BatteryPercent), 36)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.BatteryFlags), 37)]
        [InlineData(nameof(AdsControllerServer.AdsGamepadExtInputs.Reserved), 38)]
        public void ExtFieldOffsetsMatchTheContract(string fieldName, int expectedOffset)
        {
            nint offset = Marshal.OffsetOf<AdsControllerServer.AdsGamepadExtInputs>(fieldName);
            Assert.Equal(expectedOffset, (int)offset);
        }

        [Theory]
        [InlineData(0x10100u, 0)]
        [InlineData(0x20100u, 1)]
        [InlineData(0x30100u, 2)]
        [InlineData(0x40100u, 3)]
        public void ExtGroupsSelectTheirController(uint indexGroup, int expectedIndex)
        {
            Assert.True(AdsControllerServer.TryGetControllerIndexForExtRead(indexGroup, out int index));
            Assert.Equal(expectedIndex, index);
        }

        [Theory]
        [InlineData(0x100u)]
        [InlineData(0x50100u)]
        [InlineData(0x10101u)]
        [InlineData(0x10000u)]
        [InlineData(0x10010u)]
        [InlineData(0xF000u)]
        public void OtherGroupsDoNotMatchTheExtRead(uint indexGroup)
        {
            Assert.False(AdsControllerServer.TryGetControllerIndexForExtRead(indexGroup, out _));
        }

        /* The new groups must stay invisible to every v1 matcher, and the
           v1 groups invisible to the new one, or the contract would shift. */
        [Fact]
        public void ExtGroupsMatchNoControllerReadAndNoRumbleSum()
        {
            for (uint number = 1; number <= 4; ++number)
            {
                uint group = number * 0x10000u + 0x100u;
                Assert.False(AdsControllerServer.TryGetControllerIndexForRead(group, out _));
                Assert.False(AdsControllerServer.TryGetControllerIndexForRumble(group, 0u, out _));
            }
        }

        [Fact]
        public async Task ExtReadOnAnExtendedPadReturnsTheFieldsAtTheContractOffsets()
        {
            var pad = new FakeExtendedGamepad { ControllerNumber = 1, Connected = true, Extended = SampleExtended() };
            using var server = new TestableAdsControllerServer(FourPads(pad));

            var result = await server.ReadAsync(0x10100, 0, 96);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            byte[] data = result.Data.ToArray();
            Assert.Equal(96, data.Length);
            Assert.Equal(96, BitConverter.ToUInt16(data, 0));
            Assert.Equal(1, BitConverter.ToUInt16(data, 2));
            Assert.Equal(DualSenseReport.ExtPs | DualSenseReport.ExtTouchpadClick, BitConverter.ToUInt16(data, 4));
            Assert.Equal(1, BitConverter.ToUInt16(data, 6));
            Assert.Equal(1234, BitConverter.ToInt16(data, 8));
            Assert.Equal(-1234, BitConverter.ToInt16(data, 10));
            Assert.Equal(42, BitConverter.ToInt16(data, 12));
            Assert.Equal(-42, BitConverter.ToInt16(data, 14));
            Assert.Equal(8100, BitConverter.ToInt16(data, 16));
            Assert.Equal(-8100, BitConverter.ToInt16(data, 18));
            Assert.Equal(1, data[20]);
            Assert.Equal(5, data[21]);
            Assert.Equal(1919, BitConverter.ToUInt16(data, 22));
            Assert.Equal(1079, BitConverter.ToUInt16(data, 24));
            Assert.Equal(new byte[6], data[26..32]);
            Assert.Equal(0x00012345u, BitConverter.ToUInt32(data, 32));
            // Battery bytes are reserved zeroes until their semantics are pinned
            Assert.Equal(0, data[36]);
            Assert.Equal(0, data[37]);
            Assert.Equal(new byte[58], data[38..]);
        }

        [Fact]
        public async Task ExtReadOnAPlainBackendReturnsAllZeroesWithSuccess()
        {
            var pads = FourPads();
            ((FakeGamepad)pads[1]).Connected = true;
            using var server = new TestableAdsControllerServer(pads);

            var result = await server.ReadAsync(0x20100, 0, 96);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(new byte[96], result.Data.ToArray());
        }

        [Fact]
        public async Task ExtReadOnADisconnectedExtendedPadReturnsAllZeroesWithSuccess()
        {
            var pad = new FakeExtendedGamepad { ControllerNumber = 1, Connected = false, Extended = SampleExtended() };
            using var server = new TestableAdsControllerServer(FourPads(pad));

            var result = await server.ReadAsync(0x10100, 0, 96);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(new byte[96], result.Data.ToArray());
        }

        [Theory]
        [InlineData(0u, 0)]
        [InlineData(0u, 4)]
        [InlineData(0x1234u, 100)]
        [InlineData(0xFFFFu, 96)]
        public async Task ExtReadIgnoresOffsetAndRequestedLength(uint indexOffset, int readLength)
        {
            using var server = new TestableAdsControllerServer(FourPads());

            var result = await server.ReadAsync(0x10100, indexOffset, readLength);

            Assert.Equal(AdsErrorCode.NoError, result.ErrorCode);
            Assert.Equal(96, result.Data.Length);
        }

        [Fact]
        public async Task V1ControllerReadIsByteIdenticalBesideTheExtRead()
        {
            var pad = new FakeExtendedGamepad
            {
                ControllerNumber = 1,
                Connected = true,
                RightTrigger = 42.0f,
                Extended = SampleExtended(),
            };
            using var server = new TestableAdsControllerServer(FourPads(pad));

            var v1 = await server.ReadAsync(0x10000, 0, 32);

            Assert.Equal(AdsErrorCode.NoError, v1.ErrorCode);
            byte[] data = v1.Data.ToArray();
            Assert.Equal(32, data.Length);
            Assert.Equal(1, BitConverter.ToInt32(data, 0));
            Assert.Equal(42.0f, BitConverter.ToSingle(data, 24));
        }

        [Fact]
        public void DualSensePadPublishesItsSnapshotThroughTheExtendedInterface()
        {
            var state = new DualSenseState(
                ThumbLX: 0, ThumbLY: 0, ThumbRX: 0, ThumbRY: 0,
                LeftTrigger: 0, RightTrigger: 0, WireButtons: 0,
                ExtButtons: DualSenseReport.ExtMute,
                GyroX: -7, GyroY: 7, GyroZ: 3000,
                AccelX: 3000, AccelY: -3000, AccelZ: 99,
                Touch0: new GamepadTouchPoint(true, 12, 960, 540),
                Touch1: new GamepadTouchPoint(true, 13, 100, 200),
                Sequence: 77);
            using var pad = new DualSenseGamepad(3, NullLogger.Instance);
            pad.ApplySnapshot(in state, sequence: 500);

            Assert.True(pad.TryGetExtended(out GamepadExtendedState extended));
            Assert.Equal(DualSenseReport.ExtMute, extended.ExtButtons);
            Assert.Equal(-7, extended.GyroX);
            Assert.Equal(3000, extended.GyroZ);
            Assert.Equal(-3000, extended.AccelY);
            Assert.Equal(new GamepadTouchPoint(true, 12, 960, 540), extended.Touch0);
            Assert.Equal(new GamepadTouchPoint(true, 13, 100, 200), extended.Touch1);
            Assert.Equal(500u, extended.Sequence);
        }

        [Theory]
        [InlineData(0u, 10, 11, 1u)]
        [InlineData(100u, 10, 15, 105u)]
        [InlineData(100u, 250, 5, 111u)]
        [InlineData(0xFFFFFFFFu, 0, 1, 0u)]
        public void WideSequenceCountsDroppedReportsAndSurvivesTheWrap(uint wide, byte last, byte next, uint expected)
        {
            Assert.Equal(expected, DualSenseGamepad.AdvanceWideSequence(wide, last, next));
        }

        /* Golden vector against the captured byte positions: motion at 16 to
           27, touch packets at 33 to 40, extra buttons in byte 10, the
           report counter in byte 7. */
        [Fact]
        public void ParserDecodesTheExtendedBytesFromAFullReport()
        {
            byte[] report = new byte[64];
            report[0] = 0x01;
            report[7] = 0xAB;
            report[8] = 0x08;
            report[10] = 0x07; // PS, touchpad click, and Mute together
            WriteInt16(report, 16, -7);
            WriteInt16(report, 18, 7);
            WriteInt16(report, 20, 1000);
            WriteInt16(report, 22, -1000);
            WriteInt16(report, 24, 2000);
            WriteInt16(report, 26, -2000);
            report[33] = 0x05; // finger one: contact 5 at 1919, 1079
            report[34] = 0x7F;
            report[35] = 0x77;
            report[36] = 0x43;
            report[37] = 0x80; // finger two: lifted

            Assert.True(DualSenseReport.TryParse(report, out DualSenseState state));
            Assert.Equal(DualSenseReport.ExtPs | DualSenseReport.ExtMute | DualSenseReport.ExtTouchpadClick, state.ExtButtons);
            Assert.Equal(-7, state.GyroX);
            Assert.Equal(7, state.GyroY);
            Assert.Equal(1000, state.GyroZ);
            Assert.Equal(-1000, state.AccelX);
            Assert.Equal(2000, state.AccelY);
            Assert.Equal(-2000, state.AccelZ);
            Assert.Equal(new GamepadTouchPoint(true, 5, 1919, 1079), state.Touch0);
            Assert.Equal(default, state.Touch1);
            Assert.Equal(0xAB, state.Sequence);
        }

        /* A report long enough for the classic controls but short of the
           motion bytes keeps every extended field at its zero fail safe. */
        [Fact]
        public void ParserZeroesTheExtendedFieldsOnAShortReport()
        {
            byte[] report = new byte[11];
            report[0] = 0x01;
            report[8] = 0x08;

            Assert.True(DualSenseReport.TryParse(report, out DualSenseState state));
            Assert.Equal(0, state.ExtButtons);
            Assert.Equal(0, state.GyroX);
            Assert.Equal(default, state.Touch0);
            Assert.Equal(0, state.Sequence);
        }

        private static void WriteInt16(byte[] report, int offset, short value)
        {
            report[offset] = (byte)value;
            report[offset + 1] = (byte)(value >> 8);
        }
    }
}
