<p align="center"><a href="README.md">English</a> · <b>简体中文</b> · <a href="README.tr.md">Türkçe</a> · <a href="README.ar.md">العربية</a></p>
<p align="center">视频教程：<a href="https://www.youtube.com/@pwfauth">youtube.com/@pwfauth</a></p>

# PWF Auth for .NET

## 安装 1.7.0

1.7.0 新增了**用户变量**：程序可以为每个客户在服务器上保存一些值（`SetVariableAsync`、`GetVariableAsync`、`GetVariablesAsync`、`DeleteVariableAsync`），这些值也可以在后台中编辑。详见 [CHANGELOG.md](CHANGELOG.md)


PWF Auth 现在使用新的密钥为响应签名。PWFAuth 1.5.0 及更高版本内置并固定（pin）了这个公钥；**1.4.0 已无法与当前服务器通信**，请升级 SDK 并重新构建你的应用

```sh
dotnet add package PWFAuth --version 1.7.0
```

1.6.0 可以在屏蔽 pwfauth.com 域名的网络（例如阿联酋的 Etisalat）上正常工作：当某个请求完全收不到响应时，SDK 会改用 `https://pwfauth.net` 访问同一台服务器。1.5.2 在连接于收到任何响应之前就断开时（Windows 错误 10054）会自动重发一次请求。详见 [CHANGELOG.md](CHANGELOG.md)

## 服务器认证更新

所有响应，包括错误响应和时钟校正响应，都必须带有能被内置的 PWFAuth 公钥验证通过的有效 RSA-SHA256 签名。签名私钥只保存在服务器上，仅仅知道应用密钥（app secret）并不足以伪造这个签名。每个响应都通过一个全新的随机 nonce 与它对应的请求、方法、路径、HTTP 状态码以及完整的响应体字节绑定在一起（PWF-REPLY-V1）。查询字符串不属于 V1 签名路径的一部分，生产环境中由 HTTPS 保证它们在传输过程中的安全

SDK 只接受 `https://pwfauth.com`，其他任何源（origin）都会被拒绝。默认的传输层会校验 TLS 证书并拒绝重定向。签名缺失或无效时，SDK 会在 JSON 解析和信封（envelope）解密之前直接失败（fail closed）；代理或 CDN 返回的未签名错误一律视为安全错误。SDK 不会回退到旧的无签名协议。请先升级服务器，再升级客户端。发生安全错误或传输错误后，应用绝不能解锁任何功能

这项措施针对的是服务器模拟攻击，它无法阻止已经控制了客户端机器的攻击者直接修改应用本身。真正有价值的关键操作请放在服务器端执行，也不要把真实的应用密钥放进公开仓库


[PWF Auth](https://pwfauth.com) 官方客户端：许可证密钥、用户账户、硬件 ID（HWID）绑定、带服务器端远程停用（kill switch）的加密会话、免费试用、远程内容以及 OTA 更新检查

```bash
dotnet add package PWFAuth
```

目标框架为 **netstandard2.0**（支持 .NET Framework 4.6.2+，因此 WinForms/WPF 和 VB.NET 桌面应用同样能获得完整支持）和 **net8.0**

## 1.3.1 版本新特性

* **没有网络连接时也会抛出 `PwfHttpException`**：无论是离线、DNS、防火墙、代理还是 TLS 问题，调用都会抛出 `StatusCode` 为 0 的 `PwfHttpException`，网络错误放在 `InnerException` 中。1.3.0 会直接抛出原始的 `HttpRequestException`。现在 `LogoutAsync` 在这种情况下会按文档所述返回 `null`，不再抛出异常

## 1.3.0 版本新特性

* **可以用许可证密钥注册，并通过添加密钥为账户续期**：`RegisterAccountWithKeyAsync` 用一个密钥创建账户（账户获得该密钥的时长和设备数限制），`RedeemKeyAsync` 则把另一个密钥的时长加到已有账户上，即使账户已经过期也可以。详见下文的 *基于许可证密钥的账户*
* 新增 `PwfErrorCodes`：`KeyRequired`、`KeyAlreadyUsed`、`KeyInUse`、`KeyRedeemed`、`AlreadyLifetime`、`UsernameExists`

## 1.2.0 版本新特性

* **系统时钟不准会自动修正**：服务器会拒绝时间戳偏差超过五分钟的请求，现在拒绝时还会附带服务器自己的时间。客户端按这个差值调整自己的时间戳，然后重发一次请求，所以即使电脑的日期或时间不对，登录和心跳也能正常工作，再也不会收到“请检查你的系统时间”这类工单。如需关闭，设置 `PwfClientOptions.AutoCorrectClock = false` 即可

## 1.1.0 版本新特性

* **`ResetHardwareIdAsync`**：客户可以自己把许可证迁移到新电脑，立即生效（见下文的 *让用户自行把许可证迁移到新电脑*）。`RequestHardwareResetAsync` 已废弃：后台目前还不显示这类请求，所以根本没人能批准
* **拒绝明文的 “success” 响应**：真实服务器会对会话和内容端点上的每一个响应加密，所以声称成功的明文 JSON 响应只可能来自代理或伪造的服务器，现在这种情况会抛出 `PwfSecurityException`。明文的失败响应仍会作为失败结果正常返回
* **改系统时间绕不过远程停用**：心跳现在只把加密的响应算作有效应答，因此当时钟偏差超过五分钟时，服务器返回的明文拒绝不会再被忽略，而是以 `CLOCK_SKEW` 结束会话。明文的 HTTP 429 有单独的次数额度 `MaxRateLimitedBeats`（见 *为什么心跳很重要*）
* **`SessionEnded` 会在 UI 线程上触发**：在 UI 线程上调用 `StartHeartbeat()` 时（WinForms/WPF），事件处理程序可以直接操作控件。如需关闭，设置 `PwfClientOptions.RaiseEventsOnCapturedContext = false`
* 新增 `PwfErrorCodes`：`DeviceLimit`、`OpenAccessLimit`、`KeyNotActive`、`NoHwid`、`RateLimited`、`SelfResetDisabled`，以及客户端侧的 `ClockSkew`
* 每个请求都会通过 `User-Agent: PWFAuth-dotnet/1.1.0 (+https://pwfauth.com)` 标明自己的身份
* 你传入的 `HttpClient` 不会再被修改，请自行设置它的 `Timeout`

完整更新历史：[CHANGELOG.md](https://github.com/pwfauth/pwfauth-dotnet/blob/main/CHANGELOG.md)

## 快速开始

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

VB.NET：

```vb
Dim client As New PwfClient("your-64-char-app-secret")
AddHandler client.SessionEnded, Sub(s, e)
                                    MessageBox.Show(e.Message)
                                    Application.Exit()
                                End Sub

Dim login = Await client.LoginAsync("XXXXX-XXXXX-XXXXX-XXXXX")
If login.Success Then client.StartHeartbeat()
```

在窗体中，`StartHeartbeat()` 是在 UI 线程上调用的，所以 `SessionEnded` 也会在 UI 线程上触发，调用 `MessageBox` 是安全的，详见下文的 *WinForms：在 UI 线程上触发 SessionEnded*

## 为什么心跳很重要

`StartHeartbeat()` 不是可有可无的例行操作，它才是真正执行管控的地方

* 服务器会丢弃停止心跳的会话，所以一个从不发送心跳的客户端虽然不会失去什么，但也永远不会知道自己已被吊销
* 每次心跳都会重新检查许可证：封禁、暂停、过期、HWID 重置、删除和维护模式，都会在紧接着的下一次心跳时结束会话
* 如果连续 `MaxHeartbeatFailures` 次心跳（默认 3 次）都连不上服务器，客户端会以 `NETWORK_LOST` 自行结束会话。否则只要在防火墙里屏蔽许可证域名，应用就能永远运行下去
* 收到明文、未加密的 “success” 心跳应答，说明它并非来自许可证服务器，因此会被算作一次失败的心跳。也就是说，冒充服务器进行应答的代理同样无法让应用继续运行

### 改系统时间绕不过远程停用

每个请求都带有时间戳，服务器会拒绝偏差超过五分钟的请求，而且返回的是明文、未加密的错误，因为它无法验证这个请求。在 1.1.0 之前，心跳会直接忽略这些拒绝，所以登录后修改系统时钟，应用就能继续运行，并且再也收不到封禁。现在只有加密的响应才算作应答：连续 `MaxHeartbeatFailures` 次拒绝会以 `CLOCK_SKEW` 结束会话，对应的提示消息会请用户校正日期和时间（其他明文拒绝则以 `NETWORK_LOST` 结束）。明文的 `429 Too Many Requests` 响应有更大的额度 `MaxRateLimitedBeats`（默认 10 次），这样共用繁忙 IP 的用户不会在一分钟之内就被登出，但一个永远返回 429 的代理最终仍会让会话结束

自 1.2.0 起，客户端会先尝试修正时钟：服务器的拒绝响应里带有服务器时间，客户端按差值调整时间戳后重发一次心跳（`AutoCorrectClock`，默认开启）。封禁依然能够送达，因为校正后的心跳会得到真实的加密应答。只有在校正无效，或者设置了 `AutoCorrectClock = false` 时，会话才会以 `CLOCK_SKEW` 结束

## WinForms：在 UI 线程上触发 SessionEnded

`StartHeartbeat()` 会记住调用它时所在的 `SynchronizationContext`。在 UI 线程上调用它，例如在按钮的事件处理程序里紧跟在 `await client.LoginAsync(...)` 之后调用，`SessionEnded` 就会回到这个线程上触发，处理程序可以直接更新控件，不需要 `Invoke`/`BeginInvoke`（WPF 中也不需要 `Dispatcher`）

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

VB.NET：

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

只有事件会被封送（marshal）回 UI 线程，心跳本身从不在 UI 线程上运行。没有 UI 上下文时（控制台应用、服务、后台线程），事件和 1.0 一样在心跳自己的线程上触发。如果希望始终如此，在 `PwfClientOptions` 中设置 `RaiseEventsOnCapturedContext = false`

## 登录响应包含哪些内容

`LoginAsync` 成功时会带回完整的许可证状态，所以通常不需要再发一次请求，就能向用户展示他们拥有的授权

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `user.license_key` | string | 被激活的密钥 |
| `user.key_type` | string | `days`、`hours`、`lifetime` 等 |
| `user.duration` | number | 时长，单位由 `key_type` 决定 |
| `user.hwid` | string | 当前会话绑定的机器 |
| `user.activated_at` | string | UTC ISO 8601，首次激活时间 |
| `user.expires_at` | string \| **null** | UTC ISO 8601，**null 表示永久** |
| `user.days_remaining` | number \| **null** | 永久密钥为 **null** |
| `user.status` | string | `active` |
| `features` | object | 你在后台为每个密钥设置的功能开关 |
| `app.name` / `app.version` / `app.message` | string | 应用名称、当前版本和登录消息 |
| `texts` / `slides` | object / array | 远程字符串和公告轮播 |
| `session_id` | string | 客户端内部也会保存一份 |
| `heartbeat_interval` | number | 单位为秒，`StartHeartbeat()` 已自动遵循该值 |

永久密钥的 `expires_at` 和 `days_remaining` 都是 `null`，格式化之前一定要先检查，否则永久授权的客户看到的不是“永不过期”，而是程序崩溃

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

VB.NET，在 WinForms 标签上实现同样的效果：

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

客户端不会缓存这些数据：可以从你已经拿到的 `login` 响应中重新读取，也可以稍后调用 `CheckKeyAsync(key)` 获取最新状态，这不会占用设备名额

## 让用户自行把许可证迁移到新电脑

密钥会绑定到激活它的那台机器。客户换了电脑之后，在新电脑上调用 `LoginAsync` 会以 `HwidMismatch` 失败（如果是多设备密钥且所有设备名额都已占满，则返回 `DeviceLimit`）。`ResetHardwareIdAsync` 会当场解除密钥的绑定，不用提工单，也不用等你处理，下一次 `LoginAsync` 就会把密钥绑定到当前这台机器

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

重置被拒绝时，`reset.ErrorCode` 会说明原因：

| 代码 | 含义 |
| --- | --- |
| `RATE_LIMITED` | 该密钥最近刚重置过，消息中会说明还需等待几小时 |
| `SELF_RESET_DISABLED` | 你为该应用关闭了自助重置 |
| `KEY_NOT_ACTIVE` | 密钥已过期、被封禁或已暂停 |
| `NO_HWID` | 密钥没有绑定任何机器，无需重置 |
| `INVALID_KEY` | 该应用下不存在这个密钥 |

重置遵循应用的策略：你可以关闭自助重置，也可以设置两次重置之间的冷却时间（默认 12 小时）。重置成功时，`reset.GetString("next_reset_at")`（UTC，ISO 8601）会给出下次允许重置的时间。重置会解除该密钥所有设备的绑定并结束它的全部会话，因此旧电脑会在下一次心跳时被登出

退出登录并不等于重置：`LogoutAsync` 会结束会话，但密钥仍然绑定在这台机器上

## 基于许可证密钥的账户

客户购买密钥后用它创建账户，之后还可以往同一个账户里添加更多密钥来续期：只需记住一个用户名和密码，不必保管一堆密钥。在 App Settings > Account sign-up 中开启 **Sign-up needs a license key**，即可要求注册时必须提供密钥

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

在 App Settings 中，你可以选择密钥的时长是**叠加到剩余时间上**还是**从现在起重新计算**，以及账户是**保留自己的设备数限制**还是**改用密钥的设备数限制**。已兑换的密钥不能再单独登录（`LoginAsync` 会返回 `KEY_REDEEMED`），在后台封禁该密钥会把它的时长从账户中扣回

| 代码 | 含义 |
| --- | --- |
| `KEY_REQUIRED` | 该应用只能使用密钥创建账户 |
| `KEY_ALREADY_USED` | 该密钥已被添加到某个账户 |
| `KEY_IN_USE` | 该密钥已通过许可证登录激活，不能再添加到账户 |
| `INVALID_KEY` | 该应用下不存在这个密钥 |
| `ALREADY_LIFETIME` | 该账户已拥有永久访问权限 |
| `INVALID_CREDENTIALS` | 用户名或密码错误（计入登录锁定次数） |

## 其他功能

```csharp
var status  = await client.CheckKeyAsync(key);        // no session, no seat consumed
var info    = await client.GetAppInfoAsync();         // name, version, download URL, socials
var texts   = await client.GetTextsAsync();           // remote strings, per-key overrides
var slides  = await client.GetSlidesAsync();          // announcement slides
await client.SetVariableAsync("theme", "dark");        // a value kept on the server for this customer
var theme   = await client.GetVariableAsync("theme"); // null when never set; also GetVariablesAsync, DeleteVariableAsync
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

本客户端尚未封装的端点也可以直接调用：`PostEnvelopeAsync`、`GetEnvelopeAsync` 和 `PostPlainAsync` 都是公开方法，`CryptoEnvelope` 同样是公开的。和所有已封装的调用一样，这两个信封传输方法也会拒绝声称成功的明文响应

## 读取结果

`PwfResponse` 提供 `Success`、`ErrorCode` 和 `Message`，另外还有类型化的取值方法，以及用于读取特定端点字段的原始 `JsonElement`：

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

## 硬件 ID

`HardwareId.Get()` 在 Windows 上读取系统加密配置中的 MachineGuid，在 Linux 上读取 `/etc/machine-id`，在 macOS 上读取平台 UUID，都读取不到时回退为机器名。它不依赖 `wmic`（Windows 11 24H2 已移除该工具）。如果需要不同的绑定策略，可以自行覆盖：

```csharp
var client = new PwfClient(new PwfClientOptions
{
    AppSecret    = secret,
    HardwareId   = myOwnFingerprint,
    BaseUrl      = "https://pwfauth.com",
});
```

## 网络细节

所有没能拿到可用响应的失败都会抛出 `PwfHttpException`。`StatusCode` 为 0 表示完全没有收到响应（无连接、DNS、防火墙、代理、TLS，或者在 `Timeout` 内没有应答），网络错误位于 `InnerException` 中。其他任何值都是某个非 API JSON 响应的 HTTP 状态码（例如 CDN 错误页，或者基础 URL 填错了）

每个请求都带有 `User-Agent: PWFAuth-dotnet/1.3.1 (+https://pwfauth.com)`（版本号与包版本一致），方便在日志和防火墙中识别 SDK 流量。请求头只设置在每个请求上，从不设置在 `HttpClient` 上。通过 `new PwfClient(options, httpClient)` 传入的实例（来自 `IHttpClientFactory`，或者配置了代理）会原样使用：客户端从不修改它的请求头或 `Timeout`，也从不释放它。请自行设置它的 `Timeout`，`PwfClientOptions.Timeout`（默认 15 秒）只作用于客户端自己创建的 `HttpClient`

## 安全说明

应用密钥会随二进制文件一起分发，这是信封协议固有的特点，因为客户端需要用这个密钥进行加密。请把编译产物视为敏感内容：对发布版本进行混淆，并且不要把密钥放进公开的源代码仓库（在分发之前从开发者可控的配置中读取）。任何持有该密钥的人都能以你的应用的身份调用 API

在验证独立的服务器签名之后，SDK 还要求会话和内容端点的响应必须加密；只有服务器在能够验证请求之前就作出的拒绝（应用密钥错误、限流、时钟偏差过大）才会以明文 JSON 返回。因此，声称成功的明文响应一定不是来自 PWF Auth：`LoginAsync` 及其他加密调用会抛出 `PwfSecurityException` 而不会信任它，心跳则会把它当作服务器不可达来处理。请在捕获 `PwfHttpException` 的地方一并捕获它，并且绝不能因此解锁应用

## 许可

MIT © PWF Auth

自定义的 HttpClient 实例属于受信任的应用代码：请将其配置为拒绝重定向并校验 TLS 证书。SDK 无法检查注入的传输层内部的处理程序。独立的响应签名校验仍然照常生效
