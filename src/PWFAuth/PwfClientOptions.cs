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

        /// <summary>The API origin. Defaults to <c>https://pwfauth.com</c>.</summary>
        public string BaseUrl { get; set; } = "https://pwfauth.com";

        /// <summary>
        /// Machine identifier to bind the license to. Leave null to use
        /// <see cref="PWFAuth.HardwareId.Get"/>.
        /// </summary>
        public string? HardwareId { get; set; }

        /// <summary>Per-request timeout. Defaults to 15 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How many CONSECUTIVE unreachable heartbeats end the session locally with
        /// <see cref="PwfErrorCodes.NetworkLost"/>. Defaults to 3.
        /// </summary>
        /// <remarks>
        /// Do not set this to a very large number, and never to zero. The server drops
        /// the session on its own timeout regardless; if the client keeps running while
        /// the server is unreachable, blocking the license domain in a firewall becomes a
        /// way to use the application indefinitely.
        /// </remarks>
        public int MaxHeartbeatFailures { get; set; } = 3;

        /// <summary>
        /// Envelope timestamp tolerance in seconds. Must match the server's ±300.
        /// </summary>
        public int MaxClockDriftSeconds { get; set; } = 300;

        /// <summary>Throws when the options cannot produce a working client.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(AppSecret))
                throw new ArgumentException("AppSecret is required — copy it from Dashboard → App Settings.", nameof(AppSecret));
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new ArgumentException("BaseUrl is required.", nameof(BaseUrl));
            Uri? parsed;
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out parsed))
                throw new ArgumentException("BaseUrl must be an absolute URL, e.g. https://pwfauth.com", nameof(BaseUrl));
            if (MaxHeartbeatFailures < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxHeartbeatFailures),
                    "At least one failure must end the session, otherwise an unreachable server leaves the app running forever.");
        }
    }
}
