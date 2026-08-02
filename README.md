# PWF Auth for .NET

Official client for [PWF Auth](https://pwfauth.com) — license keys, user accounts,
hardware-ID binding, encrypted sessions with a server-side kill switch, free trials,
remote content, and OTA update checks.

```bash
dotnet add package PWFAuth
```

Targets **netstandard2.0** (works on .NET Framework 4.6.2+, so WinForms/WPF and VB.NET
desktop apps are first-class) and **net8.0**.

## Quick start

```csharp
using PWFAuth;

var client = new PwfClient("your-64-char-app-secret");

// The moment an admin bans, pauses, expires, resets or revokes the key — or the
// server becomes unreachable — this fires. Sign the user out here.
client.SessionEnded += (sender, e) =>
{
    Console.WriteLine($"Session ended: {e.ErrorCode} — {e.Message}");
    Environment.Exit(0);
};

var login = await client.LoginAsync("XXXXX-XXXXX-XXXXX-XXXXX");
if (!login.Success)
{
    Console.WriteLine(login.Message);   // safe to show the user
    return;
}

client.StartHeartbeat();   // keeps the session alive AND enforces the kill switch
```

VB.NET:

```vb
Dim client As New PwfClient("your-64-char-app-secret")
AddHandler client.SessionEnded, Sub(s, e)
                                    MessageBox.Show(e.Message)
                                    Application.Exit()
                                End Sub

Dim login = Await client.LoginAsync("XXXXX-XXXXX-XXXXX-XXXXX")
If login.Success Then client.StartHeartbeat()
```

## Why the heartbeat matters

`StartHeartbeat()` is not optional bookkeeping — it is the enforcement point.

* The server drops a session that stops beating, so a client that never beats loses
  nothing but also never learns it was revoked.
* Every beat re-checks the license: ban, pause, expiry, HWID reset, deletion and
  maintenance mode all end the session on the very next beat.
* If the server is unreachable for `MaxHeartbeatFailures` beats in a row (3 by default),
  the client ends the session itself with `NETWORK_LOST`. Without that, blocking the
  license domain in a firewall would keep the application running forever.

## What else it does

```csharp
var status  = await client.CheckKeyAsync(key);        // no session, no seat consumed
var info    = await client.GetAppInfoAsync();         // name, version, download URL, socials
var texts   = await client.GetTextsAsync();           // remote strings, per-key overrides
var slides  = await client.GetSlidesAsync();          // announcement slides
var update  = await client.CheckUpdateAsync("1.4.2"); // OTA: version, sha256, download URL
var trial   = await client.CreateTrialAsync();        // free trial for this machine
await client.RequestHardwareResetAsync(key, "New laptop");

// User accounts (the username/password half of the platform)
await client.RegisterAccountAsync("alice", "s3cret", "alice@example.com");
await client.AccountLoginAsync("alice", "s3cret");
```

Endpoints this client does not wrap yet are still reachable — `PostEnvelopeAsync`,
`GetEnvelopeAsync` and `PostPlainAsync` are public, and `CryptoEnvelope` is too.

## Reading results

`PwfResponse` exposes `Success`, `ErrorCode` and `Message`, plus typed getters and the
raw `JsonElement` for endpoint-specific fields:

```csharp
if (!login.Success && login.ErrorCode == PwfErrorCodes.HwidMismatch)
    ShowHardwareResetDialog();

var welcome = (await client.GetTextsAsync()).GetStringMap("texts")["welcome_message"];

var check = await client.CheckUpdateAsync("1.4.2");
if (check.GetBoolean("update_available", false) &&
    check.TryGetProperty("update", out var upd))
{
    Console.WriteLine(upd.GetProperty("version").GetString());
}
```

## Hardware ID

`HardwareId.Get()` reads the Windows cryptography MachineGuid, `/etc/machine-id` on
Linux, or the platform UUID on macOS, and falls back to the machine name. It does not
use `wmic`, which was removed in Windows 11 24H2. Override it when you need a different
binding policy:

```csharp
var client = new PwfClient(new PwfClientOptions
{
    AppSecret    = secret,
    HardwareId   = myOwnFingerprint,
    BaseUrl      = "https://pwfauth.com",
});
```

## Security note

The app secret ships inside your binary — that is inherent to the envelope protocol,
which needs the key on the client to encrypt. Treat compiled output as sensitive:
obfuscate release builds, and keep the secret out of public source control (read it from
an environment variable or an encrypted config at startup). Anyone holding the secret can
call the API as your application.

## License

MIT © PWF Auth
