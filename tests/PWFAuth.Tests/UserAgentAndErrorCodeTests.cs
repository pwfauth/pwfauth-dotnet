using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    public class UserAgentTests
    {
        // The package version, without the "+<commit>" suffix Source Link adds.
        private static readonly string PackageVersion = typeof(PwfClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        [Fact]
        public async Task EveryRequest_CarriesThePackageUserAgent_WithoutTouchingTheCallersHttpClient()
        {
            using var rig = new Rig(r =>
            {
                if (r.Path == FakeServer.LoginPath) return FakeServer.Enveloped(FakeServer.LoginOk());
                if (r.Path == "/api/app/info.php") return FakeServer.Enveloped("{\"success\":true}");
                return FakeServer.Plain("{\"success\":true}");
            });

            await rig.Client.LoginAsync(FakeServer.LicenseKey);             // envelope POST
            await rig.Client.GetAppInfoAsync();                              // envelope GET
            await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey);    // plain POST

            Assert.Equal(3, rig.Handler.Requests.Count);
            foreach (RecordedRequest request in rig.Handler.Requests)
            {
                string? userAgent = request.Header("User-Agent");
                Assert.NotNull(userAgent);
                Assert.StartsWith("PWFAuth-dotnet/" + PackageVersion, userAgent);
                Assert.Equal("PWFAuth-dotnet/" + PackageVersion + " (+https://pwfauth.com)", userAgent);
                Assert.Matches(new Regex(@"^PWFAuth-dotnet/\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)? \(\+https://pwfauth\.com\)$"), userAgent);
            }

            // Set per request, never on the HttpClient the caller handed in.
            Assert.Empty(rig.Http.DefaultRequestHeaders.UserAgent);
            Assert.False(rig.Http.DefaultRequestHeaders.Contains("X-App-Secret"));
        }

        [Fact]
        public void PackageVersion_HasNoCommitSuffix()
        {
            Assert.DoesNotContain("+", PackageVersion);
            Assert.StartsWith("1.", PackageVersion);
        }
    }

    public class ErrorCodeTests
    {
        [Theory]
        [InlineData(PwfErrorCodes.DeviceLimit, "DEVICE_LIMIT")]
        [InlineData(PwfErrorCodes.OpenAccessLimit, "OPEN_ACCESS_LIMIT")]
        [InlineData(PwfErrorCodes.KeyNotActive, "KEY_NOT_ACTIVE")]
        [InlineData(PwfErrorCodes.NoHwid, "NO_HWID")]
        [InlineData(PwfErrorCodes.RateLimited, "RATE_LIMITED")]
        [InlineData(PwfErrorCodes.SelfResetDisabled, "SELF_RESET_DISABLED")]
        public void NewCodes_MatchTheServer_AndDoNotEndTheSession(string constant, string wireValue)
        {
            Assert.Equal(wireValue, constant);
            Assert.False(PwfErrorCodes.EndsSession(constant));
        }

        [Theory]
        [InlineData(PwfErrorCodes.Banned)]
        [InlineData(PwfErrorCodes.NetworkLost)]
        [InlineData(PwfErrorCodes.SessionExpired)]
        public void KillSwitchCodes_StillEndTheSession(string code)
        {
            Assert.True(PwfErrorCodes.EndsSession(code));
        }

        [Fact]
        public void ClockSkew_IsItsOwnCode_AndNotANetworkFailure()
        {
            Assert.Equal("CLOCK_SKEW", PwfErrorCodes.ClockSkew);
            Assert.False(new SessionEndedEventArgs(PwfErrorCodes.ClockSkew, "clock").IsNetworkFailure);
            Assert.True(new SessionEndedEventArgs(PwfErrorCodes.NetworkLost, "network").IsNetworkFailure);
        }
    }
}
