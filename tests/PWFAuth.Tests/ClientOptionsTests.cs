using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    public class ClientOptionsTests
    {
        private static PwfClientOptions Options(TimeSpan timeout)
        {
            return new PwfClientOptions
            {
                AppSecret = FakeServer.AppSecret,
                BaseUrl = FakeServer.BaseUrl,
                HardwareId = FakeServer.HardwareId,
                Timeout = timeout,
            };
        }

        [Fact]
        public void HeartbeatBudgets_HaveTheDocumentedDefaults()
        {
            var options = new PwfClientOptions();

            Assert.Equal(3, options.MaxHeartbeatFailures);
            Assert.Equal(10, options.MaxRateLimitedBeats);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxRateLimitedBeats_BelowOne_IsRejected(int value)
        {
            PwfClientOptions options = Options(TimeSpan.FromSeconds(15));
            options.MaxRateLimitedBeats = value;

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.Equal(nameof(PwfClientOptions.MaxRateLimitedBeats), ex.ParamName);
        }

        [Fact]
        public async Task ACallersHttpClient_KeepsItsTimeout_EvenAfterItHasSentRequests()
        {
            var handler = new FakeHandler(_ => FakeServer.Plain("{}"));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(42) };
            using (await http.GetAsync(FakeServer.BaseUrl + "/warm-up")) { }   // from here on, setting Timeout throws

            using var client = new PwfClient(Options(TimeSpan.FromSeconds(5)), http);

            Assert.Equal(TimeSpan.FromSeconds(42), http.Timeout);
        }

        [Fact]
        public void TheClientsOwnHttpClient_UsesOptionsTimeout()
        {
            using var client = new PwfClient(Options(TimeSpan.FromSeconds(7)));

            var own = (HttpClient)typeof(PwfClient)
                .GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client)!;
            Assert.Equal(TimeSpan.FromSeconds(7), own.Timeout);
        }
    }
}
