using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PWFAuth
{
    /// <summary>
    /// One API reply. The PWF API returns open-ended JSON (fields differ per endpoint and
    /// grow over time), so this keeps the whole document reachable through
    /// <see cref="Root"/> while giving typed access to the fields every reply carries.
    /// </summary>
    public sealed class PwfResponse
    {
        private readonly JsonElement _root;

        private PwfResponse(JsonElement root, string rawJson)
        {
            _root = root;
            RawJson = rawJson;
        }

        /// <summary>Parses an API reply. Throws <see cref="PwfException"/> if it is not a JSON object.</summary>
        /// <param name="json">The raw JSON body.</param>
        public static PwfResponse Parse(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    // Clone detaches the element from the document we are about to dispose.
                    return new PwfResponse(doc.RootElement.Clone(), json);
                }
            }
            catch (JsonException ex)
            {
                throw new PwfException("The license server returned a body that is not valid JSON.", ex);
            }
        }

        /// <summary>Parses a reply and records how it travelled.</summary>
        /// <param name="json">The JSON body — decrypted already when it came in an envelope.</param>
        /// <param name="enveloped">True when it came in an envelope that verified.</param>
        /// <param name="statusCode">The HTTP status of the reply.</param>
        internal static PwfResponse FromReply(string json, bool enveloped, int statusCode)
        {
            PwfResponse response = Parse(json);
            response.IsEnveloped = enveloped;
            response.StatusCode = statusCode;
            return response;
        }

        /// <summary>
        /// True when the reply came in an envelope that verified — something only the license
        /// server (or a holder of the app secret) can produce. False for plain JSON and for
        /// responses built with <see cref="Parse"/>.
        /// </summary>
        internal bool IsEnveloped { get; private set; }

        /// <summary>The HTTP status of the reply; 0 for responses built with <see cref="Parse"/>.</summary>
        internal int StatusCode { get; private set; }

        /// <summary>The raw JSON body exactly as the server sent it.</summary>
        public string RawJson { get; }

        /// <summary>The parsed root element — use this for endpoint-specific fields.</summary>
        public JsonElement Root { get { return _root; } }

        /// <summary>True when the API reported success.</summary>
        public bool Success { get { return GetBoolean("success", false); } }

        /// <summary>
        /// The machine-readable failure reason, or null on success.
        /// Compare against <see cref="PwfErrorCodes"/>.
        /// </summary>
        public string? ErrorCode { get { return GetString("error_code"); } }

        /// <summary>The human-readable message, safe to show to the end user.</summary>
        public string? Message { get { return GetString("message"); } }

        /// <summary>Reads a top-level string field, or null when absent.</summary>
        /// <param name="name">The JSON property name.</param>
        public string? GetString(string name)
        {
            JsonElement value;
            if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        /// <summary>Reads a top-level integer field.</summary>
        /// <param name="name">The JSON property name.</param>
        /// <param name="fallback">Returned when the field is missing or not a number.</param>
        public int GetInt32(string name, int fallback)
        {
            JsonElement value;
            int parsed;
            if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed)) return parsed;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out parsed)) return parsed;
            }
            return fallback;
        }

        /// <summary>Reads a top-level boolean field.</summary>
        /// <param name="name">The JSON property name.</param>
        /// <param name="fallback">Returned when the field is missing or not a boolean.</param>
        public bool GetBoolean(string name, bool fallback)
        {
            JsonElement value;
            if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out value))
            {
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
            }
            return fallback;
        }

        /// <summary>Gets a nested element, e.g. <c>"app"</c> or <c>"update"</c>.</summary>
        /// <param name="name">The JSON property name.</param>
        /// <param name="value">Receives the element when present.</param>
        public bool TryGetProperty(string name, out JsonElement value)
        {
            if (_root.ValueKind == JsonValueKind.Object) return _root.TryGetProperty(name, out value);
            value = default(JsonElement);
            return false;
        }

        /// <summary>
        /// Flattens a string-to-string object such as <c>texts</c> or <c>features</c>
        /// into a dictionary. Returns an empty dictionary when the field is absent.
        /// </summary>
        /// <param name="name">The JSON property name.</param>
        public IReadOnlyDictionary<string, string> GetStringMap(string name)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonElement obj;
            if (TryGetProperty(name, out obj) && obj.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in obj.EnumerateObject())
                {
                    map[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? (prop.Value.GetString() ?? string.Empty)
                        : prop.Value.ToString();
                }
            }
            return map;
        }

        /// <summary>Returns the message, or the error code, or the raw body — whichever exists.</summary>
        public override string ToString()
        {
            return Message ?? ErrorCode ?? RawJson;
        }
    }
}
