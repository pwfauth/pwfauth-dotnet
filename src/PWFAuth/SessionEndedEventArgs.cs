using System;

namespace PWFAuth
{
    /// <summary>
    /// Raised when a live session stops being valid — the moment to sign the user out.
    /// </summary>
    public sealed class SessionEndedEventArgs : EventArgs
    {
        /// <summary>Creates the event payload.</summary>
        /// <param name="errorCode">One of <see cref="PwfErrorCodes"/>.</param>
        /// <param name="message">A message safe to show the end user.</param>
        public SessionEndedEventArgs(string errorCode, string message)
        {
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// Why the session ended: BANNED, PAUSED, EXPIRED, HWID_RESET, MAINTENANCE,
        /// SESSION_REVOKED, SESSION_EXPIRED, SESSION_MISMATCH, NETWORK_LOST or CLOCK_SKEW.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>The server's explanation (or the client's, for NETWORK_LOST and CLOCK_SKEW).</summary>
        public string Message { get; }

        /// <summary>
        /// True when the cause was the license server being unreachable rather than a
        /// decision by the application owner — worth wording differently in your UI.
        /// </summary>
        public bool IsNetworkFailure
        {
            get { return string.Equals(ErrorCode, PwfErrorCodes.NetworkLost, StringComparison.Ordinal); }
        }
    }
}
