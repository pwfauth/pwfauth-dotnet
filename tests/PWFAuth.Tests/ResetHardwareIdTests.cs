using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    public class ResetHardwareIdTests
    {
        private const string ResetOk =
            "{\"success\":true,\"message\":\"Hardware ID has been reset. You can now activate the key on a new device.\","
            + "\"next_reset_at\":\"2026-09-27T01:00:00Z\"}";

        private static string SentReason(RecordedRequest request)
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            return body.RootElement.GetProperty("reason").GetString()!;
        }

        [Fact]
        public async Task PostsKeyAndReason_WithTheAppSecret_AndParsesSuccess()
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));

            PwfResponse reset = await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey, "New laptop");

            Assert.True(reset.Success);
            Assert.Equal("Hardware ID has been reset. You can now activate the key on a new device.", reset.Message);
            Assert.Equal("2026-09-27T01:00:00Z", reset.GetString("next_reset_at"));

            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(FakeServer.BaseUrl + FakeServer.ResetPath), request.Uri);
            Assert.Equal(FakeServer.AppSecret, request.Header("X-App-Secret"));
            Assert.NotNull(request.ContentType);
            Assert.StartsWith("application/json", request.ContentType);

            // Plain JSON body (this endpoint does not speak the envelope) with exactly key + reason.
            Assert.False(CryptoEnvelope.LooksLikeEnvelope(request.Body!));
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            Assert.Equal(FakeServer.LicenseKey, body.RootElement.GetProperty("key").GetString());
            Assert.Equal("New laptop", body.RootElement.GetProperty("reason").GetString());
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task BlankReason_DefaultsToResetFromApp(string? reason)
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));

            await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey, reason);

            Assert.Equal("Reset from app", SentReason(Assert.Single(rig.Handler.Requests)));
        }

        [Fact]
        public async Task OmittedReason_DefaultsToResetFromApp()
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));

            await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey);

            Assert.Equal("Reset from app", SentReason(Assert.Single(rig.Handler.Requests)));
        }

        [Fact]
        public async Task LongReason_IsCutTo255Characters_WithoutSplittingASurrogatePair()
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));
            string plain = new string('a', 300);
            string emojiOnTheEdge = new string('b', 254) + "\U0001F600" + "tail";   // pair at 254/255

            await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey, plain);
            await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey, emojiOnTheEdge);

            Assert.Equal(new string('a', 255), SentReason(rig.Handler.Requests[0]));
            Assert.Equal(new string('b', 254), SentReason(rig.Handler.Requests[1]));
        }

        [Fact]
        public async Task KeyIsSentTrimmed()
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));

            await rig.Client.ResetHardwareIdAsync("  " + FakeServer.LicenseKey + " \r\n");

            using JsonDocument body = JsonDocument.Parse(Assert.Single(rig.Handler.Requests).Body!);
            Assert.Equal(FakeServer.LicenseKey, body.RootElement.GetProperty("key").GetString());
        }

        [Fact]
        public async Task RateLimited429_IsReturnedAsAFailure()
        {
            using var rig = new Rig(_ => FakeServer.Plain(
                "{\"success\":false,\"message\":\"HWID was reset recently. Please wait ~5 hour(s) before resetting again.\","
                + "\"error_code\":\"RATE_LIMITED\"}", HttpStatusCode.TooManyRequests));

            PwfResponse reset = await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey);

            Assert.False(reset.Success);
            Assert.Equal(PwfErrorCodes.RateLimited, reset.ErrorCode);
            Assert.Contains("5 hour(s)", reset.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, "INVALID_KEY")]
        [InlineData(HttpStatusCode.Unauthorized, "INVALID_KEY")]
        [InlineData(HttpStatusCode.BadRequest, "KEY_NOT_ACTIVE")]
        [InlineData(HttpStatusCode.BadRequest, "NO_HWID")]
        [InlineData(HttpStatusCode.Forbidden, "SELF_RESET_DISABLED")]
        public async Task EveryContractFailure_IsReturnedWithItsCode(HttpStatusCode status, string code)
        {
            using var rig = new Rig(_ => FakeServer.Plain(
                "{\"success\":false,\"message\":\"Refused.\",\"error_code\":\"" + code + "\"}", status));

            PwfResponse reset = await rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey);

            Assert.False(reset.Success);
            Assert.Equal(code, reset.ErrorCode);
            Assert.Equal("Refused.", reset.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task BlankKey_ThrowsArgumentException_WithoutSendingAnything(string? key)
        {
            using var rig = new Rig(_ => FakeServer.Plain(ResetOk));

            await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.ResetHardwareIdAsync(key!));

            Assert.Empty(rig.Handler.Requests);
        }

        [Fact]
        public void RequestHardwareResetAsync_IsObsolete_AndPointsToResetHardwareIdAsync()
        {
            MethodInfo? method = typeof(PwfClient).GetMethod("RequestHardwareResetAsync");
            Assert.NotNull(method);

            ObsoleteAttribute? obsolete = method!.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsolete);
            Assert.False(obsolete!.IsError);   // a warning, not an error: existing apps keep compiling
            Assert.Equal(
                "Reset requests are not shown in the PWF Auth dashboard yet. Use ResetHardwareIdAsync for an instant self-service reset.",
                obsolete.Message);
        }

        [Fact]
        public async Task RequestHardwareResetAsync_StillWorks()
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"success\":true,\"message\":\"Request submitted.\"}"));

#pragma warning disable CS0618 // exercising the obsolete API on purpose
            PwfResponse response = await rig.Client.RequestHardwareResetAsync(FakeServer.LicenseKey, "Old flow");
#pragma warning restore CS0618

            Assert.True(response.Success);
            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal("/api/auth/request-hwid-reset.php", request.Path);
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            Assert.Equal(FakeServer.LicenseKey, body.RootElement.GetProperty("license_key").GetString());
            Assert.Equal("Old flow", body.RootElement.GetProperty("reason").GetString());
        }
    }
}
