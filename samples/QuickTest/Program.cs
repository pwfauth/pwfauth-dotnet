using System;
using System.Threading;
using System.Threading.Tasks;
using PWFAuth;

internal static class Program
{
    private static int _pass, _fail;
    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  ok  " + name); }
        else    { _fail++; Console.WriteLine("FAIL  " + name + (detail.Length > 0 ? "  -- " + detail : "")); }
    }

    private static async Task<int> Main()
    {
        const string Secret = "5473618231295399bfe82d13f99e2aaf3f5538635293cd91cc546ff96e908f6b";
        const string Key    = "GBB9A-46YPY-LV9FY-668HT";

        Console.WriteLine("HardwareId.Get() = " + HardwareId.Get());
        Check("HWID is not the bare machine name fallback",
              HardwareId.Get() != Environment.MachineName, HardwareId.Get());

        using var client = new PwfClient(Secret);

        var ended = new TaskCompletionSource<SessionEndedEventArgs>();
        client.SessionEnded += (s, e) => ended.TrySetResult(e);

        var login = await client.LoginAsync(Key);
        Check("LoginAsync", login.Success, login.ToString());
        Check("SessionId populated", client.IsSignedIn);
        Check("HeartbeatInterval from server", client.HeartbeatIntervalSeconds > 0, client.HeartbeatIntervalSeconds.ToString());

        var hb = await client.HeartbeatAsync();
        Check("HeartbeatAsync", hb.Success, hb.ToString());

        var status = await client.CheckKeyAsync(Key);
        Check("CheckKeyAsync", status.Success, status.ToString());

        var info = await client.GetAppInfoAsync();
        Check("GetAppInfoAsync (envelope-GET)", info.Success, info.ToString());
        Check("app name round-trips",
              info.TryGetProperty("app", out var app) && app.GetProperty("name").GetString() == "Claude NuGet E2E");

        var texts = await client.GetTextsAsync();
        Check("GetTextsAsync (Bearer)", texts.Success, texts.ToString());
        var map = texts.GetStringMap("texts");
        Check("GetStringMap resolves the text",
              map.ContainsKey("welcome_message") && map["welcome_message"] == "Welcome from the NuGet package",
              string.Join(",", map.Keys));

        var upd = await client.CheckUpdateAsync("1.0.0");
        Check("CheckUpdateAsync", upd.Success && !upd.GetBoolean("update_available", true), upd.ToString());

        var trial = await client.CreateTrialAsync();
        Check("CreateTrialAsync (plain-post)", trial.ErrorCode != null || trial.Success, trial.ToString());

        var reset = await client.RequestHardwareResetAsync(Key, "NuGet package test");
        Check("RequestHardwareResetAsync (plain-post)", reset.Success, reset.ToString());

        // Readable failure instead of a raw JSON exception
        try
        {
            using var bad = new PwfClient(new PwfClientOptions { AppSecret = Secret, BaseUrl = "https://pwfauth.com/nope" });
            await bad.CheckKeyAsync(Key);
            Check("PwfHttpException on a wrong base URL", false, "no exception");
        }
        catch (PwfHttpException ex)
        {
            Check("PwfHttpException on a wrong base URL", ex.StatusCode == 404, "status " + ex.StatusCode);
        }

        // Wrong secret must be a crypto error, not a crash
        try
        {
            using var wrong = new PwfClient(new string('a', 64));
            await wrong.GetAppInfoAsync();
            Check("wrong secret rejected", false, "no exception");
        }
        catch (PwfException ex)
        {
            Check("wrong secret rejected", true, ex.GetType().Name);
        }

        // Kill switch: unreachable server ends the session locally
        using (var offline = new PwfClient(new PwfClientOptions { AppSecret = Secret, MaxHeartbeatFailures = 2, Timeout = TimeSpan.FromSeconds(2) }))
        {
            var ev = new TaskCompletionSource<SessionEndedEventArgs>();
            offline.SessionEnded += (s, e) => ev.TrySetResult(e);
            var l2 = await offline.LoginAsync(Key);
            if (l2.Success)
            {
                typeof(PwfClient).GetProperty("HeartbeatIntervalSeconds")!.SetValue(offline, 1);
                var field = typeof(PwfClient).GetField("_baseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                field.SetValue(offline, "https://127.0.0.1:9");   // black hole
                offline.StartHeartbeat();
                var done = await Task.WhenAny(ev.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                if (done == ev.Task)
                    Check("unreachable server ends the session (NETWORK_LOST)",
                          ev.Task.Result.ErrorCode == PwfErrorCodes.NetworkLost && !offline.IsSignedIn,
                          ev.Task.Result.ErrorCode);
                else
                    Check("unreachable server ends the session (NETWORK_LOST)", false, "timed out — loop never gave up");
            }
            else Check("offline scenario login", false, l2.ToString());
        }

        var logout = await client.LogoutAsync();
        Check("LogoutAsync", logout != null && logout.Success);
        Check("signed out after logout", !client.IsSignedIn);

        Console.WriteLine();
        Console.WriteLine($"SUMMARY: {_pass} passed, {_fail} failed");
        return _fail > 0 ? 1 : 0;
    }
}
