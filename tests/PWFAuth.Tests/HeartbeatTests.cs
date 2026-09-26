using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    // SessionEndedContextTests waits on real 5-second heartbeats; HeartbeatBudgetTests
    // shortens the interval to zero. xUnit runs test classes in parallel.

    public class SessionEndedContextTests
    {
        private sealed class Observation
        {
            public Observation(SessionEndedEventArgs args, SynchronizationContext? context)
            {
                Args = args;
                Context = context;
            }

            public SessionEndedEventArgs Args { get; }
            public SynchronizationContext? Context { get; }
        }

        private static HttpResponseMessage BannedOnFirstBeat(RecordedRequest request)
        {
            return request.Path == FakeServer.LoginPath
                ? FakeServer.Enveloped(FakeServer.LoginOk(heartbeatInterval: 5))
                : FakeServer.Enveloped("{\"success\":false,\"error_code\":\"BANNED\",\"message\":\"This license has been banned.\"}");
        }

        private static TaskCompletionSource<Observation> Watch(PwfClient client)
        {
            var seen = new TaskCompletionSource<Observation>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.SessionEnded += (sender, e) => seen.TrySetResult(new Observation(e, SynchronizationContext.Current));
            return seen;
        }

        [Fact]
        public async Task SessionEnded_IsPostedToTheCapturedContext_OrRaisedInlineWhenOptedOut()
        {
            // Three clients beat side by side at the 5-second minimum, so this is one ~5 s wait.
            using var started = new Rig(BannedOnFirstBeat);                                              // StartHeartbeat()
            using var runDirect = new Rig(BannedOnFirstBeat);                                            // RunHeartbeatAsync()
            using var optedOut = new Rig(BannedOnFirstBeat, o => o.RaiseEventsOnCapturedContext = false); // StartHeartbeat(), opted out

            await started.Client.LoginAsync(FakeServer.LicenseKey);
            await runDirect.Client.LoginAsync(FakeServer.LicenseKey);
            await optedOut.Client.LoginAsync(FakeServer.LicenseKey);
            Assert.Equal(5, started.Client.HeartbeatIntervalSeconds);

            TaskCompletionSource<Observation> startedSeen = Watch(started.Client);
            TaskCompletionSource<Observation> runDirectSeen = Watch(runDirect.Client);
            TaskCompletionSource<Observation> optedOutSeen = Watch(optedOut.Client);

            // What a WinForms/WPF UI thread looks like to the client.
            var uiContext = new RecordingSynchronizationContext();
            Task directLoop = TestHelpers.RunWithContext(uiContext, () =>
            {
                started.Client.StartHeartbeat();
                optedOut.Client.StartHeartbeat();
                return runDirect.Client.RunHeartbeatAsync(CancellationToken.None);
            });

            Observation viaStart = await TestHelpers.WithTimeout(startedSeen.Task);
            Observation viaRun = await TestHelpers.WithTimeout(runDirectSeen.Task);
            Observation inline = await TestHelpers.WithTimeout(optedOutSeen.Task);
            await TestHelpers.WithTimeout(directLoop);

            Assert.Equal(PwfErrorCodes.Banned, viaStart.Args.ErrorCode);
            Assert.Equal("This license has been banned.", viaStart.Args.Message);
            Assert.Same(uiContext, viaStart.Context);       // handler ran inside the captured context
            Assert.Same(uiContext, viaRun.Context);         // RunHeartbeatAsync captures it as well

            Assert.Equal(PwfErrorCodes.Banned, inline.Args.ErrorCode);
            Assert.NotSame(uiContext, inline.Context);      // raised on the heartbeat thread, not posted

            Assert.Equal(2, uiContext.PostCount);           // one Post per capturing client, nothing else
            Assert.True(directLoop.IsCompletedSuccessfully);
            Assert.False(started.Client.IsSignedIn);
            Assert.False(runDirect.Client.IsSignedIn);
            Assert.False(optedOut.Client.IsSignedIn);
        }
    }

    /// <summary>
    /// Which replies keep a session alive. Only an encrypted reply counts as an answer. The
    /// loop runs against a scripted server with a zero-second interval, so these are instant.
    /// Budgets are the defaults: 3 unanswered beats, 10 rate-limited ones.
    /// </summary>
    public class HeartbeatBudgetTests
    {
        private const string ClockSkewMessage =
            "Cannot verify your license because this computer's date and time are wrong. Correct them and sign in again.";
        private const string NetworkLostMessage =
            "Cannot reach the license server. Please check your connection and sign in again.";

        // What includes/PayloadCrypto.php answers when a request's timestamp is more than 300 s off.
        private static HttpResponseMessage ClockRefusal()
        {
            return CryptoError("Request expired (replay protection).");
        }

        private static HttpResponseMessage CryptoError(string message)
        {
            return FakeServer.Plain(
                "{\"success\":false,\"error_code\":\"CRYPTO_ERROR\",\"message\":\"" + message + "\"}",
                HttpStatusCode.BadRequest);
        }

        private static HttpResponseMessage PlainRefusal()
        {
            return FakeServer.Plain("{\"success\":false}");
        }

        private static HttpResponseMessage PlainRateLimited()
        {
            return FakeServer.Plain("{\"success\":false,\"detail\":\"Rate limit exceeded. Try again later.\",\"retry_after\":60}",
                HttpStatusCode.TooManyRequests);
        }

        // A 429 page from something in front of the server: not JSON, so it surfaces as a PwfHttpException.
        private static HttpResponseMessage RateLimitPage()
        {
            return Page(HttpStatusCode.TooManyRequests, "<html><body>Too Many Requests</body></html>");
        }

        private static HttpResponseMessage BadGatewayPage()
        {
            return Page(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>");
        }

        private static HttpResponseMessage ForgedSuccess()
        {
            return FakeServer.Plain("{\"success\":true,\"message\":\"Heartbeat OK\"}");
        }

        private static HttpResponseMessage EncryptedOk()
        {
            return FakeServer.Enveloped("{\"success\":true}");
        }

        private static HttpResponseMessage EncryptedUnknownFailure()
        {
            return FakeServer.Enveloped("{\"success\":false,\"error_code\":\"SOMETHING_NEW\",\"message\":\"Try again later.\"}");
        }

        private static HttpResponseMessage Page(HttpStatusCode status, string html)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
        }

        private static Func<HttpResponseMessage>[] Times(int count, Func<HttpResponseMessage> reply)
        {
            return Enumerable.Repeat(reply, count).ToArray();
        }

        private sealed class Outcome
        {
            public Outcome(IReadOnlyList<SessionEndedEventArgs> events, int beats, bool scriptFinished, bool stillSignedIn)
            {
                Events = events;
                Beats = beats;
                ScriptFinished = scriptFinished;
                StillSignedIn = stillSignedIn;
            }

            public IReadOnlyList<SessionEndedEventArgs> Events { get; }
            /// <summary>Scripted beats the loop went through before it stopped.</summary>
            public int Beats { get; }
            /// <summary>True when every scripted beat was handled and the session was still alive.</summary>
            public bool ScriptFinished { get; }
            public bool StillSignedIn { get; }
        }

        private static async Task<Outcome> RunAsync(params Func<HttpResponseMessage>[] beats)
        {
            using var script = new HeartbeatScript(beats);
            using var rig = new Rig(script.Reply);
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            TestHelpers.SetHeartbeatInterval(rig.Client, 0);

            var events = new List<SessionEndedEventArgs>();
            rig.Client.SessionEnded += (sender, e) => { lock (events) events.Add(e); };

            // No UI context, so SessionEnded is raised inline, before the loop task completes.
            Task loop = TestHelpers.RunWithContext(null, () => rig.Client.RunHeartbeatAsync(script.Stop.Token));
            await TestHelpers.WithTimeout(loop);

            int requests = rig.Handler.Requests.Count(r => r.Path == FakeServer.HeartbeatPath);
            lock (events)
            {
                return new Outcome(events.ToArray(), Math.Min(requests, beats.Length),
                    script.Stop.IsCancellationRequested, rig.Client.IsSignedIn);
            }
        }

        private static void AssertEnded(Outcome outcome, string code, string message, int afterBeats)
        {
            SessionEndedEventArgs ended = Assert.Single(outcome.Events);
            Assert.Equal(code, ended.ErrorCode);
            Assert.Equal(message, ended.Message);
            Assert.Equal(afterBeats, outcome.Beats);
            Assert.False(outcome.ScriptFinished);
            Assert.False(outcome.StillSignedIn);
        }

        private static void AssertSurvived(Outcome outcome)
        {
            Assert.Empty(outcome.Events);
            Assert.True(outcome.ScriptFinished);
            Assert.True(outcome.StillSignedIn);
        }

        [Fact]
        public async Task ThreePlainClockRefusals_EndWithClockSkew()
        {
            Outcome outcome = await RunAsync(ClockRefusal, ClockRefusal, ClockRefusal);

            AssertEnded(outcome, PwfErrorCodes.ClockSkew, ClockSkewMessage, afterBeats: 3);
            Assert.False(outcome.Events[0].IsNetworkFailure);
        }

        [Fact]
        public async Task ThreePlainRefusals_EndWithNetworkLost()
        {
            Outcome outcome = await RunAsync(PlainRefusal, PlainRefusal, PlainRefusal);

            AssertEnded(outcome, PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 3);
            Assert.True(outcome.Events[0].IsNetworkFailure);
        }

        [Fact]
        public async Task NinePlain429sInARow_DoNotEndTheSession()
        {
            AssertSurvived(await RunAsync(Times(9, PlainRateLimited)));
        }

        [Fact]
        public async Task TheTenthPlain429InARow_EndsWithNetworkLost()
        {
            AssertEnded(await RunAsync(Times(10, PlainRateLimited)), PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 10);
        }

        [Fact]
        public async Task RateLimitPagesThatAreNotJson_ShareThe429Budget()
        {
            AssertSurvived(await RunAsync(Times(9, RateLimitPage)));
            AssertEnded(await RunAsync(Times(10, RateLimitPage)), PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 10);
        }

        [Fact]
        public async Task PlainRefusals_BrokenUpByAnEncryptedSuccess_NeverAddUp()
        {
            AssertSurvived(await RunAsync(
                PlainRefusal, PlainRefusal, EncryptedOk,
                ClockRefusal, BadGatewayPage, EncryptedOk,
                ForgedSuccess, PlainRefusal));
        }

        [Fact]
        public async Task Plain429s_BrokenUpByAnEncryptedSuccess_NeverAddUp()
        {
            AssertSurvived(await RunAsync(Times(9, PlainRateLimited).Append(EncryptedOk).Concat(Times(9, PlainRateLimited)).ToArray()));
        }

        [Fact]
        public async Task AnEncryptedUnknownCode_StaysTransient_AndStartsTheCountOver()
        {
            AssertSurvived(await RunAsync(
                PlainRefusal, PlainRefusal, EncryptedUnknownFailure,
                PlainRefusal, PlainRefusal, EncryptedUnknownFailure,
                EncryptedUnknownFailure, EncryptedUnknownFailure, EncryptedUnknownFailure));
        }

        [Fact]
        public async Task AlternatingFailureKinds_DoNotResetEachOther()
        {
            // A 429 between refusals must not break their streak, or a proxy could alternate forever.
            Outcome outcome = await RunAsync(
                PlainRateLimited, PlainRefusal, PlainRateLimited, PlainRefusal, PlainRateLimited, PlainRefusal);

            AssertEnded(outcome, PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 6);
        }

        [Fact]
        public async Task ForgedPlainSuccesses_EndWithNetworkLost()
        {
            AssertEnded(await RunAsync(ForgedSuccess, ForgedSuccess, ForgedSuccess),
                PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 3);
        }

        [Fact]
        public async Task TransportFailuresAlone_EndWithNetworkLost()
        {
            AssertEnded(await RunAsync(BadGatewayPage, BadGatewayPage, BadGatewayPage),
                PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 3);
        }

        [Fact]
        public async Task ClockRefusals_AroundATransportFailure_AreStillClockSkew()
        {
            // Only plain refusals decide the verdict; a dropped connection says nothing about the clock.
            AssertEnded(await RunAsync(ClockRefusal, BadGatewayPage, ClockRefusal),
                PwfErrorCodes.ClockSkew, ClockSkewMessage, afterBeats: 3);
        }

        [Fact]
        public async Task ClockRefusals_MixedWithOtherRefusals_AreNetworkLost()
        {
            AssertEnded(await RunAsync(ClockRefusal, PlainRefusal, ClockRefusal),
                PwfErrorCodes.NetworkLost, NetworkLostMessage, afterBeats: 3);
        }

        [Theory]
        [InlineData("Request expired (replay protection).", PwfErrorCodes.ClockSkew)]
        [InlineData("REQUEST EXPIRED", PwfErrorCodes.ClockSkew)]
        [InlineData("HMAC verification failed.", PwfErrorCodes.NetworkLost)]
        public async Task OnlyAnExpiredCryptoError_BlamesTheClock(string message, string expectedCode)
        {
            Outcome outcome = await RunAsync(() => CryptoError(message), () => CryptoError(message), () => CryptoError(message));

            Assert.Equal(expectedCode, Assert.Single(outcome.Events).ErrorCode);
        }
    }
}
