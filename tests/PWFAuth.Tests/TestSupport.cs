using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>What the fake network saw, captured while the request was still alive.</summary>
    internal sealed class RecordedRequest
    {
        public RecordedRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers,
            string? contentType, string? body)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
            ContentType = contentType;
            Body = body;
        }

        public HttpMethod Method { get; }
        public Uri Uri { get; }
        public string Path { get { return Uri.AbsolutePath; } }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public string? ContentType { get; }
        public string? Body { get; }

        public string? Header(string name)
        {
            string? value;
            return Headers.TryGetValue(name, out value) ? value : null;
        }
    }

    /// <summary>Stands in for the network: records every request and answers from a callback.</summary>
    internal sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _respond;
        private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();

        public FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get { lock (_requests) return _requests.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Complete asynchronously, like a real network call. Heartbeat tests run the loop
            // with a zero-second interval, and a handler that always completed synchronously
            // would let it spin inside the calling thread.
            await Task.Yield();

            // The client disposes the request (and its content) as soon as SendAsync
            // returns, so everything worth asserting on is copied out here.
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                // User-Agent is a space-separated list of products/comments; the rest are comma lists.
                string separator = string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ", ";
                headers[header.Key] = string.Join(separator, header.Value);
            }

            string? body = null;
            string? contentType = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                contentType = request.Content.Headers.ContentType?.ToString();
            }

            var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, contentType, body);
            lock (_requests) _requests.Add(recorded);
            return _respond(recorded);
        }
    }

    /// <summary>
    /// Builds license-server replies. Enveloped ones are sealed with the library's own
    /// <see cref="CryptoEnvelope"/>, which produces exactly the {p,t,s} document the
    /// server's PayloadCrypto emits (same key derivation, same HMAC input).
    /// </summary>
    internal static class FakeServer
    {
        public const string AppSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        public const string OtherSecret = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        public const string BaseUrl = "https://license.test";
        public const string LicenseKey = "ABCDE-FGHIJ-KLMNO-PQRST";
        public const string HardwareId = "TEST-MACHINE-0001";

        public const string LoginPath = "/api/auth/login.php";
        public const string HeartbeatPath = "/api/auth/heartbeat.php";
        public const string ResetPath = "/api/customer/reset-hwid.php";

        private static readonly CryptoEnvelope Crypto = new CryptoEnvelope(AppSecret);

        /// <summary>A reply encrypted the way the real server encrypts it.</summary>
        public static HttpResponseMessage Enveloped(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return Json(Crypto.Encrypt(json), status);
        }

        /// <summary>A reply sealed with a different app secret — what a forger without the secret produces.</summary>
        public static HttpResponseMessage EnvelopedWithWrongSecret(string json)
        {
            return Json(new CryptoEnvelope(OtherSecret).Encrypt(json), HttpStatusCode.OK);
        }

        /// <summary>A plain JSON reply, as sent for refusals before the app secret is accepted.</summary>
        public static HttpResponseMessage Plain(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return Json(json, status);
        }

        /// <summary>Opens a request body the client encrypted.</summary>
        public static string DecryptRequest(RecordedRequest request)
        {
            return Crypto.Decrypt(request.Body!);
        }

        public static string LoginOk(int heartbeatInterval = 30, string sessionId = "sess-123")
        {
            return "{\"success\":true,\"message\":\"Login successful\",\"session_id\":\"" + sessionId
                + "\",\"heartbeat_interval\":" + heartbeatInterval + "}";
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>A <see cref="PwfClient"/> wired to a <see cref="FakeHandler"/> through the public constructor.</summary>
    internal sealed class Rig : IDisposable
    {
        public Rig(Func<RecordedRequest, HttpResponseMessage> respond, Action<PwfClientOptions>? configure = null)
        {
            Handler = new FakeHandler(respond);
            Http = new HttpClient(Handler);
            var options = new PwfClientOptions
            {
                AppSecret = FakeServer.AppSecret,
                BaseUrl = FakeServer.BaseUrl,
                HardwareId = FakeServer.HardwareId,   // keeps the machine probe out of the tests
            };
            if (configure != null) configure(options);
            Client = new PwfClient(options, Http);
        }

        public FakeHandler Handler { get; }
        public HttpClient Http { get; }
        public PwfClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            Http.Dispose();   // the client never disposes an HttpClient it was given
        }
    }

    /// <summary>
    /// Stands in for a UI thread's context: counts Posts and runs each callback on a
    /// worker with itself installed as Current, so a handler can tell it was marshalled.
    /// </summary>
    internal sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount { get { return Volatile.Read(ref _postCount); } }

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SynchronizationContext? previous = Current;
                SetSynchronizationContext(this);
                try { d(state); }
                finally { SetSynchronizationContext(previous); }
            });
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            // A blocking Send from a background thread is how UI apps deadlock.
            throw new NotSupportedException("The client must post to the UI thread, never block on it.");
        }
    }

    internal static class TestHelpers
    {
        /// <summary>Runs <paramref name="action"/> with <paramref name="context"/> installed, as a UI thread would have it.</summary>
        public static T RunWithContext<T>(SynchronizationContext? context, Func<T> action)
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try { return action(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        public static void RunWithContext(SynchronizationContext? context, Action action)
        {
            RunWithContext<object?>(context, () => { action(); return null; });
        }

        public static async Task<T> WithTimeout<T>(Task<T> task, int seconds = 20)
        {
            await WithTimeout((Task)task, seconds);
            return await task;
        }

        public static async Task WithTimeout(Task task, int seconds = 20)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
            Assert.True(winner == task, "Timed out after " + seconds + " s.");
            await task;
        }

        /// <summary>
        /// The login reply cannot ask for less than 5 s between beats; tests shorten it the
        /// way samples/QuickTest does.
        /// </summary>
        public static void SetHeartbeatInterval(PwfClient client, int seconds)
        {
            typeof(PwfClient).GetProperty(nameof(PwfClient.HeartbeatIntervalSeconds))!.SetValue(client, seconds);
        }
    }

    /// <summary>
    /// Answers heartbeats from a script. When the script runs out it stops the loop through
    /// <see cref="Stop"/> — so a "the session survives" test ends cleanly — and keeps the
    /// session alive with an encrypted success.
    /// </summary>
    internal sealed class HeartbeatScript : IDisposable
    {
        private readonly Func<HttpResponseMessage>[] _beats;
        private int _requests;

        public HeartbeatScript(params Func<HttpResponseMessage>[] beats)
        {
            _beats = beats;
        }

        /// <summary>Pass its token to RunHeartbeatAsync; it is cancelled once every scripted beat was served.</summary>
        public CancellationTokenSource Stop { get; } = new CancellationTokenSource();

        public HttpResponseMessage Reply(RecordedRequest request)
        {
            if (request.Path == FakeServer.LoginPath) return FakeServer.Enveloped(FakeServer.LoginOk());

            int index = Interlocked.Increment(ref _requests) - 1;
            if (index < _beats.Length) return _beats[index]();

            // The loop handles one beat at a time, so every scripted reply has been processed.
            Stop.Cancel();
            return FakeServer.Enveloped("{\"success\":true}");
        }

        public void Dispose()
        {
            Stop.Dispose();
        }
    }
}
