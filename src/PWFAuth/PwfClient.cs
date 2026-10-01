using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PWFAuth
{
    /// <summary>
    /// Client for the PWF Auth API: activate a license, keep a session alive under the
    /// owner's kill switch, pull remote content, and check for updates.
    /// </summary>
    /// <remarks>
    /// Create one instance per application and keep it alive — it owns an
    /// <see cref="HttpClient"/> and the session state. Every method is thread-safe to
    /// call, but a session is single-holder by design.
    /// </remarks>
    /// <example>
    /// <code>
    /// var client = new PwfClient("your-64-char-app-secret");
    /// client.SessionEnded += (s, e) => { MessageBox.Show(e.Message); Application.Exit(); };
    ///
    /// var login = await client.LoginAsync("XXXXX-XXXXX-XXXXX-XXXXX");
    /// if (!login.Success) { MessageBox.Show(login.Message); return; }
    /// client.StartHeartbeat();   // on the UI thread, so SessionEnded is raised there too
    /// </code>
    /// </example>
    public sealed class PwfClient : IDisposable
    {
        // Sent on every request, e.g. "PWFAuth-dotnet/1.2.0 (+https://pwfauth.com)".
        private static readonly string UserAgent = BuildUserAgent();

        private const string DefaultResetReason = "Reset from app";
        private const int MaxResetReasonLength = 255;

        private const int TooManyRequests = 429;
        private const string CryptoErrorCode = "CRYPTO_ERROR";   // the server could not verify the request
        private const string NetworkLostMessage =
            "Cannot reach the license server. Please check your connection and sign in again.";
        private const string ClockSkewMessage =
            "Cannot verify your license because this computer's date and time are wrong. Correct them and sign in again.";

        private readonly PwfClientOptions _options;
        private readonly CryptoEnvelope _crypto;
        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;
        private readonly string _baseUrl;
        private readonly object _sync = new object();

        private CancellationTokenSource? _heartbeatCts;
        private Task? _heartbeatTask;
        private bool _disposed;

        /// <summary>Creates a client with the default base URL (https://pwfauth.com).</summary>
        /// <param name="appSecret">Your application's 64-character hex secret.</param>
        public PwfClient(string appSecret)
            : this(new PwfClientOptions { AppSecret = appSecret }) { }

        /// <summary>Creates a client with full configuration.</summary>
        /// <param name="options">Secret, base URL, timeouts, heartbeat policy.</param>
        /// <param name="httpClient">
        /// Supply your own (from IHttpClientFactory, or one with a proxy configured) and this
        /// client uses it as it is: it never disposes or reconfigures it. Set its
        /// <see cref="HttpClient.Timeout"/> yourself — <see cref="PwfClientOptions.Timeout"/>
        /// only applies to the HttpClient this client creates. Leave null to get a private one.
        /// </param>
        public PwfClient(PwfClientOptions options, HttpClient? httpClient = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();

            _options = options;
            _crypto = new CryptoEnvelope(options.AppSecret, options.MaxClockDriftSeconds);
            _baseUrl = options.BaseUrl.TrimEnd('/');
            _ownsHttpClient = httpClient == null;
            // A caller's HttpClient is left alone: setting Timeout throws once it has sent a
            // request, and the caller may depend on the value it already has.
            _http = httpClient ?? new HttpClient { Timeout = options.Timeout };
            HardwareId = string.IsNullOrWhiteSpace(options.HardwareId)
                ? PWFAuth.HardwareId.Get()
                : options.HardwareId!;
        }

        /// <summary>
        /// Fires when the session stops being valid. Sign the user out here — the server
        /// has already dropped the session, so continuing to run is not licensed use.
        /// </summary>
        /// <remarks>
        /// The handler runs on the <see cref="SynchronizationContext"/> that was current when
        /// <see cref="StartHeartbeat"/> (or <see cref="RunHeartbeatAsync"/>) was called. Start
        /// the heartbeat on a WinForms or WPF UI thread — for example right after
        /// <c>await client.LoginAsync(...)</c> in a button handler — and the handler may touch
        /// controls directly, with no <c>Invoke</c> or <c>Dispatcher</c> call. Without a context
        /// (console apps, services, a thread-pool thread) it runs on the heartbeat's own
        /// background thread. <see cref="PwfClientOptions.RaiseEventsOnCapturedContext"/>
        /// set to false always gives the background-thread behaviour.
        /// </remarks>
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;

        /// <summary>The license key of the current session, or null when signed out.</summary>
        public string? LicenseKey { get; private set; }

        /// <summary>The current session id, or null when signed out.</summary>
        public string? SessionId { get; private set; }

        /// <summary>The machine identifier this client binds licenses to.</summary>
        public string HardwareId { get; }

        /// <summary>Seconds between heartbeats, as dictated by the login response.</summary>
        public int HeartbeatIntervalSeconds { get; private set; } = 30;

        /// <summary>True while a session is open.</summary>
        public bool IsSignedIn { get { return !string.IsNullOrEmpty(SessionId); } }

        // ───────────────────────── session ─────────────────────────

        /// <summary>
        /// Activates the key on first use and opens a session. Check
        /// <see cref="PwfResponse.Success"/>; on failure <see cref="PwfResponse.Message"/>
        /// is safe to show the user.
        /// </summary>
        /// <param name="licenseKey">The customer's key, e.g. XXXXX-XXXXX-XXXXX-XXXXX.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// When the key is bound to another machine the reply carries
        /// <see cref="PwfErrorCodes.HwidMismatch"/> (or <see cref="PwfErrorCodes.DeviceLimit"/>
        /// for multi-device keys). <see cref="ResetHardwareIdAsync"/> lets the customer move it
        /// to this PC; then call this method again.
        /// </remarks>
        /// <exception cref="PwfSecurityException">
        /// The reply was not encrypted yet claimed success — something other than the license
        /// server answered. The user is not signed in.
        /// </exception>
        /// <exception cref="PwfHttpException">The server could not be reached or answered with no API payload.</exception>
        /// <exception cref="PwfCryptoException">The encrypted reply failed verification (wrong app secret, or this machine's clock is off).</exception>
        public async Task<PwfResponse> LoginAsync(string licenseKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) throw new ArgumentException("License key is required.", nameof(licenseKey));

            PwfResponse response = await PostEnvelopeAsync("/api/auth/login.php", new Dictionary<string, object?>
            {
                ["license_key"] = licenseKey,
                ["hwid"] = HardwareId,
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success)
            {
                LicenseKey = licenseKey;
                SessionId = response.GetString("session_id");
                HeartbeatIntervalSeconds = Math.Max(5, response.GetInt32("heartbeat_interval", 30));
            }
            return response;
        }

        /// <summary>
        /// Reads a key's status and entitlements WITHOUT opening a session, so no device
        /// seat is consumed. Useful on a splash screen before the user signs in.
        /// </summary>
        /// <param name="licenseKey">The key to inspect.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> CheckKeyAsync(string licenseKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) throw new ArgumentException("License key is required.", nameof(licenseKey));
            return PostEnvelopeAsync("/api/auth/check-key.php", new Dictionary<string, object?>
            {
                ["license_key"] = licenseKey,
            }, cancellationToken);
        }

        /// <summary>
        /// Sends one heartbeat. Prefer <see cref="StartHeartbeat"/>, which also reacts to
        /// the kill switch — a single beat proves nothing about the next 30 seconds.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> HeartbeatAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!IsSignedIn) throw new InvalidOperationException("No open session — call LoginAsync first.");
            return PostEnvelopeAsync("/api/auth/heartbeat.php", new Dictionary<string, object?>
            {
                ["session_id"] = SessionId,
                ["license_key"] = LicenseKey,
            }, cancellationToken);
        }

        /// <summary>
        /// Starts the background heartbeat loop. It keeps the session alive AND obeys the
        /// owner's kill switch: a ban, pause, expiry, HWID reset, revoke or maintenance
        /// window raises <see cref="SessionEnded"/> on the next beat. So does losing the
        /// server: only an encrypted reply counts as an answer, and
        /// <see cref="PwfClientOptions.MaxHeartbeatFailures"/> beats in a row without one — no
        /// reply, a forged "success" (see <see cref="PwfSecurityException"/>) or a plain
        /// refusal — end the session with <see cref="PwfErrorCodes.NetworkLost"/>, or with
        /// <see cref="PwfErrorCodes.ClockSkew"/> when the server refused them because this
        /// computer's clock is wrong. Plain HTTP 429 replies have their own budget,
        /// <see cref="PwfClientOptions.MaxRateLimitedBeats"/>. Calling it twice is a no-op.
        /// </summary>
        /// <remarks>
        /// Call it on the UI thread in WinForms or WPF. The
        /// <see cref="SynchronizationContext"/> current at this call is captured and
        /// <see cref="SessionEnded"/> is raised through it, so the handler may touch controls
        /// directly. The beats themselves never run on the UI thread. Opt out with
        /// <see cref="PwfClientOptions.RaiseEventsOnCapturedContext"/>.
        /// </remarks>
        public void StartHeartbeat()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PwfClient));
                if (!IsSignedIn) throw new InvalidOperationException("No open session — call LoginAsync first.");
                if (_heartbeatTask != null && !_heartbeatTask.IsCompleted) return;

                _heartbeatCts = new CancellationTokenSource();
                _heartbeatTask = HeartbeatLoopAsync(CaptureEventContext(), _heartbeatCts.Token);
            }
        }

        /// <summary>Stops the background heartbeat loop without ending the server session.</summary>
        public void StopHeartbeat()
        {
            CancellationTokenSource? cts;
            lock (_sync) { cts = _heartbeatCts; _heartbeatCts = null; }
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }
        }

        /// <summary>
        /// The heartbeat loop itself, for callers that want to own the task (a console
        /// app can simply await it). <see cref="StartHeartbeat"/> wraps this.
        /// </summary>
        /// <param name="cancellationToken">Stops the loop.</param>
        /// <remarks>
        /// Like <see cref="StartHeartbeat"/>, it captures the <see cref="SynchronizationContext"/>
        /// current at the call and raises <see cref="SessionEnded"/> through it.
        /// </remarks>
        public Task RunHeartbeatAsync(CancellationToken cancellationToken)
        {
            return HeartbeatLoopAsync(CaptureEventContext(), cancellationToken);
        }

        private SynchronizationContext? CaptureEventContext()
        {
            return _options.RaiseEventsOnCapturedContext ? SynchronizationContext.Current : null;
        }

        private async Task HeartbeatLoopAsync(SynchronizationContext? eventContext, CancellationToken cancellationToken)
        {
            // Only an encrypted reply proves the license server answered — nothing else can
            // seal one. Every other outcome is an unanswered beat: no reply, a reply that fails
            // verification, a forged plain "success" (PwfSecurityException), and plain
            // refusals, which the server sends when it cannot verify the request at all. The
            // commonest of those is its replay check rejecting a clock more than five minutes
            // off; treating it as transient let an app whose clock was moved run forever,
            // deaf to bans.
            int unanswered = 0;      // beats in a row without an encrypted reply (not 429)
            int plainRefusals = 0;   //   ...of which were plain refusals from the server
            int clockRefusals = 0;   //   ...of which blamed this computer's clock
            int rateLimited = 0;     // beats in a row answered with HTTP 429

            while (!cancellationToken.IsCancellationRequested && IsSignedIn)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                if (!IsSignedIn) return;

                PwfResponse beat;
                try
                {
                    beat = await HeartbeatAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (PwfHttpException ex) when (ex.StatusCode == TooManyRequests)
                {
                    // A 429 page from something in front of the server (a CDN, say).
                    if (++rateLimited >= _options.MaxRateLimitedBeats)
                    {
                        EndSession(PwfErrorCodes.NetworkLost, NetworkLostMessage, eventContext);
                        return;
                    }
                    continue;
                }
                catch (Exception)
                {
                    // Transport failure: the server may be down, or someone may have
                    // blocked this domain to keep the app running. Either way the server
                    // has dropped (or will drop) the session, so we must not run forever.
                    if (++unanswered >= _options.MaxHeartbeatFailures)
                    {
                        EndUnansweredSession(plainRefusals, clockRefusals, eventContext);
                        return;
                    }
                    continue;
                }

                if (!beat.IsEnveloped)
                {
                    // A plain reply is a refusal (a plain success threw above). Shared IPs get
                    // rate limited legitimately, so 429 has its own, larger budget.
                    if (beat.StatusCode == TooManyRequests)
                    {
                        if (++rateLimited >= _options.MaxRateLimitedBeats)
                        {
                            EndSession(PwfErrorCodes.NetworkLost, NetworkLostMessage, eventContext);
                            return;
                        }
                        continue;
                    }

                    plainRefusals++;
                    if (IsClockRefusal(beat)) clockRefusals++;
                    if (++unanswered >= _options.MaxHeartbeatFailures)
                    {
                        EndUnansweredSession(plainRefusals, clockRefusals, eventContext);
                        return;
                    }
                    continue;
                }

                // Encrypted: the server is reachable and the clock is fine. Every count starts
                // over — and neither kind of failure ever resets the other, so alternating
                // them cannot keep the loop alive either.
                unanswered = plainRefusals = clockRefusals = rateLimited = 0;

                if (beat.Success) continue;

                string? code = beat.ErrorCode;
                if (PwfErrorCodes.EndsSession(code))
                {
                    EndSession(code!, beat.Message ?? "Your session has ended.", eventContext);
                    return;
                }
                // Unknown encrypted failure: transient, keep beating.
            }
        }

        // The failure budget ran out. When every plain refusal in the streak was the server's
        // replay check ("Request expired"), the clock is to blame — say so, since signing in
        // again cannot work until it is corrected.
        private void EndUnansweredSession(int plainRefusals, int clockRefusals, SynchronizationContext? eventContext)
        {
            if (plainRefusals > 0 && clockRefusals == plainRefusals)
                EndSession(PwfErrorCodes.ClockSkew, ClockSkewMessage, eventContext);
            else
                EndSession(PwfErrorCodes.NetworkLost, NetworkLostMessage, eventContext);
        }

        private static bool IsClockRefusal(PwfResponse reply)
        {
            string? message = reply.Message;
            return string.Equals(reply.ErrorCode, CryptoErrorCode, StringComparison.Ordinal)
                && message != null
                && message.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Ends this session on the server and stops the heartbeat. Safe to call when
        /// already signed out.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// Logging out does not unbind the key: it stays bound to this machine, which can
        /// sign in again at any time. To move the license to another computer, call
        /// <see cref="ResetHardwareIdAsync"/>.
        /// </remarks>
        public async Task<PwfResponse?> LogoutAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!IsSignedIn) return null;
            StopHeartbeat();

            string session = SessionId!;
            string key = LicenseKey ?? string.Empty;
            SessionId = null;

            try
            {
                return await PostEnvelopeAsync("/api/auth/logout.php", new Dictionary<string, object?>
                {
                    ["session_id"] = session,
                    ["license_key"] = key,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (PwfException)
            {
                // The local session is already gone; the server reaps it on timeout.
                return null;
            }
        }

        // ─────────────────── app content & updates ───────────────────

        /// <summary>
        /// App metadata and social links: display name, current version, download URL,
        /// login message, and the maintenance flags.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> GetAppInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetEnvelopeAsync("/api/app/info.php", null, cancellationToken);
        }

        /// <summary>
        /// Remote texts for the signed-in license, with per-key overrides applied over the
        /// application defaults. Edit them in the dashboard and shipped apps pick the
        /// change up without a release.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> GetTextsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(LicenseKey)) throw new InvalidOperationException("Sign in first — remote texts are resolved per license key.");
            return GetEnvelopeAsync("/api/app/text.php", LicenseKey, cancellationToken);
        }

        /// <summary>The active announcement slides configured for this application.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> GetSlidesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return PostEnvelopeAsync("/api/app/slides.php", new Dictionary<string, object?>
            {
                ["action"] = "get_slides",
            }, cancellationToken);
        }

        /// <summary>
        /// Asks whether a newer build exists. The reply carries
        /// <c>update_available</c> and, when true, an <c>update</c> object with the
        /// version, SHA-256, size and download URL.
        /// </summary>
        /// <param name="currentVersion">The version running right now, e.g. "1.4.2".</param>
        /// <param name="channel">"stable", "beta" or "alpha". Defaults to stable.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> CheckUpdateAsync(string currentVersion, string channel = "stable",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(currentVersion)) throw new ArgumentException("Current version is required.", nameof(currentVersion));
            return PostEnvelopeAsync("/api/update/check.php", new Dictionary<string, object?>
            {
                ["v"] = currentVersion,
                ["channel"] = string.IsNullOrWhiteSpace(channel) ? "stable" : channel,
                ["hwid"] = HardwareId,
                ["license_key"] = LicenseKey ?? string.Empty,
            }, cancellationToken);
        }

        /// <summary>Counts a click on one of the app's social links.</summary>
        /// <param name="linkId">The numeric id from <see cref="GetAppInfoAsync"/>.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> TrackSocialClickAsync(int linkId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return PostEnvelopeAsync("/api/app/social-click.php", new Dictionary<string, object?>
            {
                ["link_id"] = linkId,
            }, cancellationToken);
        }

        // ───────────────── trials & self-service ─────────────────

        /// <summary>
        /// Requests a free trial key for this machine. Trials are opt-in per application;
        /// when they are off the reply carries <see cref="PwfErrorCodes.TrialDisabled"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> CreateTrialAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return PostPlainAsync("/api/auth/trial.php", new Dictionary<string, object?>
            {
                ["hwid"] = HardwareId,
            }, cancellationToken);
        }

        /// <summary>
        /// Moves a license to a new PC, instantly and without waiting for the developer:
        /// unbinds the key from every machine it is bound to so the next
        /// <see cref="LoginAsync"/> binds it to this one. Subject to the application's
        /// self-service policy and cooldown.
        /// </summary>
        /// <param name="licenseKey">The customer's key.</param>
        /// <param name="reason">
        /// Optional note stored with the reset, e.g. "New laptop". At most 255 characters
        /// (longer text is cut); defaults to "Reset from app".
        /// </param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>
        /// On success <see cref="PwfResponse.Message"/> confirms the reset and
        /// <c>next_reset_at</c> (UTC, ISO 8601) says when the next one is allowed. On failure
        /// <see cref="PwfResponse.ErrorCode"/> is <see cref="PwfErrorCodes.InvalidKey"/>,
        /// <see cref="PwfErrorCodes.KeyNotActive"/>, <see cref="PwfErrorCodes.NoHwid"/>,
        /// <see cref="PwfErrorCodes.RateLimited"/> (the message says how many hours to wait) or
        /// <see cref="PwfErrorCodes.SelfResetDisabled"/>, and <see cref="PwfResponse.Message"/>
        /// is safe to show the user.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The typical flow: <see cref="LoginAsync"/> fails with
        /// <see cref="PwfErrorCodes.HwidMismatch"/> or <see cref="PwfErrorCodes.DeviceLimit"/>,
        /// the user confirms they want to move the license here, you call this method, then
        /// call <see cref="LoginAsync"/> again.
        /// </para>
        /// <para>
        /// The developer decides whether self-service resets are allowed for the application
        /// and how long the cooldown between two resets is (12 hours by default). A reset
        /// unbinds every device of the key and ends all of its sessions on the server — a
        /// session this client holds on the same key included, which a running heartbeat then
        /// reports through <see cref="SessionEnded"/>.
        /// </para>
        /// </remarks>
        public Task<PwfResponse> ResetHardwareIdAsync(string licenseKey, string? reason = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) throw new ArgumentException("License key is required.", nameof(licenseKey));
            return PostPlainAsync("/api/customer/reset-hwid.php", new Dictionary<string, object?>
            {
                ["key"] = licenseKey.Trim(),
                ["reason"] = ResetReason(reason),
            }, cancellationToken);
        }

        /// <summary>
        /// Queues a hardware-reset request for the owner to review. Obsolete: the PWF Auth
        /// dashboard does not show these requests yet, so nobody can approve one — use
        /// <see cref="ResetHardwareIdAsync"/> for an instant self-service reset.
        /// </summary>
        /// <param name="licenseKey">The customer's key.</param>
        /// <param name="reason">What the customer says happened.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// The reply is deliberately identical whether or not the key exists, so it cannot
        /// be used to probe which keys are real.
        /// </remarks>
        [Obsolete("Reset requests are not shown in the PWF Auth dashboard yet. Use ResetHardwareIdAsync for an instant self-service reset.")]
        public Task<PwfResponse> RequestHardwareResetAsync(string licenseKey, string reason,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) throw new ArgumentException("License key is required.", nameof(licenseKey));
            return PostPlainAsync("/api/auth/request-hwid-reset.php", new Dictionary<string, object?>
            {
                ["license_key"] = licenseKey,
                ["reason"] = reason ?? "Device changed",
            }, cancellationToken);
        }

        // ─────────────────── user accounts ───────────────────

        /// <summary>
        /// Registers an end-user account (the username/password half of the platform, as
        /// opposed to license keys).
        /// </summary>
        /// <param name="username">Desired username, unique per application.</param>
        /// <param name="password">Password; stored hashed server-side.</param>
        /// <param name="email">Contact e-mail.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> RegisterAccountAsync(string username, string password, string? email = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.", nameof(password));
            return PostPlainAsync("/api/auth/account-register.php", new Dictionary<string, object?>
            {
                ["username"] = username,
                ["password"] = password,
                ["email"] = email ?? string.Empty,
            }, cancellationToken);
        }

        /// <summary>
        /// Signs an end-user account in and opens a session bound to this machine, exactly
        /// like a license login — the heartbeat loop applies the same way.
        /// </summary>
        /// <param name="username">Account username.</param>
        /// <param name="password">Account password.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<PwfResponse> AccountLoginAsync(string username, string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));

            PwfResponse response = await PostPlainAsync("/api/auth/account-login.php", new Dictionary<string, object?>
            {
                ["username"] = username,
                ["password"] = password,
                ["hwid"] = HardwareId,
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success)
            {
                SessionId = response.GetString("session_id");
                LicenseKey = response.GetString("license_key");
                HeartbeatIntervalSeconds = Math.Max(5, response.GetInt32("heartbeat_interval", 30));
            }
            return response;
        }

        /// <summary>Changes an end-user account password.</summary>
        /// <param name="username">Account username.</param>
        /// <param name="currentPassword">Verified before the change.</param>
        /// <param name="newPassword">The replacement.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PwfResponse> ChangeAccountPasswordAsync(string username, string currentPassword, string newPassword,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return PostPlainAsync("/api/auth/change-password.php", new Dictionary<string, object?>
            {
                ["username"] = username,
                ["current_password"] = currentPassword,
                ["new_password"] = newPassword,
            }, cancellationToken);
        }

        // ─────────────────── transports ───────────────────

        /// <summary>
        /// POST with the AES envelope in both directions — the transport the session and
        /// app-content endpoints require.
        /// </summary>
        /// <param name="path">Endpoint path, e.g. "/api/auth/login.php".</param>
        /// <param name="body">Fields to send.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// A plain JSON failure (a bad app secret, a rate limit) is returned as a failed
        /// <see cref="PwfResponse"/> as usual; a plain JSON <em>success</em> is refused.
        /// </remarks>
        /// <exception cref="PwfSecurityException">
        /// The reply was not encrypted but claimed success. These endpoints encrypt every
        /// reply once the app secret is accepted, so it did not come from the license server.
        /// </exception>
        public async Task<PwfResponse> PostEnvelopeAsync(string path, IDictionary<string, object?> body,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            PwfResponse reply = await PostEnvelopeOnceAsync(path, body, cancellationToken).ConfigureAwait(false);
            // A wrong clock on this machine: the server refused the timestamp and sent its own
            // time. Shift ours by the difference and send once more, freshly stamped.
            if (_options.AutoCorrectClock && TryCorrectClock(reply))
                reply = await PostEnvelopeOnceAsync(path, body, cancellationToken).ConfigureAwait(false);
            return reply;
        }

        private async Task<PwfResponse> PostEnvelopeOnceAsync(string path, IDictionary<string, object?> body,
            CancellationToken cancellationToken)
        {
            string payload = _crypto.Encrypt(SerializeBody(body));
            using (HttpRequestMessage request = CreateRequest(HttpMethod.Post, path))
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                return await SendAsync(request, true, cancellationToken).ConfigureAwait(false);
            }
        }

        // The server's plain refusal for a timestamp outside its window carries
        // "reason": "CLOCK_SKEW" and "server_time" (unix seconds). Only a plain reply qualifies:
        // the server never sends that refusal encrypted.
        private bool TryCorrectClock(PwfResponse reply)
        {
            JsonElement reason, serverTime;
            long seconds;
            if (reply.IsEnveloped
                || !reply.TryGetProperty("reason", out reason) || reason.ValueKind != JsonValueKind.String
                || !string.Equals(reason.GetString(), PwfErrorCodes.ClockSkew, StringComparison.Ordinal)
                || !reply.TryGetProperty("server_time", out serverTime) || serverTime.ValueKind != JsonValueKind.Number
                || !serverTime.TryGetInt64(out seconds) || seconds <= 0)
                return false;
            _crypto.ClockOffsetSeconds = seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return true;
        }

        /// <summary>
        /// GET whose reply is enveloped. Pass a license key for endpoints that read
        /// <c>Authorization: Bearer</c>.
        /// </summary>
        /// <param name="path">Endpoint path, e.g. "/api/app/info.php".</param>
        /// <param name="bearerLicenseKey">License key, or null when not required.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// A plain JSON failure is returned as a failed <see cref="PwfResponse"/>; a plain
        /// JSON <em>success</em> is refused.
        /// </remarks>
        /// <exception cref="PwfSecurityException">
        /// The reply was not encrypted but claimed success, so it did not come from the
        /// license server.
        /// </exception>
        public async Task<PwfResponse> GetEnvelopeAsync(string path, string? bearerLicenseKey = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpRequestMessage request = CreateRequest(HttpMethod.Get, path))
            {
                if (!string.IsNullOrEmpty(bearerLicenseKey))
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerLicenseKey);
                return await SendAsync(request, true, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// POST plain JSON — for the endpoints that do not speak the envelope (trials,
        /// hardware resets, user accounts).
        /// </summary>
        /// <param name="path">Endpoint path, e.g. "/api/auth/trial.php".</param>
        /// <param name="body">Fields to send.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<PwfResponse> PostPlainAsync(string path, IDictionary<string, object?> body,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpRequestMessage request = CreateRequest(HttpMethod.Post, path))
            {
                request.Content = new StringContent(SerializeBody(body), Encoding.UTF8, "application/json");
                return await SendAsync(request, false, cancellationToken).ConfigureAwait(false);
            }
        }

        // Headers go on each request, never on HttpClient.DefaultRequestHeaders: the
        // HttpClient may be the caller's, shared with the rest of their application.
        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("X-App-Secret", _options.AppSecret);
            return request;
        }

        private async Task<PwfResponse> SendAsync(HttpRequestMessage request, bool requireEnvelope, CancellationToken cancellationToken)
        {
            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PwfHttpException(0, "The license server did not respond in time.", string.Empty);
            }

            using (httpResponse)
            {
                int status = (int)httpResponse.StatusCode;
                string raw = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    throw new PwfHttpException(status,
                        "License server returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + " with an empty body.",
                        string.Empty);
                }

                if (CryptoEnvelope.LooksLikeEnvelope(raw))
                {
                    return PwfResponse.FromReply(_crypto.Decrypt(raw), true, status);
                }

                PwfResponse plain;
                try
                {
                    plain = PwfResponse.FromReply(raw, false, status);
                }
                catch (PwfException)
                {
                    // Not JSON at all — nearly always a proxy/CDN error page or a wrong base URL.
                    throw new PwfHttpException(status,
                        "License server returned HTTP " + status.ToString(CultureInfo.InvariantCulture)
                        + " with a non-JSON body. Check the API base URL.",
                        Snippet(raw));
                }

                // The API answers failures with its own {success,error_code,message}
                // shape; hand those back so callers can react. Anything else with a
                // failing status is a transport problem, not an API answer.
                JsonElement ignored;
                if (status >= 400 && !plain.TryGetProperty("success", out ignored))
                {
                    throw new PwfHttpException(status,
                        "License server returned HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
                        Snippet(raw));
                }

                // Envelope endpoints encrypt EVERY reply once the request is verified; only
                // refusals that happen before that point (bad secret, rate limit, wrong
                // method, a clock too far off) travel as plain JSON, and those are all
                // failures. A plain success therefore came from a proxy, a hosts-file
                // redirect or a fake server — and accepting it would let any of them unlock
                // the application.
                if (requireEnvelope && plain.Success)
                {
                    throw new PwfSecurityException("The license server's reply was not encrypted, so it cannot be trusted.");
                }
                return plain;
            }
        }

        private static string SerializeBody(IDictionary<string, object?> body)
        {
            return JsonSerializer.Serialize(body);
        }

        private static string Snippet(string raw)
        {
            return raw.Length <= 200 ? raw : raw.Substring(0, 200);
        }

        private static string ResetReason(string? reason)
        {
            string text = string.IsNullOrWhiteSpace(reason) ? DefaultResetReason : reason!.Trim();
            if (text.Length <= MaxResetReasonLength) return text;

            // Never cut between the two halves of a surrogate pair (an emoji, say): a lone
            // surrogate is invalid UTF-16 and makes the whole JSON body unreadable to PHP.
            int length = MaxResetReasonLength;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }

        private static string BuildUserAgent()
        {
            string version = string.Empty;
            try
            {
                Assembly assembly = typeof(PwfClient).Assembly;
                AssemblyInformationalVersionAttribute? info =
                    assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null) version = info.InformationalVersion ?? string.Empty;
                if (version.Length == 0)
                {
                    Version? assemblyVersion = assembly.GetName().Version;
                    if (assemblyVersion != null) version = assemblyVersion.ToString(3);
                }
            }
            catch (Exception)
            {
                // Reflection can be restricted in trimmed hosts; the header is informational only.
            }

            // Source Link appends "+<commit sha>" to the informational version.
            int plus = version.IndexOf('+');
            if (plus >= 0) version = version.Substring(0, plus);
            if (version.Length == 0) version = "0.0.0";

            return "PWFAuth-dotnet/" + version + " (+https://pwfauth.com)";
        }

        private void EndSession(string errorCode, string message, SynchronizationContext? eventContext)
        {
            SessionId = null;
            var args = new SessionEndedEventArgs(errorCode, message);

            if (eventContext != null)
            {
                int started = 0;
                try
                {
                    eventContext.Post(_ =>
                    {
                        Interlocked.Exchange(ref started, 1);
                        RaiseSessionEnded(args);
                    }, null);
                    return;
                }
                catch (Exception) when (Volatile.Read(ref started) == 0)
                {
                    // The captured context no longer accepts work — a WinForms UI thread
                    // that has already shut down throws here. A lost event would leave the
                    // app running unlicensed, so raise it right here instead.
                }
            }
            RaiseSessionEnded(args);
        }

        private void RaiseSessionEnded(SessionEndedEventArgs args)
        {
            EventHandler<SessionEndedEventArgs>? handler = SessionEnded;
            if (handler != null)
            {
                handler(this, args);
            }
        }

        /// <summary>
        /// Stops the heartbeat and releases the HTTP client. Call <see cref="LogoutAsync"/>
        /// first to end the server session now rather than at the server's session timeout.
        /// Neither one unbinds the key from this machine — see <see cref="ResetHardwareIdAsync"/>.
        /// </summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            StopHeartbeat();
            if (_ownsHttpClient) _http.Dispose();
        }
    }
}
