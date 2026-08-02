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
        /// <summary>The key is bound to a different machine.</summary>
        public const string HwidMismatch = "HWID_MISMATCH";
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

        /// <summary>
        /// Raised by the client (never by the server) when the license server has been
        /// unreachable for <see cref="PwfClientOptions.MaxHeartbeatFailures"/> consecutive
        /// beats. The server drops the session on its own timeout, so a client that kept
        /// running would be usable simply by blocking the domain in a firewall.
        /// </summary>
        public const string NetworkLost = "NETWORK_LOST";

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
