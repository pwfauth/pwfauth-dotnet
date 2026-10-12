using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>
    /// 1.5.2: a request that got no reply at all goes out once more on a new connection (an idle
    /// connection closed on the way shows up as Windows error 10054). A reply, a reply that drops
    /// while it is read, and a timeout are never sent twice.
    /// </summary>
    public class RetryTests
    {
        private const string Reset = "An existing connection was forcibly closed by the remote host.";

        // A reply without the server's signature: reaching the signature check proves the second
        // try got an answer through (the fake network cannot sign like pwfauth.com).
        private static HttpResponseMessage Unsigned()
        {
            return FakeServer.Plain("{\"success\":false,\"error_code\":\"INVALID_KEY\",\"message\":\"Invalid key\"}");
        }

        private static (HttpClient Http, FakeHandler Handler, PwfClient Client) Make(Func<RecordedRequest, HttpResponseMessage> respond)
        {
            var handler = new FakeHandler(respond);
            var http = new HttpClient(handler);
            var client = new PwfClient(new PwfClientOptions { AppSecret = FakeServer.AppSecret, HardwareId = FakeServer.HardwareId }, http);
            return (http, handler, client);
        }

        [Fact]
        public async Task DroppedConnection_IsSentOnceMore_WithTheSameRequest()
        {
            int calls = 0;
            var (http, handler, client) = Make(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException(Reset);
                return Unsigned();
            });
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<PwfSecurityException>(() => client.LoginAsync(FakeServer.LicenseKey));
                Assert.Equal(2, handler.Requests.Count);
                RecordedRequest first = handler.Requests[0], second = handler.Requests[1];
                Assert.Equal(first.Method, second.Method);
                Assert.Equal(first.Uri, second.Uri);
                Assert.Equal(first.Body, second.Body);
                Assert.Equal(first.ContentType, second.ContentType);
                Assert.Equal(first.Header("X-PWF-Nonce"), second.Header("X-PWF-Nonce"));
                Assert.Equal(first.Header("X-App-Secret"), second.Header("X-App-Secret"));
                Assert.Equal(first.Header("User-Agent"), second.Header("User-Agent"));
            }
        }

        [Fact]
        public async Task DroppedGet_IsSentOnceMore_WithItsBearer()
        {
            int calls = 0;
            var (http, handler, client) = Make(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException(Reset);
                return Unsigned();
            });
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<PwfSecurityException>(() => client.GetEnvelopeAsync("/api/app/info.php", FakeServer.LicenseKey));
                Assert.Equal(2, handler.Requests.Count);
                Assert.Null(handler.Requests[1].Body);
                Assert.Equal("Bearer " + FakeServer.LicenseKey, handler.Requests[1].Header("Authorization"));
            }
        }

        [Fact]
        public async Task DroppedTwice_ThrowsPwfHttpException_AfterTwoTries()
        {
            var (http, handler, client) = Make(_ => throw new HttpRequestException(Reset));
            using (http)
            using (client)
            {
                var ex = await Assert.ThrowsAsync<PwfHttpException>(() => client.ResetHardwareIdAsync(FakeServer.LicenseKey));
                Assert.Equal(0, ex.StatusCode);
                Assert.IsType<HttpRequestException>(ex.InnerException);
                Assert.Equal(2, handler.Requests.Count);
            }
        }

        [Fact]
        public async Task Timeout_IsNotSentTwice()
        {
            var (http, handler, client) = Make(_ => throw new TaskCanceledException("timed out"));
            using (http)
            using (client)
            {
                var ex = await Assert.ThrowsAsync<PwfHttpException>(() => client.LoginAsync(FakeServer.LicenseKey));
                Assert.StartsWith("The license server did not respond in time", ex.Message);
                Assert.Single(handler.Requests);
            }
        }

        [Fact]
        public async Task AReply_IsNotSentTwice()
        {
            var (http, handler, client) = Make(_ => FakeServer.Plain("busy", HttpStatusCode.ServiceUnavailable));
            using (http)
            using (client)
            {
                await Assert.ThrowsAsync<PwfSecurityException>(() => client.LoginAsync(FakeServer.LicenseKey));
                Assert.Single(handler.Requests);
            }
        }

        [Fact]
        public async Task Cancelled_DuringTheWait_IsNotSentAgain()
        {
            using (var cts = new CancellationTokenSource())
            {
                var (http, handler, client) = Make(_ =>
                {
                    cts.Cancel();
                    throw new HttpRequestException(Reset);
                });
                using (http)
                using (client)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => client.PostPlainAsync("/api/auth/trial.php", new System.Collections.Generic.Dictionary<string, object?>(), cts.Token));
                    Assert.Single(handler.Requests);
                }
            }
        }
    }
}
