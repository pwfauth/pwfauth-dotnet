using System;

namespace PWFAuth
{
    /// <summary>Base class for every error this client raises.</summary>
    public class PwfException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        public PwfException(string message) : base(message) { }

        /// <summary>Creates the exception with a message and the underlying cause.</summary>
        public PwfException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// No usable reply from the license server. Either it could not be reached at all —
    /// no connection, DNS, a firewall, a proxy, TLS, or no answer in time — and
    /// <see cref="StatusCode"/> is 0 (the network error is the
    /// <see cref="Exception.InnerException"/>); or it answered, but not with a usable
    /// body — a proxy/CDN error page, an empty response, or a failing status with no API
    /// payload. The most common cause of the latter is a wrong base URL, which is why the
    /// status code is surfaced rather than swallowed.
    /// </summary>
    public class PwfHttpException : PwfException
    {
        /// <summary>The HTTP status code the server returned; 0 when there was no reply at all.</summary>
        public int StatusCode { get; }

        /// <summary>The first part of the response body, for diagnostics.</summary>
        public string ResponseSnippet { get; }

        /// <summary>Creates the exception.</summary>
        public PwfHttpException(int statusCode, string message, string responseSnippet)
            : base(message)
        {
            StatusCode = statusCode;
            ResponseSnippet = responseSnippet ?? string.Empty;
        }

        /// <summary>Creates the exception with the underlying network error.</summary>
        public PwfHttpException(int statusCode, string message, string responseSnippet, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseSnippet = responseSnippet ?? string.Empty;
        }
    }

    /// <summary>
    /// The encrypted envelope could not be built or verified: a wrong app secret, a
    /// tampered payload, or a client clock more than five minutes off the server.
    /// </summary>
    public class PwfCryptoException : PwfException
    {
        /// <summary>Creates the exception.</summary>
        public PwfCryptoException(string message) : base(message) { }

        /// <summary>Creates the exception with the underlying cause.</summary>
        public PwfCryptoException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// A reply that must be encrypted came back as plain JSON claiming success. The
    /// license server never sends a plain success on those endpoints — once it has
    /// verified a request, every reply is encrypted — so this answer came from something
    /// else: a proxy, a hosts-file redirect or a fake server standing in for pwfauth.com.
    /// Never unlock the application on it; inside the heartbeat it counts as an
    /// unreachable server.
    /// </summary>
    public class PwfSecurityException : PwfException
    {
        /// <summary>Creates the exception.</summary>
        public PwfSecurityException(string message) : base(message) { }

        /// <summary>Creates the exception with the underlying cause.</summary>
        public PwfSecurityException(string message, Exception innerException) : base(message, innerException) { }
    }
}
