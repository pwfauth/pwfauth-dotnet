using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
namespace PWFAuth.Tests {
public class ServerAuthTests {
    [Fact] public void RealSignatureRejectsTampering() {
        var v=JsonDocument.Parse(File.ReadAllText("signed-reply.json")).RootElement;
        object[] args={v.GetProperty("nonce").GetString()!,v.GetProperty("method").GetString()!,v.GetProperty("path").GetString()!,Encoding.UTF8.GetBytes(v.GetProperty("request").GetString()!),v.GetProperty("status").GetInt32(),Encoding.UTF8.GetBytes(v.GetProperty("body").GetString()!),v.GetProperty("signature").GetString()!};
        var verify=typeof(PwfClient).Assembly.GetType("PWFAuth.ServerAuth")!.GetMethod("Verify",BindingFlags.NonPublic|BindingFlags.Static)!;
        verify.Invoke(null,args);
        object[] changed={new string('b',64),"GET","/api/auth/heartbeat.php",Encoding.UTF8.GetBytes("{}"),401,Encoding.UTF8.GetBytes("{}"),"AAAA"};
        for(int i=0;i<args.Length;i++) {var bad=(object[])args.Clone();bad[i]=changed[i];var ex=Assert.Throws<TargetInvocationException>(()=>verify.Invoke(null,bad));Assert.IsType<PwfSecurityException>(ex.InnerException);}
        foreach(var signature in new[]{"","!"}) {var bad=(object[])args.Clone();bad[6]=signature;Assert.IsType<PwfSecurityException>(Assert.Throws<TargetInvocationException>(()=>verify.Invoke(null,bad)).InnerException);}
    }
    [Theory]
    [InlineData("http://pwfauth.com")][InlineData("http://127.0.0.1:8080")][InlineData("https://evil.test")][InlineData("https://pwfauth.com.evil.test")][InlineData("https://pwfauth.com@evil.test")][InlineData("https://pwfauth.com:444")][InlineData("https://pwfauth.com/api")]
    public void RejectsOtherOrigins(string url) {Assert.Throws<PwfSecurityException>(()=>new PwfClient(new PwfClientOptions{AppSecret=new string('a',64),BaseUrl=url}));}
    [Fact] public async Task UnsignedReplyRejected() {
        using(var http=new System.Net.Http.HttpClient(new FakeHandler(r=>new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent(new CryptoEnvelope(new string('a',64)).Encrypt("{\"success\":true,\"session_id\":\"fake\"}"))})))
        using(var client=new PwfClient(new PwfClientOptions{AppSecret=new string('a',64)},http)) {await Assert.ThrowsAsync<PwfSecurityException>(()=>client.LoginAsync("anything"));Assert.False(client.IsSignedIn);}
    }
}}
