<p align="center"><a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.tr.md">Türkçe</a> · <b>العربية</b></p>

# مكتبة PWF Auth لمنصة .NET

## تثبيت الإصدار 1.7.0

يضيف الإصدار 1.7.0 **متغيرات المستخدم**: قيم يحفظها برنامجك على الخادم لكل عميل (`SetVariableAsync`، `GetVariableAsync`، `GetVariablesAsync`، `DeleteVariableAsync`)، ويمكن تعديلها من لوحة التحكم. راجع [CHANGELOG.md](CHANGELOG.md)


يوقّع PWF Auth ردوده بمفتاح جديد، والإصدارات 1.5.0 وما بعدها من PWFAuth تحمل هذا المفتاح مثبّتاً داخلها؛ أما **الإصدار 1.4.0 فلا يستطيع التواصل مع الخادم الحالي**، لذا حدّث المكتبة وأعد بناء تطبيقك

```sh
dotnet add package PWFAuth --version 1.7.0
```

يعمل الإصدار 1.6.0 على الشبكات التي تحجب اسم النطاق pwfauth.com (مثل شبكة اتصالات Etisalat في الإمارات): عندما لا يتلقى الطلب أي رد إطلاقاً، يُرسَل إلى الخادم نفسه عبر `https://pwfauth.net`. ويعيد الإصدار 1.5.2 إرسال الطلب مرة واحدة عندما ينقطع الاتصال قبل وصول أي رد (خطأ Windows رقم 10054). راجع [CHANGELOG.md](CHANGELOG.md)

## تحديث مصادقة الخادم

يجب أن تحمل جميع الردود، بما فيها ردود الأخطاء وردود تصحيح الساعة، توقيع RSA-SHA256 صالحاً صادراً عن المفتاح العام لـ PWFAuth المثبّت في المكتبة. يبقى مفتاح التوقيع الخاص على الخادم، ومعرفة سر التطبيق لا تكفي لتزوير هذا التوقيع. يربط رقم عشوائي جديد (nonce) كل رد بطلبه، وبطريقة HTTP والمسار وحالة HTTP وبايتات المحتوى الدقيقة (PWF-REPLY-V1). سلاسل الاستعلام (query strings) ليست جزءاً من المسار الموقَّع في V1، ويحميها اتصال HTTPS في بيئة الإنتاج أثناء النقل

لا تقبل المكتبة إلا `https://pwfauth.com`، وترفض أي أصل (origin) آخر. يتحقق النقل الافتراضي فيها من شهادات TLS ويرفض عمليات إعادة التوجيه. يُرفض الرد الذي يفتقد التوقيع أو يحمل توقيعاً غير صالح قبل تحليل JSON أو فك تشفير الغلاف (envelope)، وتُعَد أخطاء الوكيل (proxy) أو شبكة CDN غير الموقّعة أخطاء أمنية. لا يوجد رجوع إلى البروتوكول القديم غير الموقّع. حدّث الخادم قبل العملاء. ويجب ألا تفتح التطبيقات أي ميزة بعد خطأ أمني أو خطأ في النقل

يعالج هذا انتحال الخادم، لكنه لا يمنع مهاجماً يتحكم بجهاز العميل من تعديل التطبيق نفسه. أبقِ العمليات المهمة التي يجب أن يحسمها الخادم على الخادم، وأبقِ أسرار التطبيقات الحقيقية خارج المستودعات العامة


المكتبة الرسمية للعميل في [PWF Auth](https://pwfauth.com): مفاتيح الترخيص، وحسابات المستخدمين، وربط المفتاح بمعرّف الجهاز (HWID)، وجلسات مشفّرة مع الإيقاف عن بُعد من جهة الخادم، والنسخ التجريبية المجانية، والمحتوى البعيد، والتحقق من التحديثات عبر الإنترنت (OTA)

```bash
dotnet add package PWFAuth
```

تستهدف المكتبة **netstandard2.0** (وتعمل على .NET Framework 4.6.2 وما بعده، لذا تحظى تطبيقات سطح المكتب المبنية بـ WinForms وWPF وVB.NET بدعم كامل) و**net8.0**

## الجديد في الإصدار 1.3.1

* **غياب الاتصال أصبح `PwfHttpException`.** عند انقطاع الإنترنت، أو فشل DNS، أو الحجب من جدار الحماية أو الوكيل (proxy)، أو خطأ TLS، يرمي الاستدعاء `PwfHttpException` بقيمة `StatusCode` تساوي 0، ويكون خطأ الشبكة في `InnerException`. كان الإصدار 1.3.0 يمرّر `HttpRequestException` الخام كما هو. وأصبحت `LogoutAsync` تعيد `null` في هذه الحالة كما هو موثّق، بدلاً من رمي استثناء

## الجديد في الإصدار 1.3.0

* **إنشاء حساب بمفتاح ترخيص، وإضافة مفاتيح لتمديد الحساب.** تنشئ `RegisterAccountWithKeyAsync` حساباً من مفتاح (يأخذ الحساب مدة المفتاح وحد أجهزته)، وتضيف `RedeemKeyAsync` مدة مفتاح آخر إلى حساب موجود، حتى بعد انتهاء صلاحيته. راجع *الحسابات التي تعمل بمفاتيح الترخيص* أدناه
* رموز جديدة في `PwfErrorCodes`: `KeyRequired`، `KeyAlreadyUsed`، `KeyInUse`، `KeyRedeemed`، `AlreadyLifetime`، `UsernameExists`

## الجديد في الإصدار 1.2.0

* **ساعة النظام الخاطئة تُصلَح تلقائياً.** يرفض الخادم أي طلب يختلف طابعه الزمني عن وقته بأكثر من خمس دقائق، وأصبح يرسل وقته مع الرفض. يزيح العميل طوابعه الزمنية بمقدار الفرق ويعيد إرسال الطلب مرة واحدة، فيعمل تسجيل الدخول والتحقق الدوري (heartbeat) على الجهاز الذي تاريخه أو وقته غير صحيح، ولا مزيد من تذاكر الدعم التي تنتهي بعبارة «تحقق من ساعتك». لإيقاف ذلك اضبط `PwfClientOptions.AutoCorrectClock = false`

## الجديد في الإصدار 1.1.0

* **الدالة `ResetHardwareIdAsync`**: ينقل العملاء ترخيصهم إلى جهاز جديد بأنفسهم وفوراً (راجع *اسمح للمستخدمين بنقل ترخيصهم إلى جهاز جديد* أدناه). أصبحت `RequestHardwareResetAsync` قديمة (obsolete): لوحة التحكم لا تعرض هذه الطلبات بعد، فلم يكن بوسع أحد الموافقة على أي منها
* **رفض ردود «النجاح» غير المشفّرة.** يشفّر الخادم الحقيقي كل رد على نقاط نهاية الجلسة والمحتوى، لذا فإن رد JSON غير مشفّر يدّعي النجاح لا يمكن أن يأتي إلا من وكيل (proxy) أو خادم مزيّف. أصبح هذا الرد يرمي `PwfSecurityException`. أما ردود الفشل غير المشفّرة فما زالت تُعاد كردود فاشلة
* **لا يمكن التهرب من الإيقاف عن بُعد بتغيير الساعة.** أصبح التحقق الدوري لا يعدّ رداً إلا الردود المشفّرة، فالرفض غير المشفّر الذي يرسله الخادم عندما تبتعد الساعة بأكثر من خمس دقائق ينهي الجلسة برمز `CLOCK_SKEW` بدلاً من تجاهله. ولردود HTTP 429 غير المشفّرة رصيد خاص بها هو `MaxRateLimitedBeats` (راجع *لماذا التحقق الدوري مهم*)
* **يصل الحدث `SessionEnded` على مؤشر ترابط واجهة المستخدم** عند استدعاء `StartHeartbeat()` منه (WinForms/WPF)، فيستطيع المعالج التعامل مع عناصر التحكم مباشرة. لإيقاف ذلك اضبط `PwfClientOptions.RaiseEventsOnCapturedContext = false`
* رموز جديدة في `PwfErrorCodes`: `DeviceLimit`، `OpenAccessLimit`، `KeyNotActive`، `NoHwid`، `RateLimited`، `SelfResetDisabled`، والرمز `ClockSkew` الذي يولّده العميل نفسه
* يعرّف كل طلب نفسه بالترويسة `User-Agent: PWFAuth-dotnet/1.1.0 (+https://pwfauth.com)`
* لم يعد `HttpClient` الذي تمرّره يُعدَّل: اضبط قيمة `Timeout` فيه بنفسك

السجل الكامل: [CHANGELOG.md](https://github.com/pwfauth/pwfauth-dotnet/blob/main/CHANGELOG.md)

## البدء السريع

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

بلغة VB.NET:

```vb
Dim client As New PwfClient("your-64-char-app-secret")
AddHandler client.SessionEnded, Sub(s, e)
                                    MessageBox.Show(e.Message)
                                    Application.Exit()
                                End Sub

Dim login = Await client.LoginAsync("XXXXX-XXXXX-XXXXX-XXXXX")
If login.Success Then client.StartHeartbeat()
```

في النموذج (Form) يُستدعى `StartHeartbeat()` على مؤشر ترابط واجهة المستخدم، لذا يُطلَق `SessionEnded` على مؤشر ترابط واجهة المستخدم أيضاً ويكون استخدام `MessageBox` آمناً. راجع *تطبيقات WinForms: الحدث SessionEnded على مؤشر ترابط واجهة المستخدم* أدناه

## لماذا التحقق الدوري مهم

ليست `StartHeartbeat()` خطوة تنظيمية اختيارية، بل هي النقطة التي يُفرَض فيها الترخيص فعلياً

* يُسقط الخادم الجلسة التي تتوقف نبضاتها، لذا فإن العميل الذي لا يرسل أي نبضة لا يخسر شيئاً، لكنه أيضاً لا يعلم أبداً أن ترخيصه أُلغي
* تعيد كل نبضة فحص الترخيص: الحظر والإيقاف المؤقت وانتهاء الصلاحية وإعادة تعيين معرّف الجهاز والحذف ووضع الصيانة، كلها تنهي الجلسة في النبضة التالية مباشرة
* إذا تعذّر الوصول إلى الخادم خلال `MaxHeartbeatFailures` نبضات متتالية (3 افتراضياً)، ينهي العميل الجلسة بنفسه برمز `NETWORK_LOST`. ولولا ذلك لكان حجب نطاق الترخيص في جدار الحماية كافياً لإبقاء التطبيق يعمل إلى الأبد
* النبضة التي تتلقى رد «نجاح» غير مشفّر لم يأتِ ردها من خادم الترخيص، لذا تُحسب نبضة فاشلة، ولا يستطيع وكيل (proxy) يجيب بدلاً من الخادم أن يُبقي التطبيق يعمل هو الآخر

### لا يمكن التهرب من الإيقاف عن بُعد بتغيير الساعة

يحمل كل طلب طابعاً زمنياً، ويرفض الخادم الطلب الذي يبتعد بأكثر من خمس دقائق برسالة خطأ غير مشفّرة، لأنه لا يستطيع التحقق من الطلب. قبل الإصدار 1.1.0 كان التحقق الدوري يتجاهل حالات الرفض هذه، فكان تغيير ساعة النظام بعد تسجيل الدخول يُبقي التطبيق يعمل دون أن يصله أي حظر. أما الآن فلا يُعدّ رداً إلا الرد المشفّر: إذا تكرر الرفض `MaxHeartbeatFailures` مرات متتالية انتهت الجلسة برمز `CLOCK_SKEW`، وتطلب رسالته من المستخدم تصحيح التاريخ والوقت (وأي رفض آخر غير مشفّر ينهيها برمز `NETWORK_LOST`). أما ردود `429 Too Many Requests` غير المشفّرة فلها رصيد أكبر هو `MaxRateLimitedBeats` (10 افتراضياً)، حتى لا يُسجَّل خروج المستخدمين الذين يتشاركون عنوان IP مزدحماً خلال دقيقة واحدة، لكن الوكيل الذي يرد بـ 429 إلى ما لا نهاية ينهي الجلسة رغم ذلك

منذ الإصدار 1.2.0 يحاول العميل أولاً إصلاح الساعة: يحمل رفض الخادم وقته، فيزيح العميل طوابعه الزمنية بمقدار الفرق ويعيد إرسال النبضة مرة واحدة (`AutoCorrectClock`، مفعّل افتراضياً). وتبقى أوامر الحظر تصل، لأن النبضة المصحّحة تتلقى رداً حقيقياً مشفّراً. ولا تنتهي الجلسة برمز `CLOCK_SKEW` إلا إذا لم يُجدِ ذلك نفعاً، أو عند ضبط `AutoCorrectClock = false`

## تطبيقات WinForms: الحدث SessionEnded على مؤشر ترابط واجهة المستخدم

تتذكر `StartHeartbeat()` سياق `SynchronizationContext` الذي استُدعيت عليه. استدعِها من مؤشر ترابط واجهة المستخدم، مثلاً بعد `await client.LoginAsync(...)` مباشرة داخل معالج نقر زر، وسيُطلَق `SessionEnded` على مؤشر الترابط نفسه، فيستطيع المعالج تحديث عناصر التحكم مباشرة دون `Invoke`/`BeginInvoke` (وفي WPF دون `Dispatcher`)

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

بلغة VB.NET:

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

يُنقل الحدث وحده إلى مؤشر ترابط واجهة المستخدم، أما النبضات نفسها فلا تعمل عليه أبداً. وحين لا يوجد سياق واجهة مستخدم (تطبيقات الكونسول، والخدمات، ومؤشرات الترابط في الخلفية) يُطلَق الحدث على مؤشر الترابط الخاص بالتحقق الدوري، كما في الإصدار 1.0. اضبط `RaiseEventsOnCapturedContext = false` في `PwfClientOptions` لتحصل على هذا السلوك دائماً

## محتوى رد تسجيل الدخول

يحمل رد `LoginAsync` الناجح حالة الترخيص كاملة، لذا نادراً ما تحتاج إلى استدعاء ثانٍ لتعرض للمستخدم ما لديه

| الحقل | النوع | ملاحظات |
| --- | --- | --- |
| `user.license_key` | string | المفتاح الذي جرى تفعيله |
| `user.key_type` | string | `days`، `hours`، `lifetime`، … |
| `user.duration` | number | المدة بوحدات `key_type` |
| `user.hwid` | string | الجهاز الذي ترتبط به هذه الجلسة |
| `user.activated_at` | string | بتوقيت UTC وصيغة ISO 8601، وقت التفعيل الأول |
| `user.expires_at` | string \| **null** | بتوقيت UTC وصيغة ISO 8601. **القيمة null تعني مدى الحياة** |
| `user.days_remaining` | number \| **null** | القيمة **null** لمفاتيح مدى الحياة |
| `user.status` | string | `active` |
| `features` | object | مفاتيح الميزات (feature flags) لكل مفتاح ترخيص، تضبطها في لوحة التحكم |
| `app.name` / `app.version` / `app.message` | string | اسم تطبيقك وإصداره الحالي ورسالة تسجيل الدخول |
| `texts` / `slides` | object / array | نصوص بعيدة وشرائح إعلانات |
| `session_id` | string | يحتفظ به العميل داخلياً أيضاً |
| `heartbeat_interval` | number | بالثواني، وتلتزم به `StartHeartbeat()` تلقائياً |

تكون قيمة `expires_at` و`days_remaining` هي `null` في مفاتيح مدى الحياة، فتحقق منهما دائماً قبل التنسيق، وإلا رأى صاحب مفتاح مدى الحياة انهياراً في التطبيق بدلاً من عبارة «لا ينتهي أبداً»

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

وهذا المثال نفسه بلغة VB.NET لعنصر Label في WinForms:

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

لا يخزّن العميل شيئاً من هذا مؤقتاً: أعد قراءته من رد `login` الذي لديك، أو استدعِ `CheckKeyAsync(key)` لاحقاً لقراءة حديثة دون استهلاك جهاز من الأجهزة المسموح بها

## اسمح للمستخدمين بنقل ترخيصهم إلى جهاز جديد

يرتبط المفتاح بالجهاز الذي يفعّله. عندما يستبدل العميل جهازه، يفشل `LoginAsync` على الجهاز الجديد برمز `HwidMismatch` (أو `DeviceLimit` إذا كان المفتاح متعدد الأجهزة ومستخدماً على كل أجهزته المسموح بها). تفكّ `ResetHardwareIdAsync` ارتباط المفتاح فوراً، دون تذكرة دعم ودون انتظارك، ثم يربطه `LoginAsync` التالي بهذا الجهاز

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

عند رفض إعادة التعيين، يوضح `reset.ErrorCode` السبب:

| الرمز | المعنى |
| --- | --- |
| `RATE_LIMITED` | أُعيد تعيين المفتاح مؤخراً، وتذكر الرسالة عدد ساعات الانتظار |
| `SELF_RESET_DISABLED` | أوقفتَ إعادة التعيين الذاتية لهذا التطبيق |
| `KEY_NOT_ACTIVE` | المفتاح منتهي الصلاحية أو محظور أو موقوف مؤقتاً |
| `NO_HWID` | المفتاح غير مرتبط بأي جهاز، فلا يوجد ما يُعاد تعيينه |
| `INVALID_KEY` | لا يوجد مفتاح كهذا لهذا التطبيق |

تتبع إعادة التعيين سياسة تطبيقك: يمكنك إيقافها، وتختار مدة الانتظار بين عمليتي إعادة تعيين (12 ساعة افتراضياً). وعند النجاح يحدد `reset.GetString("next_reset_at")` (بتوقيت UTC وصيغة ISO 8601) موعد السماح بإعادة التعيين التالية. تفكّ إعادة التعيين ارتباط كل أجهزة المفتاح وتنهي جميع جلساته، فيُسجَّل خروج الجهاز القديم عند نبضة التحقق الدوري التالية

تسجيل الخروج ليس إعادة تعيين: تنهي `LogoutAsync` الجلسة، لكن المفتاح يبقى مرتبطاً بالجهاز

## الحسابات التي تعمل بمفاتيح الترخيص

يشتري العملاء مفتاحاً، وينشئون به حساباً، ثم يضيفون لاحقاً مفاتيح أخرى إلى الحساب نفسه لتمديده: اسم مستخدم واحد وكلمة مرور واحدة، دون كومة من المفاتيح يحتفظون بها. فعّل **Sign-up needs a license key** في App Settings > Account sign-up لجعل المفتاح إلزامياً عند إنشاء الحساب

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

في App Settings تختار هل تُضاف مدة المفتاح **فوق المدة المتبقية** أم **تبدأ من جديد من الآن**، وهل **يحتفظ الحساب بحد أجهزته** أم **يأخذ حد أجهزة المفتاح**. لا يمكن تسجيل الدخول بمفتاح مستخدَم وحده (يرد `LoginAsync` بالرمز `KEY_REDEEMED`)، وحظره في لوحة التحكم يسترجع مدته من الحساب

| الرمز | المعنى |
| --- | --- |
| `KEY_REQUIRED` | لا ينشئ هذا التطبيق حسابات إلا بمفتاح |
| `KEY_ALREADY_USED` | أُضيف المفتاح إلى حساب من قبل |
| `KEY_IN_USE` | فُعّل المفتاح بتسجيل دخول بالترخيص، فلا يمكن إضافته إلى حساب |
| `INVALID_KEY` | لا يوجد مفتاح كهذا لهذا التطبيق |
| `ALREADY_LIFETIME` | لدى الحساب وصول مدى الحياة بالفعل |
| `INVALID_CREDENTIALS` | اسم المستخدم أو كلمة المرور غير صحيحة (تُحتسب ضمن محاولات قفل تسجيل الدخول) |

## ميزات أخرى

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

ما زالت نقاط النهاية التي لا تغلّفها هذه المكتبة بعد متاحة: الدوال `PostEnvelopeAsync` و`GetEnvelopeAsync` و`PostPlainAsync` عامة، وكذلك `CryptoEnvelope`. وترفض طريقتا النقل عبر الغلاف (envelope) أي رد غير مشفّر يدّعي النجاح، مثل كل الاستدعاءات المغلّفة

## قراءة النتائج

يوفّر `PwfResponse` الخصائص `Success` و`ErrorCode` و`Message`، إضافة إلى دوال قراءة محددة النوع وكائن `JsonElement` الخام للحقول الخاصة بكل نقطة نهاية:

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

## معرّف الجهاز

تقرأ `HardwareId.Get()` قيمة MachineGuid الخاصة بالتشفير في Windows، أو `/etc/machine-id` على Linux، أو معرّف UUID للمنصة على macOS، وتلجأ إلى اسم الجهاز إذا تعذّر ذلك. ولا تستخدم `wmic` الذي أُزيل في Windows 11 24H2. استبدلها بقيمتك عندما تحتاج إلى سياسة ربط مختلفة:

```csharp
var client = new PwfClient(new PwfClientOptions
{
    AppSecret    = secret,
    HardwareId   = myOwnFingerprint,
    BaseUrl      = "https://pwfauth.com",
});
```

## تفاصيل الشبكة

كل فشل في الحصول على رد صالح للاستخدام هو `PwfHttpException`. القيمة 0 في `StatusCode` تعني عدم وصول أي رد إطلاقاً (لا اتصال، أو خطأ DNS، أو جدار حماية، أو وكيل (proxy)، أو خطأ TLS، أو لا رد خلال `Timeout`)، ويكون خطأ الشبكة في `InnerException`. وأي قيمة أخرى هي حالة HTTP لرد ليس بصيغة JSON التي تعيدها الواجهة البرمجية (صفحة خطأ من CDN، أو عنوان أساسي خاطئ)

يحمل كل طلب الترويسة `User-Agent: PWFAuth-dotnet/1.3.1 (+https://pwfauth.com)`، ويتبع رقم الإصدار فيها إصدار الحزمة، لذا يسهل تمييز حركة المكتبة في السجلات وجدران الحماية. تُضبط الترويسات على كل طلب، ولا تُضبط أبداً على `HttpClient`. والكائن الذي تمرّره إلى `new PwfClient(options, httpClient)` (من `IHttpClientFactory`، أو مع وكيل مضبوط) يُستخدم كما هو تماماً: لا يغيّر العميل ترويساته ولا قيمة `Timeout` فيه، ولا يتخلص منه أبداً. اضبط `Timeout` فيه بنفسك، فقيمة `PwfClientOptions.Timeout` (15 ثانية افتراضياً) لا تنطبق إلا على `HttpClient` الذي ينشئه العميل لنفسه

## ملاحظة أمنية

يُشحن سر التطبيق داخل ملفك التنفيذي، وهذا أمر ملازم لبروتوكول الغلاف (envelope) الذي يحتاج إلى المفتاح على جهاز العميل ليشفّر. تعامل مع الناتج المترجَم على أنه حساس: طبّق تشويش الشيفرة على النسخ المبنية للإصدار، وأبقِ السر خارج التحكم بالمصادر العام (اقرأه من إعدادات يتحكم بها المطوّر قبل التوزيع). أي شخص يملك السر يستطيع استدعاء الواجهة البرمجية بصفته تطبيقك

بعد التحقق من توقيع الخادم المستقل، تشترط المكتبة أيضاً التشفير على نقاط نهاية الجلسة والمحتوى، ولا تعود بصيغة JSON غير مشفّرة إلا حالات الرفض التي تحدث قبل أن يتمكن الخادم من التحقق من الطلب (سر خاطئ، أو تجاوز حد الطلبات، أو ساعة بعيدة جداً عن الوقت الصحيح). لذا فإن الرد غير المشفّر الذي يدّعي النجاح لم يأتِ من PWF Auth، فترمي `LoginAsync` وبقية الاستدعاءات المشفّرة `PwfSecurityException` بدلاً من الوثوق به، ويعامله التحقق الدوري كخادم يتعذّر الوصول إليه. التقط هذا الاستثناء حيث تلتقط `PwfHttpException`، ولا تفتح التطبيق أبداً عند حدوثه

## الرخصة

رخصة MIT © PWF Auth

نُسخ HttpClient المخصّصة تُعدّ شيفرة موثوقة من التطبيق: اضبطها لترفض عمليات إعادة التوجيه وتتحقق من شهادات TLS. لا تستطيع المكتبة فحص المعالج الداخلي لطريقة نقل محقونة، ويبقى التحقق المستقل من توقيع الرد مطبّقاً
