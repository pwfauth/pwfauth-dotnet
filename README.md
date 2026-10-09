# PWF Auth for .NET

## Install this security release now

nuget.org publication of 1.4.0 is pending. Installing the registry's latest version
may still select the older release. Download [PWFAuth.1.4.0.nupkg](https://github.com/pwfauth/pwfauth-dotnet/releases/download/v1.4.0/PWFAuth.1.4.0.nupkg), place it in a local NuGet source,
and select version 1.4.0. Keep nuget.org as a source for its dependencies.
The updated C#/VB.NET example repositories already include the verified package
in `vendor/` and a `NuGet.Config` that restores it automatically.

Package SHA-256: `fbe308ec8ab47e9c9ce1464ea3487b91a3fbd64adae2a178c279a0d5f21ce90f`.


## Server authentication update

All responses, including errors and clock-correction responses, must carry a valid
RSA-SHA256 signature from the pinned PWFAuth public key. The private signing key
stays on the server. Knowing the application secret is not enough to forge this
signature. A fresh random nonce binds each response to its request, method, path,
HTTP status and exact body bytes (PWF-REPLY-V1). Query strings are not part of the
V1 signed path; production HTTPS protects them in transit.

The SDK accepts only `https://pwfauth.com`. Its default transport validates TLS
certificates and refuses redirects. Missing/invalid signatures fail closed before
JSON parsing or envelope decryption; unsigned proxy/CDN errors are security errors.
There is no fallback to the old unsigned protocol. Update the server before clients.
Applications must never unlock functionality after a security or transport error.

This addresses server emulation. It does not prevent an attacker who controls the
client machine from modifying the application itself. Keep authoritative valuable
operations on the server and keep real app secrets out of public repositories.


Official client for [PWF Auth](https://pwfauth.com) — license keys, user accounts,
hardware-ID binding, encrypted sessions with a server-side kill switch, free trials,
remote content, and OTA update checks.

```bash
dotnet add package PWFAuth
```

Targets **netstandard2.0** (works on .NET Framework 4.6.2+, so WinForms/WPF and VB.NET
desktop apps are first-class) and **net8.0**.

## What's new in 1.3.1

* **No connection is a `PwfHttpException`.** Offline, DNS, a firewall, a proxy or TLS: the
  call throws `PwfHttpException` with `StatusCode` 0 and the network error as `InnerException`.
  1.3.0 let the raw `HttpRequestException` through. `LogoutAsync` now returns `null` in that
  case, as documented, instead of throwing.

## What's new in 1.3.0

* **Sign up with a license key, and add keys to extend an account.** `RegisterAccountWithKeyAsync`
  creates an account from a key (it gets the key's time and device limit), and `RedeemKeyAsync`
  adds another key's time to an existing account, even after it has expired. See
  *Accounts that run on license keys* below.
* New `PwfErrorCodes`: `KeyRequired`, `KeyAlreadyUsed`, `KeyInUse`, `KeyRedeemed`,
  `AlreadyLifetime`, `UsernameExists`.

## What's new in 1.2.0

* **A wrong system clock repairs itself.** The server refuses a request whose timestamp is
  more than five minutes off, and now sends its own time with the refusal. The client shifts
  its timestamps by the difference and sends the request once more, so signing in and the
  heartbeat work on a PC whose date or time is wrong — no more "check your clock" support
  tickets. Opt out with `PwfClientOptions.AutoCorrectClock = false`.

## What's new in 1.1.0

* **`ResetHardwareIdAsync`** — customers move their license to a new PC themselves,
  instantly (see *Let users move their license to a new PC* below).
  `RequestHardwareResetAsync` is obsolete: the dashboard does not show those requests
  yet, so nobody could approve one.
* **Plain "success" replies are rejected.** The real server encrypts every reply on the
  session and content endpoints, so a plain JSON reply claiming success can only come from
  a proxy or a fake server. It now raises `PwfSecurityException`. Plain failure replies are
  still returned as failed responses.
* **The kill switch cannot be dodged by changing the clock.** The heartbeat now counts only
  encrypted replies as answers, so the plain refusals the server sends to a clock that is
  more than five minutes off end the session with `CLOCK_SKEW` instead of being ignored.
  Plain HTTP 429 gets its own budget, `MaxRateLimitedBeats` (see *Why the heartbeat matters*).
* **`SessionEnded` arrives on the UI thread** when `StartHeartbeat()` is called from it
  (WinForms/WPF), so handlers can touch controls directly. Opt out with
  `PwfClientOptions.RaiseEventsOnCapturedContext = false`.
* New `PwfErrorCodes`: `DeviceLimit`, `OpenAccessLimit`, `KeyNotActive`, `NoHwid`,
  `RateLimited`, `SelfResetDisabled`, and the client-side `ClockSkew`.
* Every request identifies itself with `User-Agent: PWFAuth-dotnet/1.1.0 (+https://pwfauth.com)`.
* An `HttpClient` you pass in is no longer modified — set its `Timeout` yourself.

Full history: [CHANGELOG.md](https://github.com/pwfauth/pwfauth-dotnet/blob/main/CHANGELOG.md).

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

In a form, that `StartHeartbeat()` runs on the UI thread, so `SessionEnded` is raised on
the UI thread too and the `MessageBox` is safe — see *WinForms: SessionEnded on the UI
thread* below.

## Why the heartbeat matters

`StartHeartbeat()` is not optional bookkeeping — it is the enforcement point.

* The server drops a session that stops beating, so a client that never beats loses
  nothing but also never learns it was revoked.
* Every beat re-checks the license: ban, pause, expiry, HWID reset, deletion and
  maintenance mode all end the session on the very next beat.
* If the server is unreachable for `MaxHeartbeatFailures` beats in a row (3 by default),
  the client ends the session itself with `NETWORK_LOST`. Without that, blocking the
  license domain in a firewall would keep the application running forever.
* A beat answered with a plain, unencrypted "success" did not come from the license
  server, so it counts as a failed beat — a proxy answering in the server's place cannot
  keep the application running either.

### The kill switch cannot be dodged by changing the clock

Every request is timestamped, and the server refuses one that is more than five minutes
off — with a plain, unencrypted error, because it cannot verify the request. Before 1.1.0
the heartbeat shrugged those refusals off, so moving the system clock after signing in kept
the application running and deaf to bans. Now only an encrypted reply counts as an answer:
`MaxHeartbeatFailures` refusals in a row end the session with `CLOCK_SKEW`, whose message
asks the user to correct the date and time (other plain refusals end it with
`NETWORK_LOST`). Plain `429 Too Many Requests` replies get a larger budget,
`MaxRateLimitedBeats` (10 by default), so users behind a busy shared IP are not signed out
within a minute — but a proxy answering 429 forever still ends the session.

Since 1.2.0 the client first tries to repair the clock: the server's refusal carries its own
time, the client shifts its timestamps by the difference and sends the beat once more
(`AutoCorrectClock`, on by default). Bans still arrive, because the corrected beat gets a
real, encrypted answer. A session ends with `CLOCK_SKEW` only when that does not help, or
with `AutoCorrectClock = false`.

## WinForms: SessionEnded on the UI thread

`StartHeartbeat()` remembers the `SynchronizationContext` it was called on. Call it from
the UI thread — for example right after `await client.LoginAsync(...)` in a button
handler — and `SessionEnded` is raised back on that thread, so the handler can update
controls directly, without `Invoke`/`BeginInvoke` (WPF: without the `Dispatcher`).

```csharp
using PWFAuth;

public partial class MainForm : Form
{
    private readonly PwfClient _client = new PwfClient("your-64-char-app-secret");

    public MainForm()
    {
        InitializeComponent();
        _client.SessionEnded += (s, e) => OnSessionEnded(e);
    }

    private async void btnSignIn_Click(object sender, EventArgs e)
    {
        btnSignIn.Enabled = false;
        var login = await _client.LoginAsync(txtKey.Text.Trim());
        btnSignIn.Enabled = true;

        if (!login.Success)
        {
            lblStatus.Text = login.Message;
            return;
        }

        panelApp.Enabled = true;
        lblStatus.Text = "Signed in";
        _client.StartHeartbeat();   // called on the UI thread → SessionEnded comes back to it
    }

    // Runs on the UI thread: touching controls here is safe.
    private void OnSessionEnded(SessionEndedEventArgs e)
    {
        panelApp.Enabled = false;
        lblStatus.Text = e.IsNetworkFailure
            ? "Lost the connection to the license server. Please sign in again."
            : e.Message;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _client.Dispose();
        base.OnFormClosed(e);
    }
}
```

VB.NET:

```vb
Private ReadOnly client As New PwfClient("your-64-char-app-secret")

Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    AddHandler client.SessionEnded, Sub(s, args)
                                        ' Already on the UI thread — no Invoke needed.
                                        panelApp.Enabled = False
                                        lblStatus.Text = args.Message
                                    End Sub
End Sub

Private Async Sub btnSignIn_Click(sender As Object, e As EventArgs) Handles btnSignIn.Click
    Dim login = Await client.LoginAsync(txtKey.Text.Trim())
    If Not login.Success Then
        lblStatus.Text = login.Message
        Return
    End If
    panelApp.Enabled = True
    client.StartHeartbeat()   ' UI thread → SessionEnded is raised on it
End Sub
```

Only the event is marshalled — the beats themselves never run on the UI thread. Where there
is no UI context (console apps, services, a background thread) the event is raised on the
heartbeat's own thread, as in 1.0. Set `RaiseEventsOnCapturedContext = false` in
`PwfClientOptions` to always get that behaviour.

## What the login reply contains

A successful `LoginAsync` carries the whole licence state, so you rarely need a second
call to show the user what they have.

| Field | Type | Notes |
| --- | --- | --- |
| `user.license_key` | string | The key that was activated |
| `user.key_type` | string | `days`, `hours`, `lifetime`, … |
| `user.duration` | number | Length in units of `key_type` |
| `user.hwid` | string | The machine this session is bound to |
| `user.activated_at` | string | UTC ISO 8601, first activation |
| `user.expires_at` | string \| **null** | UTC ISO 8601. **null means lifetime** |
| `user.days_remaining` | number \| **null** | **null** for lifetime keys |
| `user.status` | string | `active` |
| `features` | object | Per-key feature flags you set in the dashboard |
| `app.name` / `app.version` / `app.message` | string | Your app's name, current version, login message |
| `texts` / `slides` | object / array | Remote strings and announcement slides |
| `session_id` | string | Also held internally by the client |
| `heartbeat_interval` | number | Seconds; `StartHeartbeat()` already honours it |

`expires_at` and `days_remaining` are `null` on lifetime keys — always check before
formatting, or a lifetime customer sees a crash instead of "never expires".

```csharp
using System.Text.Json;

if (login.TryGetProperty("user", out JsonElement user))
{
    string? key    = user.GetProperty("license_key").GetString();
    string? status = user.GetProperty("status").GetString();

    // The ValueKind check is required, not defensive padding: on .NET Framework
    // TryGetDateTime *throws* on a JSON null instead of returning false, and
    // expires_at is null for every lifetime key.
    if (user.TryGetProperty("expires_at", out JsonElement exp)
        && exp.ValueKind == JsonValueKind.String
        && exp.TryGetDateTime(out DateTime expiresUtc))
    {
        int left = user.TryGetProperty("days_remaining", out JsonElement days)
                   && days.ValueKind == JsonValueKind.Number ? days.GetInt32() : 0;

        Console.WriteLine($"{key} — expires {expiresUtc:yyyy-MM-dd HH:mm} UTC, {left} day(s) left");
        Console.WriteLine($"Local time: {expiresUtc.ToLocalTime():g}");
    }
    else
    {
        Console.WriteLine($"{key} — lifetime licence ({status})");
    }
}

// Feature flags — whatever you defined for this key in the dashboard
foreach (var f in login.GetStringMap("features"))
    Console.WriteLine($"{f.Key} = {f.Value}");
```

VB.NET — the same thing for a WinForms label:

```vb
Imports System.Text.Json

Dim user As JsonElement
If login.TryGetProperty("user", user) Then
    Dim expEl As JsonElement, daysEl As JsonElement
    Dim expiresUtc As DateTime

    ' The ValueKind check is required: on .NET Framework TryGetDateTime throws
    ' on a JSON null rather than returning False, and lifetime keys send null.
    If user.TryGetProperty("expires_at", expEl) AndAlso
       expEl.ValueKind = JsonValueKind.String AndAlso
       expEl.TryGetDateTime(expiresUtc) Then
        Dim left As Integer = 0
        If user.TryGetProperty("days_remaining", daysEl) AndAlso
           daysEl.ValueKind = JsonValueKind.Number Then left = daysEl.GetInt32()

        lblLicense.Text = String.Format("Expires {0:yyyy-MM-dd} — {1} day(s) left",
                                        expiresUtc.ToLocalTime(), left)

        If left <= 7 Then lblLicense.ForeColor = Color.OrangeRed   ' nudge them to renew
    Else
        lblLicense.Text = "Lifetime licence"          ' no expiry to show
    End If
End If

' Gate a feature on a per-key flag
Dim features = login.GetStringMap("features")
btnProExport.Enabled = features.ContainsKey("pro_tier") AndAlso features("pro_tier") = "True"
```

Nothing here is cached by the client — re-read it from the `login` response you already
hold, or call `CheckKeyAsync(key)` later for a fresh read without consuming a seat.

## Let users move their license to a new PC

A key binds to the machine that activates it. When a customer replaces their PC,
`LoginAsync` on the new one fails with `HwidMismatch` (or `DeviceLimit` for a multi-device
key that is already on all its machines). `ResetHardwareIdAsync` unbinds the key on the
spot — no ticket, no waiting for you — and the next `LoginAsync` binds it to this machine.

```csharp
var login = await client.LoginAsync(key);

if (!login.Success &&
    (login.ErrorCode == PwfErrorCodes.HwidMismatch || login.ErrorCode == PwfErrorCodes.DeviceLimit))
{
    Console.WriteLine(login.Message);
    Console.Write("Move your license to this PC? [y/N] ");

    if (string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
    {
        var reset = await client.ResetHardwareIdAsync(key, "Moved to a new PC");
        Console.WriteLine(reset.Message);          // written for the end user, success or not

        if (reset.Success)
            login = await client.LoginAsync(key);  // binds the key to this machine
    }
}

if (!login.Success)
{
    Console.WriteLine(login.Message);
    return;
}

client.StartHeartbeat();
```

When the reset is refused, `reset.ErrorCode` says why:

| Code | Meaning |
| --- | --- |
| `RATE_LIMITED` | The key was reset recently; the message says how many hours to wait |
| `SELF_RESET_DISABLED` | You turned self-service resets off for this application |
| `KEY_NOT_ACTIVE` | The key is expired, banned or paused |
| `NO_HWID` | The key is not bound to any machine — nothing to reset |
| `INVALID_KEY` | No such key for this application |

Resets follow your application's policy: you can turn them off, and you choose the
cooldown between two resets (12 hours by default). On success,
`reset.GetString("next_reset_at")` (UTC, ISO 8601) says when the next one is allowed. A
reset unbinds every device of the key and ends all of its sessions, so the old PC is signed
out at its next heartbeat.

Logging out is not a reset: `LogoutAsync` ends the session, but the key stays bound to the
machine.

## Accounts that run on license keys

Customers buy a key, create an account with it, and later add more keys to the same account
to extend it: one username and password, no pile of keys to keep. Turn on **Sign-up needs a
license key** in App Settings > Account sign-up to make the key mandatory at sign-up.

```csharp
// Sign up: the account gets the key's time and device limit; the key is used up.
var signup = await client.RegisterAccountWithKeyAsync("alice", "s3cret-pass", key, "alice@example.com");
if (!signup.Success) { Console.WriteLine(signup.Message); return; }

// Later: add another key, with the password (works even if the account has expired) ...
var added = await client.RedeemKeyAsync("alice", "s3cret-pass", newKey);

// ... or from a program that is signed in with AccountLoginAsync.
var added2 = await client.RedeemKeyAsync(newKey);
Console.WriteLine(added.Message);                    // "Key added: +30 days. ..."
Console.WriteLine(added.GetInt32("days_remaining", 0));
```

In App Settings you choose whether a key's time goes **on top of the time left** or
**starts again from now**, and whether the account **keeps its own device limit** or
**takes the key's**. A used key cannot sign in on its own (`LoginAsync` answers
`KEY_REDEEMED`), and banning it in the dashboard takes its time back from the account.

| Code | Meaning |
| --- | --- |
| `KEY_REQUIRED` | This application only creates accounts with a key |
| `KEY_ALREADY_USED` | The key was already added to an account |
| `KEY_IN_USE` | The key was activated by a license login, so it cannot go to an account |
| `INVALID_KEY` | No such key for this application |
| `ALREADY_LIFETIME` | The account already has lifetime access |
| `INVALID_CREDENTIALS` | Wrong username or password (counts toward the sign-in lockout) |

## What else it does

```csharp
var status  = await client.CheckKeyAsync(key);        // no session, no seat consumed
var info    = await client.GetAppInfoAsync();         // name, version, download URL, socials
var texts   = await client.GetTextsAsync();           // remote strings, per-key overrides
var slides  = await client.GetSlidesAsync();          // announcement slides
var update  = await client.CheckUpdateAsync("1.4.2"); // OTA: version, sha256, download URL
var trial   = await client.CreateTrialAsync();        // free trial for this machine
var moved   = await client.ResetHardwareIdAsync(key); // free the key for a new PC
await client.LogoutAsync();                           // end the session; the key stays bound here

// User accounts (the username/password half of the platform)
await client.RegisterAccountAsync("alice", "s3cret", "alice@example.com");
await client.RegisterAccountWithKeyAsync("bob", "s3cret", key);   // account from a license key
await client.AccountLoginAsync("alice", "s3cret");
await client.RedeemKeyAsync("alice", "s3cret", newKey);           // add a key's time to the account
```

Endpoints this client does not wrap yet are still reachable — `PostEnvelopeAsync`,
`GetEnvelopeAsync` and `PostPlainAsync` are public, and `CryptoEnvelope` is too. The two
envelope transports refuse a plain reply that claims success, like every wrapped call.

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

## Network details

Every failure to get a usable reply is a `PwfHttpException`. `StatusCode` 0 means no reply at
all (no connection, DNS, a firewall, a proxy, TLS, or no answer within `Timeout`); the network
error is the `InnerException`. Any other value is the HTTP status of a reply that was not the
API's JSON (a CDN error page, a wrong base URL).

Every request carries `User-Agent: PWFAuth-dotnet/1.3.1 (+https://pwfauth.com)` — the
version follows the package — so SDK traffic is easy to tell apart in logs and firewalls.
Headers are set on each request, never on the `HttpClient`. One you pass to
`new PwfClient(options, httpClient)` (from `IHttpClientFactory`, or with a proxy
configured) is used exactly as it is: the client never changes its headers or its
`Timeout` and never disposes it. Set its `Timeout` yourself — `PwfClientOptions.Timeout`
(15 seconds by default) applies only to the `HttpClient` the client creates for itself.

## Security note

The app secret ships inside your binary — that is inherent to the envelope protocol,
which needs the key on the client to encrypt. Treat compiled output as sensitive:
obfuscate release builds, and keep the secret out of public source control (read it from
a developer-controlled configuration before distribution). Anyone holding the secret can
call the API as your application.

After verifying the independent server signature, the SDK also requires encryption on session and content endpoints; only refusals
that happen before it can verify the request (a bad secret, a rate limit, a clock too far
off) come back as plain JSON. A plain reply that claims success therefore did not come
from PWF Auth — `LoginAsync` and the other encrypted calls throw `PwfSecurityException`
rather than trust it, and the heartbeat treats it like an unreachable server. Catch it
where you catch `PwfHttpException`, and never unlock the application on it.

## License

MIT © PWF Auth

Custom HttpClient instances are trusted application code: configure them to reject redirects and validate TLS certificates. The SDK cannot inspect an injected transport's internal handler. Independent response signature verification still applies.
