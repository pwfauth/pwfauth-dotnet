using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PWFAuth
{
    internal static class ServerAuth
    {
        internal const string Production = "https://pwfauth.com";

        /// <summary>
        /// https://pwfauth.com, or a server on this computer for development (1.5.0): http(s) to
        /// localhost, 127.0.0.1 or [::1], optionally with a port and a plain path such as /pwfauth.
        /// A loopback origin is no shortcut: every reply still needs the pinned signature.
        /// </summary>
        internal static void ValidateOrigin(string url)
        {
            if (url != null && (url.TrimEnd('/') == Production || IsLoopback(url))) return;
            throw new PwfSecurityException("Only https://pwfauth.com is supported (or, for development, a server on this computer: http://localhost or http://127.0.0.1).");
        }

        private static bool IsLoopback(string url)
        {
            if (url.IndexOfAny(new[] {'\\', ' ', '\t', '\r', '\n', '?', '#'}) >= 0) return false;
            Uri? uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (uri.UserInfo.Length > 0) return false;
            // the host as written: a literal loopback address or "localhost", never a name that only resolves there
            string host = uri.Host;
            if (!(string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]")) return false;
            string path = uri.AbsolutePath;
            if (path.Contains("//") || path.Contains("/./") || path.Contains("/../") || path.Contains("%")) return false;
            foreach (char c in path)
                if (!(c == '/' || c == '-' || c == '_' || c == '.' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
            return true;
        }
        internal static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.IndexOfAny(new[] {'\\', '#', '\r', '\n'}) >= 0) throw new PwfSecurityException("Invalid API path.");
        }
        internal static string Nonce()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Hex(bytes);
        }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes)); }
        internal static void Verify(string nonce, string method, string path, byte[] request, int status, byte[] body, string signature)
        {
            var material = string.Join("\n", "PWF-REPLY-V1", nonce, method, path, Hash(request), status.ToString(CultureInfo.InvariantCulture), Hash(body));
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String("rJgpZLCvg6z08gEFN4l/x0FiSYQTsuff7bpb5BHFl7/263J0GOk4YCyt+kXftiURGN4J96KZ4ktAV2tgbu5pD0LrUa97YLOOkDYIepzbYeSgL8CMQIiDkP86XEp/q2wC7y8weqyRV4fUV8SFNZ3MgzwRI+aJxYyJGsPheK7Olq2HD5Dx+idjAz8PEbxLWqbNHnS2PdiZ34jC8darQqKIEW65oBeE6ihulLLDqn1miV8tiEjorROyaCE3oScHzyHB7ZPH2YWcW7vYjgk9ED93zy3/DrAwyfyWfK2jvb231OFI5+OWKR7XsErlOm3xgAwUkcN3kLxi5/tIbkq3bFOmOJEr0hWO/nmUmlyCnBA400xjwEUajXmwILugv5QlLkhKtK31upMTTA9XEloerHyCN4NUGx3+4NqNP07cFkTcH1PpinFRp/iVHLX6M2Zabd+V0rynn2X9STtWmRrKPMMBAGV+hS8RMg4oliIqmf8FKooQ3HGaL7EoV4n0wgzXG4v9"), Exponent = new byte[] {1, 0, 1} });
                    if (!rsa.VerifyData(Encoding.UTF8.GetBytes(material), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException();
                }
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException)
            { throw new PwfSecurityException("Missing or invalid server signature."); }
        }
    }
}
