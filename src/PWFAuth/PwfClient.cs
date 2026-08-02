using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
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
    /// client.StartHeartbeat();
    /// </code>
    /// </example>
    public sealed class PwfClient : IDisposable
    {
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
        /// Supply your own (from IHttpClientFactory, or one with a proxy configured) and
        /// this client will not dispose it. Leave null to get a private one.
        /// </param>
        public PwfClient(PwfClientOptions options, HttpClient? httpClient = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();

            _options = options;
            _crypto = new CryptoEnvelope(options.AppSecret, options.MaxClockDriftSeconds);
            _baseUrl = options.BaseUrl.TrimEnd('/');
            _ownsHttpClient = httpClient == null;
            _http = httpClient ?? new HttpClient();
            _http.Timeout = options.Timeout;
            HardwareId = string.IsNullOrWhiteSpace(options.HardwareId)
                ? PWFAuth.HardwareId.Get()
                : options.HardwareId!;
        }

        /// <summary>
        /// Fires when the session stops being valid. Sign the user out here — the server
        /// has already dropped the session, so continuing to run is not licensed use.
        /// </summary>
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
        /// server for <see cref="PwfClientOptions.MaxHeartbeatFailures"/> beats in a row.
        /// Calling it twice is a no-op.
        /// </summary>
        public void StartHeartbeat()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PwfClient));
                if (!IsSignedIn) throw new InvalidOperationException("No open session — call LoginAsync first.");
                if (_heartbeatTask != null && !_heartbeatTask.IsCompleted) return;

                _heartbeatCts = new CancellationTokenSource();
                _heartbeatTask = RunHeartbeatAsync(_heartbeatCts.Token);
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
        public async Task RunHeartbeatAsync(CancellationToken cancellationToken)
        {
            int failures = 0;

            while (!cancellationToken.IsCancellationRequested && IsSignedIn)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                if (!IsSignedIn) return;

                PwfResponse? beat = null;
                try
                {
                    beat = await HeartbeatAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    // Transport failure: the server may be down, or someone may have
                    // blocked this domain to keep the app running. Either way the server
                    // has dropped (or will drop) the session, so we must not run forever.
                    failures++;
                    if (failures >= _options.MaxHeartbeatFailures)
                    {
                        EndSession(PwfErrorCodes.NetworkLost,
                            "Cannot reach the license server. Please check your connection and sign in again.");
                        return;
                    }
                    continue;
                }

                failures = 0;

                if (beat.Success) continue;

                string? code = beat.ErrorCode;
                if (PwfErrorCodes.EndsSession(code))
                {
                    EndSession(code!, beat.Message ?? "Your session has ended.");
                    return;
                }
                // Unknown non-success: treat as transient and keep beating.
            }
        }

        /// <summary>
        /// Closes the session server-side and frees the device seat. Safe to call when
        /// already signed out.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
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
        /// Queues a hardware-reset request for the owner to review — the flow for a
        /// customer who changed machines.
        /// </summary>
        /// <param name="licenseKey">The customer's key.</param>
        /// <param name="reason">What the customer says happened.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// The reply is deliberately identical whether or not the key exists, so it cannot
        /// be used to probe which keys are real.
        /// </remarks>
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
        public async Task<PwfResponse> PostEnvelopeAsync(string path, IDictionary<string, object?> body,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string payload = _crypto.Encrypt(SerializeBody(body));
            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path))
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("X-App-Secret", _options.AppSecret);
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// GET whose reply is enveloped. Pass a license key for endpoints that read
        /// <c>Authorization: Bearer</c>.
        /// </summary>
        /// <param name="path">Endpoint path, e.g. "/api/app/info.php".</param>
        /// <param name="bearerLicenseKey">License key, or null when not required.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<PwfResponse> GetEnvelopeAsync(string path, string? bearerLicenseKey = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path))
            {
                request.Headers.TryAddWithoutValidation("X-App-Secret", _options.AppSecret);
                if (!string.IsNullOrEmpty(bearerLicenseKey))
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerLicenseKey);
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// POST plain JSON — for the endpoints that do not speak the envelope (trials,
        /// hardware-reset requests, user accounts).
        /// </summary>
        /// <param name="path">Endpoint path, e.g. "/api/auth/trial.php".</param>
        /// <param name="body">Fields to send.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<PwfResponse> PostPlainAsync(string path, IDictionary<string, object?> body,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path))
            {
                request.Content = new StringContent(SerializeBody(body), Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("X-App-Secret", _options.AppSecret);
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<PwfResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
                    return PwfResponse.Parse(_crypto.Decrypt(raw));
                }

                PwfResponse plain;
                try
                {
                    plain = PwfResponse.Parse(raw);
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

        private void EndSession(string errorCode, string message)
        {
            SessionId = null;
            EventHandler<SessionEndedEventArgs>? handler = SessionEnded;
            if (handler != null)
            {
                handler(this, new SessionEndedEventArgs(errorCode, message));
            }
        }

        /// <summary>
        /// Stops the heartbeat and releases the HTTP client. Call
        /// <see cref="LogoutAsync"/> first if you want the device seat freed immediately
        /// rather than at the server's session timeout.
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
