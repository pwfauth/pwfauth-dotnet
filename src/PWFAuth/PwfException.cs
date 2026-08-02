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
    /// The license server answered, but not with a usable body — a proxy/CDN error page,
    /// an empty response, or a failing status with no API payload. The most common cause
    /// is a wrong base URL, which is why the status code is surfaced rather than swallowed.
    /// </summary>
    public class PwfHttpException : PwfException
    {
        /// <summary>The HTTP status code the server returned.</summary>
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
}
