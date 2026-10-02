using System;
using System.Collections.Generic;

namespace PWFAuth
{
    /// <summary>
    /// Error codes the PWF Auth API returns in <c>error_code</c>.
    /// </summary>
    public static class PwfErrorCodes
    {
        /// <summary>A required field was missing from the request.</summary>
        public const string MissingFields = "MISSING_FIELDS";
        /// <summary>The license key does not exist for this application.</summary>
        public const string InvalidKey = "INVALID_KEY";
        /// <summary>Username/password rejected (user accounts and admin login).</summary>
        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        /// <summary>
        /// The key is bound to a different machine (single-device keys; multi-device keys
        /// report <see cref="DeviceLimit"/>). <see cref="PwfClient.ResetHardwareIdAsync"/> moves it.
        /// </summary>
        public const string HwidMismatch = "HWID_MISMATCH";
        /// <summary>
        /// A multi-device key is already bound to its maximum number of machines. The login
        /// reply also carries <c>devices_used</c> and <c>max_devices</c>.
        /// </summary>
        public const string DeviceLimit = "DEVICE_LIMIT";
        /// <summary>
        /// The application runs in open-access mode and has reached its daily cap of new
        /// users. Nothing is wrong with the key; try again later.
        /// </summary>
        public const string OpenAccessLimit = "OPEN_ACCESS_LIMIT";
        /// <summary>The license or account has expired.</summary>
        public const string Expired = "EXPIRED";
        /// <summary>The license or account was banned by the owner.</summary>
        public const string Banned = "BANNED";
        /// <summary>The license or account is temporarily paused.</summary>
        public const string Paused = "PAUSED";
        /// <summary>The application is in maintenance mode; clients are locked out.</summary>
        public const string Maintenance = "MAINTENANCE";
        /// <summary>The session no longer exists server-side.</summary>
        public const string SessionExpired = "SESSION_EXPIRED";
        /// <summary>The session belongs to a different license key.</summary>
        public const string SessionMismatch = "SESSION_MISMATCH";
        /// <summary>The key or account behind a live session was deleted.</summary>
        public const string SessionRevoked = "SESSION_REVOKED";
        /// <summary>An admin cleared the key's hardware binding, or another machine took it.</summary>
        public const string HwidReset = "HWID_RESET";
        /// <summary>Free trials are switched off for this application.</summary>
        public const string TrialDisabled = "TRIAL_DISABLED";
        /// <summary>A trial was already issued to this machine.</summary>
        public const string TrialUsed = "TRIAL_USED";
        /// <summary>The per-IP trial cap was reached.</summary>
        public const string TrialLimit = "TRIAL_LIMIT";

        /// <summary>Self-service reset refused: the key is expired, banned or paused.</summary>
        public const string KeyNotActive = "KEY_NOT_ACTIVE";
        /// <summary>Self-service reset refused: the key is not bound to any machine, so there is nothing to reset.</summary>
        public const string NoHwid = "NO_HWID";
        /// <summary>
        /// Too many requests. From <see cref="PwfClient.ResetHardwareIdAsync"/>: the cooldown
        /// since the key's last reset has not passed yet — the message says how many hours to wait.
        /// </summary>
        public const string RateLimited = "RATE_LIMITED";
        /// <summary>The developer has turned self-service hardware resets off for this application.</summary>
        public const string SelfResetDisabled = "SELF_RESET_DISABLED";

        /// <summary>The application requires a license key to create an account: use <see cref="PwfClient.RegisterAccountWithKeyAsync"/>.</summary>
        public const string KeyRequired = "KEY_REQUIRED";
        /// <summary>The key was already added to an account (by sign-up or <see cref="PwfClient.RedeemKeyAsync(string, string, string, System.Threading.CancellationToken)"/>).</summary>
        public const string KeyAlreadyUsed = "KEY_ALREADY_USED";
        /// <summary>The key has already been activated by a license login, so it cannot be added to an account.</summary>
        public const string KeyInUse = "KEY_IN_USE";
        /// <summary>From <see cref="PwfClient.LoginAsync"/>: the key was added to an account; sign in with that account instead.</summary>
        public const string KeyRedeemed = "KEY_REDEEMED";
        /// <summary>The account already has lifetime access, so a key would add nothing.</summary>
        public const string AlreadyLifetime = "ALREADY_LIFETIME";
        /// <summary>Account sign-up: the username is taken in this application.</summary>
        public const string UsernameExists = "USERNAME_EXISTS";

        /// <summary>
        /// Raised by the client (never by the server) when the license server has not
        /// answered <see cref="PwfClientOptions.MaxHeartbeatFailures"/> beats in a row — no
        /// reply, or only a plain refusal it sends before it can verify a request — or when
        /// <see cref="PwfClientOptions.MaxRateLimitedBeats"/> beats in a row got HTTP 429. The
        /// server drops the session on its own timeout, so a client that kept running would
        /// be usable simply by blocking the domain in a firewall.
        /// </summary>
        public const string NetworkLost = "NETWORK_LOST";

        /// <summary>
        /// Raised by the client (never by the server) when the license server refused
        /// <see cref="PwfClientOptions.MaxHeartbeatFailures"/> beats in a row because this
        /// computer's clock is wrong: every request is timestamped, and the server rejects
        /// one more than five minutes off. Ask the user to correct the date and time, then
        /// sign in again.
        /// </summary>
        public const string ClockSkew = "CLOCK_SKEW";

        private static readonly HashSet<string> SessionEnding = new HashSet<string>(StringComparer.Ordinal)
        {
            Banned, Paused, Expired, HwidReset, Maintenance,
            SessionRevoked, SessionExpired, SessionMismatch, NetworkLost,
        };

        /// <summary>
        /// True when the code means the session is gone for good and the user must be
        /// signed out — as opposed to a transient failure worth retrying.
        /// </summary>
        /// <param name="errorCode">The <c>error_code</c> from a heartbeat response.</param>
        public static bool EndsSession(string? errorCode)
        {
            return !string.IsNullOrEmpty(errorCode) && SessionEnding.Contains(errorCode!);
        }
    }
}
