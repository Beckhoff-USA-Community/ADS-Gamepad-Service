namespace AdsGamepadService.Tests
{
    /* The service refuses to start on invalid configuration instead of
       running with silently wrong values. These tests pin that gate. */
    public class ServiceOptionsTests
    {
        [Fact]
        public void DefaultsAreValidAndMatchTheHistoricalIdentity()
        {
            var options = new ServiceOptions();
            Assert.Empty(options.Validate());
            Assert.Equal(25733, options.AmsPort);
            Assert.Equal("XboxAdsServer", options.ServerName);
            Assert.Equal(4, options.MaxControllers);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        [InlineData(100000)]
        public void OutOfRangePortIsRejected(int port)
        {
            var options = new ServiceOptions { AmsPort = port };
            Assert.Contains(options.Validate(), e => e.Contains("AmsPort"));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(25733)]
        [InlineData(65535)]
        public void InRangePortIsAccepted(int port)
        {
            var options = new ServiceOptions { AmsPort = port };
            Assert.Empty(options.Validate());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyServerNameIsRejected(string name)
        {
            var options = new ServiceOptions { ServerName = name };
            Assert.Contains(options.Validate(), e => e.Contains("ServerName"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(-2)]
        public void OutOfRangeControllerCountIsRejected(int count)
        {
            var options = new ServiceOptions { MaxControllers = count };
            Assert.Contains(options.Validate(), e => e.Contains("MaxControllers"));
        }

        [Fact]
        public void AllErrorsAreReportedTogether()
        {
            var options = new ServiceOptions { AmsPort = 0, ServerName = "", MaxControllers = 9 };
            Assert.Equal(3, options.Validate().Length);
        }
    }
}
