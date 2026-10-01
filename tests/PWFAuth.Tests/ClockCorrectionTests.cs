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
    /// AutoCorrectClock (1.2.0): the server's plain CLOCK_SKEW refusal carries its own time,
    /// and the client shifts its stamps by the difference and retries once.
    /// </summary>
    public class ClockCorrectionTests
    {
        private const long ServerAhead = 30240;   // this PC is 8 h 24 min slow

        // A server whose clock is ServerAhead seconds in front of this machine's.
        private static readonly CryptoEnvelope ServerCrypto = new CryptoEnvelope(FakeServer.AppSecret) { ClockOffsetSeconds = ServerAhead };

        private static long ServerNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ServerAhead;
        }

        private static long Stamp(RecordedRequest request)
        {
            using (JsonDocument doc = JsonDocument.Parse(request.Body!))
                return doc.RootElement.GetProperty("t").GetInt64();
        }

        private static HttpResponseMessage Skew(string serverTimeJson)
        {
            return FakeServer.Plain("{\"success\":false,\"error_code\":\"CRYPTO_ERROR\",\"reason\":\"CLOCK_SKEW\",\"server_time\":"
                + serverTimeJson + ",\"message\":\"This computer's clock is wrong, so the request expired.\"}", HttpStatusCode.BadRequest);
        }

        private static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ServerCrypto.Encrypt(json), System.Text.Encoding.UTF8, "application/json") };
        }

        // What the real server does: refuse a stamp more than 300 s from its own clock, else answer sealed with its clock.
        private static HttpResponseMessage RealisticServer(RecordedRequest request)
        {
            if (Math.Abs(Stamp(request) - ServerNow()) > 300)
                return Skew(ServerNow().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return request.Path == FakeServer.LoginPath ? Json(FakeServer.LoginOk()) : Json("{\"success\":true}");
        }

        [Fact]
        public async Task WrongClock_IsCorrectedFromServerTime_AndTheRequestGoesOnceMore()
        {
            using var rig = new Rig(RealisticServer);
            PwfResponse login = await rig.Client.LoginAsync(FakeServer.LicenseKey);

            Assert.True(login.Success);
            Assert.Equal(2, rig.Handler.Requests.Count);
            Assert.InRange(Stamp(rig.Handler.Requests[1]) - ServerNow(), -5, 5);   // the retry carries the server's time

            // The shift sticks: the heartbeat is stamped right the first time.
            PwfResponse beat = await rig.Client.HeartbeatAsync();
            Assert.True(beat.Success);
            Assert.Equal(3, rig.Handler.Requests.Count);
        }

        [Fact]
        public async Task AutoCorrectClockOff_KeepsTheRefusal()
        {
            using var rig = new Rig(RealisticServer, o => o.AutoCorrectClock = false);
            PwfResponse login = await rig.Client.LoginAsync(FakeServer.LicenseKey);

            Assert.False(login.Success);
            Assert.Equal("CRYPTO_ERROR", login.ErrorCode);
            Assert.Single(rig.Handler.Requests);
        }

        [Fact]
        public async Task OnlyOneRetry_WhenTheServerKeepsRefusing()
        {
            using var rig = new Rig(r => Skew(ServerNow().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            PwfResponse login = await rig.Client.LoginAsync(FakeServer.LicenseKey);

            Assert.False(login.Success);
            Assert.Equal(2, rig.Handler.Requests.Count);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("\"1790000000\"")]
        [InlineData("null")]
        [InlineData("1.5")]
        public async Task UnusableServerTime_IsIgnored(string serverTimeJson)
        {
            using var rig = new Rig(r => Skew(serverTimeJson));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            Assert.Single(rig.Handler.Requests);
        }

        [Fact]
        public async Task PlainRefusalWithoutReason_IsNotARetry()
        {
            // What the server sent before 2026-10-01, and still sends for a bad signature.
            using var rig = new Rig(r => FakeServer.Plain("{\"success\":false,\"error_code\":\"CRYPTO_ERROR\",\"message\":\"Request expired (replay protection).\"}", HttpStatusCode.BadRequest));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            Assert.Single(rig.Handler.Requests);
        }

        [Fact]
        public async Task EncryptedReplyNamingClockSkew_IsNotARetry()
        {
            using var rig = new Rig(r => FakeServer.Enveloped("{\"success\":false,\"reason\":\"CLOCK_SKEW\",\"server_time\":" + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 999999) + "}"));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            Assert.Single(rig.Handler.Requests);
        }

        [Fact]
        public void ClockOffset_ShiftsStampsAndTheReplyCheck()
        {
            var behind = new CryptoEnvelope(FakeServer.AppSecret) { ClockOffsetSeconds = -ServerAhead };
            string sealedByServer = ServerCrypto.Encrypt("{\"ok\":1}");
            Assert.Throws<PwfCryptoException>(() => behind.Decrypt(sealedByServer));   // 16 h 48 min apart

            behind.ClockOffsetSeconds = ServerAhead;
            Assert.Equal("{\"ok\":1}", behind.Decrypt(sealedByServer));
        }
    }
}
