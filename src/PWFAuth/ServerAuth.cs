using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PWFAuth
{
    internal static class ServerAuth
    {
        internal static void ValidateOrigin(string url)
        {
            if (url == null || url.TrimEnd('/') != "https://pwfauth.com") throw new PwfSecurityException("Only https://pwfauth.com is supported.");
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
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String("xF7ROKevJDFTnyfYq5S1QkdwMkY8tMAFhMgi5cMIFdpdCO7xvEtirmVUW+sHvttanDPXMQDGgDKdkbspsHpfGbr7vs6ScCltjrGSMx0FansQSvmYp0DVwYVByB0YEhyaej4B+FmwATfHLdFr+XAXN+C5BWGpVNYfnt0WknasAn/FTncVr01hk4win2iz3C6avZI/T+YgtsWFJioLRmfuUcMSZ+Hs2zEfCawImh6UPQhsotD8ZHgje8IsYVQTosA7WGX0Cyt1OoT1PJbBxsTOQaWd6DWqNo4R6GUZzdkb+9Vjj71lmz92Y3rZsff+OIjdbToQqB/sappj4y5tQVtklXnDDs/vW45QAWS6PmKjUFuMetkxOSguUO4BbU2Qk4+/Fp60w2y2WSQuCg4g1nrAaJAwb2b/h009dIlxzTwSPk8nfSlnIRDNbUxQkIUQHs/PgNag1qKc75+kZ6/0VXoP+2uky8of/4ldFy6FxzRl6FO8jEgrDl8jZpwfiNipz7+p"), Exponent = new byte[] {1, 0, 1} });
                    if (!rsa.VerifyData(Encoding.UTF8.GetBytes(material), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException();
                }
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException)
            { throw new PwfSecurityException("Missing or invalid server signature."); }
        }
    }
}
