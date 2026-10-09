using System;

namespace PWFAuth
{
    /// <summary>
    /// Configuration for <see cref="PwfClient"/>. Everything except the app secret has a
    /// working default.
    /// </summary>
    public sealed class PwfClientOptions
    {
        /// <summary>
        /// Your application's 64-character hex secret, from Dashboard → App Settings.
        /// Required.
        /// </summary>
        /// <remarks>
        /// This value ships inside your binary — that is inherent to the envelope
        /// protocol, which needs the key on the client to encrypt. Treat the compiled
        /// output as sensitive: obfuscate release builds, and never commit the secret to
        /// a public repository. Reading it from an environment variable or an encrypted
        /// config at startup keeps it out of source control.
        /// </remarks>
        public string AppSecret { get; set; } = string.Empty;

        /// <summary>The API origin. Must be <c>https://pwfauth.com</c>; other origins are rejected.</summary>
        public string BaseUrl { get; set; } = "https://pwfauth.com";

        /// <summary>
        /// Machine identifier to bind the license to. Leave null to use
        /// <see cref="PWFAuth.HardwareId.Get"/>.
        /// </summary>
        public string? HardwareId { get; set; }

        /// <summary>
        /// Per-request timeout of the <see cref="System.Net.Http.HttpClient"/> the client
        /// creates for itself. Defaults to 15 seconds.
        /// </summary>
        /// <remarks>
        /// Ignored when you pass your own HttpClient to the <see cref="PwfClient"/>
        /// constructor: that one is used as it is, so set its <c>Timeout</c> yourself.
        /// </remarks>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How many heartbeats in a row may go unanswered before the session ends locally
        /// with <see cref="PwfErrorCodes.NetworkLost"/> — or <see cref="PwfErrorCodes.ClockSkew"/>
        /// when the server refused them because this computer's clock is wrong. Defaults to 3.
        /// </summary>
        /// <remarks>
        /// After independent signature verification, only an encrypted session reply
        /// counts as an answer. No reply, a reply that fails verification and a plain refusal all
        /// count as unanswered (plain HTTP 429 has its own budget,
        /// <see cref="MaxRateLimitedBeats"/>). Do not set this to a very large number. The
        /// server drops the session on its own timeout regardless; if the client keeps running
        /// while the server is unreachable, blocking the license domain in a firewall — or
        /// moving the clock — becomes a way to use the application indefinitely.
        /// </remarks>
        public int MaxHeartbeatFailures { get; set; } = 3;

        /// <summary>
        /// How many heartbeats in a row may be answered with a plain HTTP 429 (Too Many
        /// Requests) before the session ends locally with <see cref="PwfErrorCodes.NetworkLost"/>.
        /// Defaults to 10.
        /// </summary>
        /// <remarks>
        /// Rate limiting gets a larger budget than <see cref="MaxHeartbeatFailures"/>: users
        /// behind a shared IP (an office, a mobile carrier, a VPN) can be throttled
        /// legitimately, and that should not sign them out within a minute or two. It still
        /// has to run out — a proxy answering every beat with 429 would otherwise keep the
        /// application running forever. Only an encrypted reply from the license server
        /// starts the count over.
        /// </remarks>
        public int MaxRateLimitedBeats { get; set; } = 10;

        /// <summary>
        /// Envelope timestamp tolerance in seconds. Must match the server's ±300.
        /// </summary>
        public int MaxClockDriftSeconds { get; set; } = 300;

        /// <summary>
        /// Repair a wrong system clock on its own. When the server refuses a request because this
        /// machine's clock is more than five minutes off, it sends its own time with the refusal;
        /// the client then shifts its timestamps by the difference and sends the request once
        /// more. Defaults to true.
        /// </summary>
        /// <remarks>
        /// Clock correction uses only replies authenticated by the pinned server signing key.
        /// </remarks>
        public bool AutoCorrectClock { get; set; } = true;

        /// <summary>
        /// Raise <see cref="PwfClient.SessionEnded"/> through the
        /// <see cref="System.Threading.SynchronizationContext"/> that was current when the
        /// heartbeat was started — the UI thread in a WinForms or WPF app — so the handler can
        /// update controls without <c>Invoke</c> or a <c>Dispatcher</c>. Defaults to true.
        /// </summary>
        /// <remarks>
        /// Where no context exists (console apps, services, a thread-pool thread) the event
        /// is raised on the heartbeat's own background thread either way. Set this to false
        /// to always get that behaviour, as in version 1.0.
        /// </remarks>
        public bool RaiseEventsOnCapturedContext { get; set; } = true;

        /// <summary>Throws when the options cannot produce a working client.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(AppSecret))
                throw new ArgumentException("AppSecret is required — copy it from Dashboard → App Settings.", nameof(AppSecret));
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new ArgumentException("BaseUrl is required.", nameof(BaseUrl));
            ServerAuth.ValidateOrigin(BaseUrl);
            Uri? parsed;
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out parsed))
                throw new ArgumentException("BaseUrl must be an absolute URL, e.g. https://pwfauth.com", nameof(BaseUrl));
            if (MaxHeartbeatFailures < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxHeartbeatFailures),
                    "At least one failure must end the session, otherwise an unreachable server leaves the app running forever.");
            if (MaxRateLimitedBeats < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxRateLimitedBeats),
                    "At least one rate-limited beat must end the session, otherwise a proxy answering 429 forever leaves the app running.");
        }
    }
}
