using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PWFAuth
{
    /// <summary>
    /// The AES-256-CBC + HMAC-SHA256 envelope the SDK-grade endpoints speak. Mirrors the
    /// server's PayloadCrypto exactly: <c>{"p": base64(IV || ciphertext), "t": unix, "s": hmac_hex(p + t)}</c>,
    /// with the encryption and MAC keys both derived from the app secret — so no extra
    /// key exchange is needed.
    /// </summary>
    /// <remarks>
    /// Exposed publicly so you can call endpoints this client has not wrapped yet.
    /// For everything the client already covers, use <see cref="PwfClient"/> instead.
    /// </remarks>
    public sealed class CryptoEnvelope
    {
        private readonly byte[] _encKey;
        private readonly byte[] _macKey;
        private readonly int _maxDriftSeconds;

        /// <summary>Builds the envelope codec for one application secret.</summary>
        /// <param name="appSecret">The 64-character hex secret from your dashboard.</param>
        /// <param name="maxDriftSeconds">
        /// How far a reply's timestamp may drift before it is rejected as a replay.
        /// Must match the server (300 seconds).
        /// </param>
        public CryptoEnvelope(string appSecret, int maxDriftSeconds = 300)
        {
            if (string.IsNullOrEmpty(appSecret)) throw new ArgumentException("App secret is required.", nameof(appSecret));
            _maxDriftSeconds = maxDriftSeconds;
            using (SHA256 sha = SHA256.Create())
            {
                _encKey = sha.ComputeHash(Encoding.UTF8.GetBytes("enc:" + appSecret));
            }
            using (SHA256 sha = SHA256.Create())
            {
                _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("mac:" + appSecret));
            }
        }

        /// <summary>Encrypts a request body into the wire envelope.</summary>
        /// <param name="json">The plain JSON body to protect.</param>
        public string Encrypt(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);

            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.UTF8.GetBytes(json);
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
            }

            byte[] combined = new byte[iv.Length + cipher.Length];
            Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
            Buffer.BlockCopy(cipher, 0, combined, iv.Length, cipher.Length);

            string p = Convert.ToBase64String(combined);
            long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string s = HmacHex(p + t.ToString(System.Globalization.CultureInfo.InvariantCulture));

            using (var stream = new System.IO.MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("p", p);
                    writer.WriteNumber("t", t);
                    writer.WriteString("s", s);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Verifies and decrypts a wire envelope back into plain JSON.</summary>
        /// <param name="envelopeJson">The <c>{p,t,s}</c> document the server returned.</param>
        /// <exception cref="PwfCryptoException">
        /// The signature failed, the timestamp drifted too far, or the payload is malformed.
        /// </exception>
        public string Decrypt(string envelopeJson)
        {
            string p, s;
            long t;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(envelopeJson))
                {
                    JsonElement root = doc.RootElement;
                    p = root.GetProperty("p").GetString() ?? string.Empty;
                    t = root.GetProperty("t").GetInt64();
                    s = root.GetProperty("s").GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                throw new PwfCryptoException("Invalid envelope format.", ex);
            }

            string expected = HmacHex(p + t.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!FixedTimeEquals(expected, s))
            {
                throw new PwfCryptoException("HMAC verification failed — wrong app secret, or the payload was tampered with.");
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - t) > _maxDriftSeconds)
            {
                throw new PwfCryptoException(
                    "Envelope timestamp is outside the accepted window — check this machine's system clock.");
            }

            byte[] combined;
            try { combined = Convert.FromBase64String(p); }
            catch (FormatException ex) { throw new PwfCryptoException("Malformed ciphertext.", ex); }
            if (combined.Length <= 16) throw new PwfCryptoException("Malformed ciphertext.");

            byte[] iv = new byte[16];
            byte[] cipher = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            Buffer.BlockCopy(combined, 16, cipher, 0, cipher.Length);

            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = _encKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                    {
                        byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                        return Encoding.UTF8.GetString(plain);
                    }
                }
            }
            catch (CryptographicException ex)
            {
                throw new PwfCryptoException("Decryption failed — wrong app secret or corrupted payload.", ex);
            }
        }

        /// <summary>True when the document looks like a <c>{p,t,s}</c> envelope.</summary>
        /// <param name="json">Any JSON body.</param>
        public static bool LooksLikeEnvelope(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return false;
                    JsonElement ignored;
                    return root.TryGetProperty("p", out ignored)
                        && root.TryGetProperty("t", out ignored)
                        && root.TryGetProperty("s", out ignored);
                }
            }
            catch (JsonException) { return false; }
        }

        private string HmacHex(string message)
        {
            using (var hmac = new HMACSHA256(_macKey))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Compares two hex signatures without an early exit. A plain string comparison
        /// leaks, through timing, how many leading characters matched — which is enough
        /// to walk a forged signature into place one character at a time.
        /// </summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
