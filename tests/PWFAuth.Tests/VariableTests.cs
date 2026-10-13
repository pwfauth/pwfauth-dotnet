using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PWFAuth.Tests
{
    /// <summary>
    /// User variables (1.7.0): values the program keeps on the server for the signed-in customer.
    /// POST /api/auth/var.php in the envelope, with the session id; the typed calls return the
    /// value and throw <see cref="PwfRefusedException"/> when the server refuses.
    /// </summary>
    public class VariableTests
    {
        private const string VarPath = "/api/auth/var.php";

        private static async Task<Rig> SignedInRig(Func<RecordedRequest, HttpResponseMessage> onVar)
        {
            var rig = new Rig(r => r.Uri.AbsolutePath == VarPath ? onVar(r) : FakeServer.Enveloped(FakeServer.LoginOk()));
            await rig.Client.LoginAsync(FakeServer.LicenseKey);
            return rig;
        }

        /// <summary>The one call to var.php (the sign-in before it is not counted).</summary>
        private static RecordedRequest VarRequest(Rig rig) => Assert.Single(rig.Handler.Requests, r => r.Uri.AbsolutePath == VarPath);

        private static JsonElement Body(RecordedRequest request)
        {
            using JsonDocument doc = JsonDocument.Parse(FakeServer.DecryptRequest(request));
            return doc.RootElement.Clone();
        }

        [Fact]
        public async Task SetVariable_PostsSessionActionNameValue_InTheEnvelope()
        {
            using Rig rig = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"name\":\"theme\"}"));

            await rig.Client.SetVariableAsync("theme", "dark");

            RecordedRequest request = VarRequest(rig);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(FakeServer.BaseUrl + VarPath), request.Uri);
            Assert.True(CryptoEnvelope.LooksLikeEnvelope(request.Body!));
            JsonElement body = Body(request);
            Assert.Equal("sess-123", body.GetProperty("session_id").GetString());
            Assert.Equal("set", body.GetProperty("action").GetString());
            Assert.Equal("theme", body.GetProperty("name").GetString());
            Assert.Equal("dark", body.GetProperty("value").GetString());
            Assert.Equal(4, body.EnumerateObject().Count());
        }

        [Fact]
        public async Task GetVariable_ReturnsTheValue_OrNullWhenNeverSet()
        {
            using Rig rig = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"name\":\"theme\",\"value\":\"dark\",\"exists\":true}"));
            Assert.Equal("dark", await rig.Client.GetVariableAsync("theme"));
            JsonElement body = Body(VarRequest(rig));
            Assert.Equal("get", body.GetProperty("action").GetString());
            Assert.False(body.TryGetProperty("value", out _));

            using Rig none = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"name\":\"theme\",\"value\":null,\"exists\":false}"));
            Assert.Null(await none.Client.GetVariableAsync("theme"));
        }

        [Fact]
        public async Task GetVariables_ListsThem_AndDeleteReportsWhetherOneWasThere()
        {
            using Rig rig = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"vars\":{\"a\":\"1\",\"b\":\"two\"}}"));
            IReadOnlyDictionary<string, string> all = await rig.Client.GetVariablesAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal("1", all["a"]);
            Assert.Equal("two", all["b"]);
            JsonElement body = Body(VarRequest(rig));
            Assert.Equal("list", body.GetProperty("action").GetString());
            Assert.False(body.TryGetProperty("name", out _));

            using Rig empty = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"vars\":{}}"));
            Assert.Empty(await empty.Client.GetVariablesAsync());

            using Rig del = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true,\"name\":\"a\",\"deleted\":true}"));
            Assert.True(await del.Client.DeleteVariableAsync("a"));
            Assert.Equal("delete", Body(VarRequest(del)).GetProperty("action").GetString());
        }

        [Fact]
        public async Task ARefusal_ThrowsPwfRefusedException_WithTheServersCodeAndWords()
        {
            using Rig rig = await SignedInRig(_ => FakeServer.Enveloped(
                "{\"success\":false,\"error_code\":\"TOO_MANY_VARS\",\"message\":\"A customer can have at most 50 variables. Delete one first.\"}"));

            PwfRefusedException ex = await Assert.ThrowsAsync<PwfRefusedException>(() => rig.Client.SetVariableAsync("x", "y"));

            Assert.Equal("TOO_MANY_VARS", ex.ErrorCode);
            Assert.Contains("50 variables", ex.Message);
            Assert.False(ex.Response.Success);
        }

        [Fact]
        public async Task WithoutASession_OrWithABadArgument_ItNeverCallsTheServer()
        {
            using var rig = new Rig(_ => FakeServer.Enveloped("{\"success\":true}"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Client.GetVariableAsync("theme"));
            Assert.Empty(rig.Handler.Requests);

            using Rig signedIn = await SignedInRig(_ => FakeServer.Enveloped("{\"success\":true}"));
            await Assert.ThrowsAsync<ArgumentException>(() => signedIn.Client.GetVariableAsync(""));
            await Assert.ThrowsAsync<ArgumentNullException>(() => signedIn.Client.SetVariableAsync("theme", null!));
            Assert.DoesNotContain(signedIn.Handler.Requests, r => r.Uri.AbsolutePath == VarPath);
        }
    }
}
