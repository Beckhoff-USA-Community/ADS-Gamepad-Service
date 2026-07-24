namespace AdsGamepadService.Tests
{
    /* Request routing rules as shipped since the first release. Reads select
       the controller by IndexGroup alone. Rumble writes match on the unchecked
       sum of IndexGroup and IndexOffset, which accepts any split of the sum. */
    public class DispatchTests
    {
        [Theory]
        [InlineData(0x10000u, 0)]
        [InlineData(0x20000u, 1)]
        [InlineData(0x30000u, 2)]
        [InlineData(0x40000u, 3)]
        public void ReadGroupsMapToControllers(uint indexGroup, int expectedIndex)
        {
            Assert.True(AdsControllerServer.TryGetControllerIndexForRead(indexGroup, out int index));
            Assert.Equal(expectedIndex, index);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(0x00001u)]
        [InlineData(0x10001u)]
        [InlineData(0x50000u)]
        [InlineData(0xFFFFFFFFu)]
        public void UnknownReadGroupsAreRejected(uint indexGroup)
        {
            Assert.False(AdsControllerServer.TryGetControllerIndexForRead(indexGroup, out _));
        }

        [Theory]
        [InlineData(0x10000u, 0x10u, 0)]
        [InlineData(0x20000u, 0x10u, 1)]
        [InlineData(0x30000u, 0x10u, 2)]
        [InlineData(0x40000u, 0x10u, 3)]
        public void CanonicalRumbleCommandsMapToControllers(uint indexGroup, uint indexOffset, int expectedIndex)
        {
            Assert.True(AdsControllerServer.TryGetControllerIndexForRumble(indexGroup, indexOffset, out int index));
            Assert.Equal(expectedIndex, index);
        }

        [Fact]
        public void AnySplitOfTheSumIsAccepted()
        {
            // Historical behavior: only the sum matters, not the split
            Assert.True(AdsControllerServer.TryGetControllerIndexForRumble(0x10010u, 0u, out int a));
            Assert.Equal(0, a);
            Assert.True(AdsControllerServer.TryGetControllerIndexForRumble(0x20000u, 0x10010u, out int b));
            Assert.Equal(2, b);
        }

        [Fact]
        public void SumMatchingWrapsLikeTheOriginalUncheckedAddition()
        {
            // The original used unchecked 32 bit addition, so a wrapping pair matches
            Assert.True(AdsControllerServer.TryGetControllerIndexForRumble(0xFFFFFFFFu, 0x10011u, out int index));
            Assert.Equal(0, index);
        }

        [Theory]
        [InlineData(0x10000u, 0u)]
        [InlineData(0x10000u, 0x11u)]
        [InlineData(0x50000u, 0x10u)]
        public void OtherWritesAreRejected(uint indexGroup, uint indexOffset)
        {
            Assert.False(AdsControllerServer.TryGetControllerIndexForRumble(indexGroup, indexOffset, out _));
        }
    }
}
