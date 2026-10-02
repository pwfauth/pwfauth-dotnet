using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>
    /// Account keys (1.3.0): sign up with a license key, add keys to an account. Both endpoints
    /// speak plain JSON (no envelope), like the rest of the account calls.
    /// </summary>
    public class AccountKeysTests
    {
        private const string RegisterPath = "/api/auth/account-register.php";
        private const string RedeemPath = "/api/auth/account-redeem.php";
        private const string AccountLoginPath = "/api/auth/account-login.php";

        private const string RegisterOk =
            "{\"success\":true,\"message\":\"Account created: +30 days from your key.\","
            + "\"user\":{\"id\":7,\"username\":\"alice\",\"status\":\"active\",\"expires_at\":\"2026-11-01T12:00:00Z\",\"days_remaining\":30,\"max_devices\":3},"
            + "\"key\":{\"days_added\":30,\"lifetime\":false}}";
        private const string RedeemOk =
            "{\"success\":true,\"message\":\"Key added: +30 days. Your account now runs until 2026-12-01T12:00:00Z.\","
            + "\"days_added\":30,\"lifetime\":false,\"expires_at\":\"2026-12-01T12:00:00Z\",\"days_remaining\":60,\"max_devices\":1,\"mode\":\"stack\"}";

        private static JsonElement Body(RecordedRequest request)
        {
            Assert.False(CryptoEnvelope.LooksLikeEnvelope(request.Body!));
            using JsonDocument doc = JsonDocument.Parse(request.Body!);
            return doc.RootElement.Clone();
        }

        [Fact]
        public async Task RegisterWithKey_PostsTheKey_AsPlainJson()
        {
            using var rig = new Rig(_ => FakeServer.Plain(RegisterOk));

            PwfResponse r = await rig.Client.RegisterAccountWithKeyAsync("alice", "s3cret-pass", "  " + FakeServer.LicenseKey + " ", "a@example.com");

            Assert.True(r.Success);
            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(FakeServer.BaseUrl + RegisterPath), request.Uri);
            Assert.Equal(FakeServer.AppSecret, request.Header("X-App-Secret"));
            JsonElement body = Body(request);
            Assert.Equal("alice", body.GetProperty("username").GetString());
            Assert.Equal("s3cret-pass", body.GetProperty("password").GetString());
            Assert.Equal("a@example.com", body.GetProperty("email").GetString());
            Assert.Equal(FakeServer.LicenseKey, body.GetProperty("license_key").GetString());   // trimmed
            Assert.Equal(4, body.EnumerateObject().Count());
        }

        [Fact]
        public async Task RedeemWithPassword_PostsUsernamePasswordAndKey()
        {
            using var rig = new Rig(_ => FakeServer.Plain(RedeemOk));

            PwfResponse r = await rig.Client.RedeemKeyAsync("alice", "s3cret-pass", FakeServer.LicenseKey);

            Assert.True(r.Success);
            Assert.Equal(60, r.GetInt32("days_remaining", 0));
            Assert.Equal("2026-12-01T12:00:00Z", r.GetString("expires_at"));
            RecordedRequest request = Assert.Single(rig.Handler.Requests);
            Assert.Equal(new Uri(FakeServer.BaseUrl + RedeemPath), request.Uri);
            JsonElement body = Body(request);
            Assert.Equal("alice", body.GetProperty("username").GetString());
            Assert.Equal("s3cret-pass", body.GetProperty("password").GetString());
            Assert.Equal(FakeServer.LicenseKey, body.GetProperty("license_key").GetString());
            Assert.Equal(3, body.EnumerateObject().Count());
        }

        [Fact]
        public async Task RedeemForTheSignedInAccount_SendsItsSession_NotAPassword()
        {
            using var rig = new Rig(req => req.Path == AccountLoginPath
                ? FakeServer.Plain(FakeServer.LoginOk(sessionId: "acct-sess-9"))
                : FakeServer.Plain(RedeemOk));

            await rig.Client.AccountLoginAsync("alice", "s3cret-pass");
            PwfResponse r = await rig.Client.RedeemKeyAsync(FakeServer.LicenseKey);

            Assert.True(r.Success);
            RecordedRequest redeem = rig.Handler.Requests.Last();
            Assert.Equal(new Uri(FakeServer.BaseUrl + RedeemPath), redeem.Uri);
            JsonElement body = Body(redeem);
            Assert.Equal("acct-sess-9", body.GetProperty("session_id").GetString());
            Assert.Equal(FakeServer.LicenseKey, body.GetProperty("license_key").GetString());
            Assert.Equal(2, body.EnumerateObject().Count());
        }

        [Fact]
        public async Task RedeemForTheSignedInAccount_WithoutASession_Throws_AndSendsNothing()
        {
            using var rig = new Rig(_ => FakeServer.Plain(RedeemOk));

            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Client.RedeemKeyAsync(FakeServer.LicenseKey));
            Assert.Empty(rig.Handler.Requests);
        }

        [Theory]
        [InlineData("KEY_ALREADY_USED", 409)]
        [InlineData("INVALID_CREDENTIALS", 401)]
        [InlineData("ALREADY_LIFETIME", 409)]
        public async Task Refusals_ComeBackAsFailedResponses_WithTheirCode(string code, int status)
        {
            using var rig = new Rig(_ => FakeServer.Plain("{\"success\":false,\"error_code\":\"" + code + "\",\"message\":\"No.\"}", (HttpStatusCode)status));

            PwfResponse r = await rig.Client.RedeemKeyAsync("alice", "s3cret-pass", FakeServer.LicenseKey);

            Assert.False(r.Success);
            Assert.Equal(code, r.ErrorCode);
            Assert.Equal("No.", r.Message);
        }

        [Fact]
        public async Task BlankArguments_Throw_BeforeAnyRequest()
        {
            using var rig = new Rig(_ => FakeServer.Plain(RedeemOk));

            await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.RegisterAccountWithKeyAsync("alice", "pw", " "));
            await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.RedeemKeyAsync("alice", "", FakeServer.LicenseKey));
            await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.RedeemKeyAsync("", "pw", FakeServer.LicenseKey));
            await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.RedeemKeyAsync(" "));
            Assert.Empty(rig.Handler.Requests);
        }

        [Theory]
        [InlineData(PwfErrorCodes.KeyRequired, "KEY_REQUIRED")]
        [InlineData(PwfErrorCodes.KeyAlreadyUsed, "KEY_ALREADY_USED")]
        [InlineData(PwfErrorCodes.KeyInUse, "KEY_IN_USE")]
        [InlineData(PwfErrorCodes.KeyRedeemed, "KEY_REDEEMED")]
        [InlineData(PwfErrorCodes.AlreadyLifetime, "ALREADY_LIFETIME")]
        [InlineData(PwfErrorCodes.UsernameExists, "USERNAME_EXISTS")]
        public void NewCodes_MatchTheServer_AndDoNotEndTheSession(string constant, string wireValue)
        {
            Assert.Equal(wireValue, constant);
            Assert.False(PwfErrorCodes.EndsSession(constant));
        }
    }
}
