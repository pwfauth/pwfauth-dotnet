using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>
    /// 1.3.1: a request that never got a reply (offline, DNS, firewall, proxy, TLS) used to
    /// escape as a raw HttpRequestException, although PwfHttpException is documented as
    /// "the server could not be reached". It is now a PwfHttpException with StatusCode 0
    /// and the network error as InnerException.
    /// </summary>
    public class TransportFailureTests
    {
        private static HttpResponseMessage Offline(RecordedRequest _)
        {
            throw new HttpRequestException("No such host is known. (license.test:443)");
        }

        /// <summary>Content whose body fails mid-read, like a connection reset during the reply.</summary>
        private sealed class DroppedContent : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                throw new IOException("The connection was reset by the remote host.");
            }

            protected override bool TryComputeLength(out long length)
            {
                length = -1;
                return false;
            }
        }

        [Fact]
        public async Task EncryptedCall_WithNoConnection_ThrowsPwfHttpException_Status0()
        {
            using var rig = new Rig(Offline);
            var ex = await Assert.ThrowsAsync<PwfHttpException>(() => rig.Client.LoginAsync(FakeServer.LicenseKey));
            Assert.Equal(0, ex.StatusCode);
            Assert.IsType<HttpRequestException>(ex.InnerException);
            Assert.StartsWith("Cannot reach the license server", ex.Message);
            Assert.False(rig.Client.IsSignedIn);
        }

        [Fact]
        public async Task PlainCall_WithNoConnection_ThrowsPwfHttpException_Status0()
        {
            using var rig = new Rig(Offline);
            var ex = await Assert.ThrowsAsync<PwfHttpException>(() => rig.Client.ResetHardwareIdAsync(FakeServer.LicenseKey));
            Assert.Equal(0, ex.StatusCode);
            Assert.IsType<HttpRequestException>(ex.InnerException);
        }

        [Fact]
        public async Task EncryptedGet_WithNoConnection_ThrowsPwfHttpException_Status0()
        {
            using var rig = new Rig(Offline);
            var ex = await Assert.ThrowsAsync<PwfHttpException>(() => rig.Client.GetAppInfoAsync());
            Assert.Equal(0, ex.StatusCode);
        }

        [Fact]
        public async Task NoConnection_IsCaughtWhereverPwfExceptionIs()
        {
            using var rig = new Rig(Offline);
            // The pattern the README and the samples use for every failure.
            await Assert.ThrowsAnyAsync<PwfException>(() => rig.Client.CheckKeyAsync(FakeServer.LicenseKey));
        }

        [Fact]
        public async Task Logout_WithNoConnection_ReturnsNull_AndEndsTheLocalSession()
        {
            bool online = true;
            using var rig = new Rig(r =>
            {
                if (!online) throw new HttpRequestException("The network is unreachable.");
                return FakeServer.Enveloped(FakeServer.LoginOk());
            });
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            Assert.True(rig.Client.IsSignedIn);

            online = false;
            PwfResponse? reply = await rig.Client.LogoutAsync();   // 1.3.0 threw HttpRequestException here

            Assert.Null(reply);
            Assert.False(rig.Client.IsSignedIn);
        }

        [Fact]
        public async Task ConnectionDroppedWhileReadingTheReply_ThrowsPwfHttpException()
        {
            // HttpClient buffers the body inside SendAsync, so a reset mid-reply surfaces
            // there as an HttpRequestException (no usable reply: StatusCode 0). The guard
            // around ReadAsStringAsync covers handlers that hand back an unbuffered body.
            using var rig = new Rig(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new DroppedContent() });
            var ex = await Assert.ThrowsAsync<PwfHttpException>(() => rig.Client.LoginAsync(FakeServer.LicenseKey));
            Assert.Equal(0, ex.StatusCode);
            Assert.NotNull(ex.InnerException);
        }

        [Fact]
        public async Task CallerCancellation_StaysAnOperationCanceledException()
        {
            using var rig = new Rig(_ => FakeServer.Enveloped(FakeServer.LoginOk()));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => rig.Client.LoginAsync(FakeServer.LicenseKey, cts.Token));
        }

        [Fact]
        public async Task Heartbeat_WithNoConnection_StillEndsWithNetworkLost()
        {
            bool online = true;
            using var rig = new Rig(r =>
            {
                if (!online) throw new HttpRequestException("The network is unreachable.");
                return FakeServer.Enveloped(FakeServer.LoginOk());
            }, o => { o.MaxHeartbeatFailures = 3; o.RaiseEventsOnCapturedContext = false; });

            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            TestHelpers.SetHeartbeatInterval(rig.Client, 0);
            var ended = new TaskCompletionSource<SessionEndedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Client.SessionEnded += (_, e) => ended.TrySetResult(e);

            online = false;
            rig.Client.StartHeartbeat();
            SessionEndedEventArgs args = await TestHelpers.WithTimeout(ended.Task);

            Assert.Equal(PwfErrorCodes.NetworkLost, args.ErrorCode);
            Assert.False(rig.Client.IsSignedIn);
        }
    }
}
