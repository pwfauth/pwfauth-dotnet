using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>
    /// The envelope endpoints encrypt every reply once the app secret is accepted, so a
    /// plain reply claiming success must be refused while plain failures keep working.
    /// </summary>
    public class EnvelopeTransportTests
    {
        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task LoginAsync_PlainSuccess_ThrowsPwfSecurityException_AndStaysSignedOut(HttpStatusCode status)
        {
            using var rig = new Rig(_ => FakeServer.Plain(FakeServer.LoginOk(sessionId: "forged"), status));

            PwfSecurityException ex = await Assert.ThrowsAsync<PwfSecurityException>(
                () => rig.Client.LoginAsync(FakeServer.LicenseKey));

            Assert.Equal("The license server's reply was not encrypted, so it cannot be trusted.", ex.Message);
            Assert.IsAssignableFrom<PwfException>(ex);
            Assert.False(rig.Client.IsSignedIn);
            Assert.Null(rig.Client.SessionId);
            Assert.Null(rig.Client.LicenseKey);
        }

        [Fact]
        public async Task LoginAsync_EnvelopedSuccess_IsAccepted_AndSetsTheSession()
        {
            using var rig = new Rig(_ => FakeServer.Enveloped(FakeServer.LoginOk(heartbeatInterval: 45, sessionId: "sess-123")));

            PwfResponse login = await rig.Client.LoginAsync(FakeServer.LicenseKey);

            Assert.True(login.Success);
            Assert.Equal("Login successful", login.Message);
            Assert.True(rig.Client.IsSignedIn);
            Assert.Equal("sess-123", rig.Client.SessionId);
            Assert.Equal(FakeServer.LicenseKey, rig.Client.LicenseKey);
            Assert.Equal(45, rig.Client.HeartbeatIntervalSeconds);

            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(FakeServer.LoginPath, request.Path);
            Assert.Equal(FakeServer.AppSecret, request.Header("X-App-Secret"));
            Assert.True(CryptoEnvelope.LooksLikeEnvelope(request.Body!));

            using JsonDocument sent = JsonDocument.Parse(FakeServer.DecryptRequest(request));
            Assert.Equal(FakeServer.LicenseKey, sent.RootElement.GetProperty("license_key").GetString());
            Assert.Equal(FakeServer.HardwareId, sent.RootElement.GetProperty("hwid").GetString());
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.OK)]
        public async Task PlainFailure_OnAnEnvelopeEndpoint_IsReturnedAsAFailure(HttpStatusCode status)
        {
            using var rig = new Rig(_ => FakeServer.Plain(
                "{\"success\":false,\"error_code\":\"RATE_LIMITED\",\"message\":\"Too many requests.\"}", status));

            PwfResponse login = await rig.Client.LoginAsync(FakeServer.LicenseKey);

            Assert.False(login.Success);
            Assert.Equal(PwfErrorCodes.RateLimited, login.ErrorCode);
            Assert.Equal("Too many requests.", login.Message);
            Assert.False(rig.Client.IsSignedIn);
        }

        [Fact]
        public async Task Plain401WithoutASuccessField_StillThrowsPwfHttpException()
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"detail\":\"Invalid app secret\"}", HttpStatusCode.Unauthorized));

            PwfHttpException ex = await Assert.ThrowsAsync<PwfHttpException>(
                () => rig.Client.LoginAsync(FakeServer.LicenseKey));

            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("Invalid app secret", ex.ResponseSnippet);
        }

        [Fact]
        public async Task EnvelopeSealedWithAnotherSecret_ThrowsPwfCryptoException()
        {
            using var rig = new Rig(_ => FakeServer.EnvelopedWithWrongSecret(FakeServer.LoginOk()));

            await Assert.ThrowsAsync<PwfCryptoException>(() => rig.Client.LoginAsync(FakeServer.LicenseKey));
            Assert.False(rig.Client.IsSignedIn);
        }

        [Fact]
        public async Task GetEnvelope_PlainSuccess_ThrowsPwfSecurityException()
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"success\":true,\"app\":{\"name\":\"Forged\"}}"));

            await Assert.ThrowsAsync<PwfSecurityException>(() => rig.Client.GetAppInfoAsync());

            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/app/info.php", request.Path);
        }

        [Fact]
        public async Task GetEnvelope_EnvelopedSuccess_IsAccepted()
        {
            using var rig = new Rig(_ => FakeServer.Enveloped("{\"success\":true,\"app\":{\"name\":\"Real App\"}}"));

            PwfResponse info = await rig.Client.GetAppInfoAsync();

            Assert.True(info.Success);
            JsonElement app;
            Assert.True(info.TryGetProperty("app", out app));
            Assert.Equal("Real App", app.GetProperty("name").GetString());
        }

        [Fact]
        public async Task PostEnvelopeAsync_OnAnyPath_RefusesAPlainSuccess()
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"success\":true}"));

            await Assert.ThrowsAsync<PwfSecurityException>(() => rig.Client.PostEnvelopeAsync(
                "/api/app/not-wrapped-yet.php", new Dictionary<string, object?> { ["x"] = 1 }));
        }

        [Fact]
        public async Task HeartbeatAsync_PlainSuccess_ThrowsPwfSecurityException()
        {
            using var rig = new Rig(r => r.Path == FakeServer.LoginPath
                ? FakeServer.Enveloped(FakeServer.LoginOk())
                : FakeServer.Plain("{\"success\":true,\"message\":\"Heartbeat OK\"}"));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);

            await Assert.ThrowsAsync<PwfSecurityException>(() => rig.Client.HeartbeatAsync());
        }

        [Fact]
        public async Task LogoutAsync_PlainSuccess_IsNotTrusted_ButTheLocalSessionStillEnds()
        {
            using var rig = new Rig(r => r.Path == FakeServer.LoginPath
                ? FakeServer.Enveloped(FakeServer.LoginOk())
                : FakeServer.Plain("{\"success\":true,\"message\":\"Logged out successfully\"}"));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);

            PwfResponse? logout = await rig.Client.LogoutAsync();

            Assert.Null(logout);   // PwfSecurityException is a PwfException, which LogoutAsync absorbs
            Assert.False(rig.Client.IsSignedIn);
        }

        [Fact]
        public async Task PlainEndpoints_StillAcceptPlainSuccess()
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"success\":true,\"license_key\":\"TRIAL-12345-ABCDE-FGHIJ\"}"));

            PwfResponse trial = await rig.Client.CreateTrialAsync();
            PwfResponse direct = await rig.Client.PostPlainAsync("/api/auth/trial.php", new Dictionary<string, object?>());

            Assert.True(trial.Success);
            Assert.True(direct.Success);
        }
    }
}
