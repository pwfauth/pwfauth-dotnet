<p align="center"><a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a> · <b>Türkçe</b> · <a href="README.ar.md">العربية</a></p>
<p align="center">Video eğitimleri: <a href="https://www.youtube.com/@pwfauth">youtube.com/@pwfauth</a></p>

# .NET için PWF Auth

## 1.7.0 kurulumu

1.7.0, **kullanıcı değişkenleri** özelliğini ekler: programınızın her müşteri için sunucuda tuttuğu değerler
(`SetVariableAsync`, `GetVariableAsync`, `GetVariablesAsync`, `DeleteVariableAsync`). Bu değerler
panelden düzenlenebilir. Ayrıntılar için [CHANGELOG.md](CHANGELOG.md) dosyasına bakın


PWF Auth yanıtlarını artık yeni bir anahtarla imzalıyor. PWFAuth 1.5.0 ve sonraki sürümler bu anahtarı sabitler (pin);
**1.4.0 güncel sunucuyla iletişim kuramaz**, bu yüzden paketi güncelleyip uygulamanızı yeniden derleyin

```sh
dotnet add package PWFAuth --version 1.7.0
```

1.6.0, pwfauth.com adını engelleyen ağlarda (BAE'de Etisalat) da çalışır: bir istek hiç yanıt alamazsa
aynı sunucuya `https://pwfauth.net` üzerinden gider. 1.5.2 ise bağlantı herhangi bir yanıt gelmeden
koptuğunda (Windows hatası 10054) isteği bir kez daha gönderir. Ayrıntılar için
[CHANGELOG.md](CHANGELOG.md) dosyasına bakın

## Sunucu kimlik doğrulama güncellemesi

Hatalar ve saat düzeltme yanıtları dahil tüm yanıtlar, sabitlenmiş PWFAuth açık anahtarıyla oluşturulmuş
geçerli bir RSA-SHA256 imzası taşımalıdır. İmzalamada kullanılan özel anahtar sunucuda kalır. Uygulama
gizli anahtarını (app secret) bilmek bu imzayı taklit etmeye yetmez. Her seferinde yeni üretilen rastgele
bir nonce, her yanıtı kendi isteğine, metoduna, yoluna, HTTP durum koduna ve gövdenin birebir baytlarına
bağlar (PWF-REPLY-V1). Sorgu dizeleri (query string) V1 imzalı yolun parçası değildir; canlı ortamdaki
HTTPS bunları aktarım sırasında korur

SDK yalnızca `https://pwfauth.com` adresini kabul eder; diğer tüm kaynaklar (origin) reddedilir. Varsayılan
aktarım katmanı TLS sertifikalarını doğrular ve yönlendirmeleri reddeder. Eksik veya geçersiz imzalar, JSON
ayrıştırma ya da zarf (envelope) şifre çözme işleminden önce güvenli biçimde başarısız olur (fail closed);
imzasız proxy/CDN hataları güvenlik hatası sayılır. Eski imzasız protokole geri dönüş yoktur. Önce sunucuyu,
sonra istemcileri güncelleyin. Uygulamalar bir güvenlik veya aktarım hatasından sonra hiçbir işlevin kilidini
asla açmamalıdır

Bu önlem sunucu taklidine (server emulation) karşıdır. İstemci makinesini kontrol eden bir saldırganın
uygulamanın kendisini değiştirmesini engellemez. Değerli işlemlerde son sözü sunucuya bırakın ve gerçek
uygulama gizli anahtarlarını herkese açık depolara koymayın


[PWF Auth](https://pwfauth.com) için resmi istemci: lisans anahtarları, kullanıcı hesapları,
donanım kimliği (HWID) bağlama, sunucu tarafında uzaktan durdurma (kill switch) içeren şifreli oturumlar,
ücretsiz denemeler, uzak içerik ve OTA güncelleme kontrolleri

```bash
dotnet add package PWFAuth
```

Hedef platformlar **netstandard2.0** (.NET Framework 4.6.2+ üzerinde çalışır, yani WinForms/WPF ve VB.NET
masaüstü uygulamaları tam destek alır) ve **net8.0**

## 1.3.1 sürümündeki yenilikler

* **Bağlantı olmaması artık bir `PwfHttpException`.** Çevrimdışı olma, DNS, güvenlik duvarı, proxy veya TLS:
  çağrı, `StatusCode` değeri 0 olan ve ağ hatasını `InnerException` olarak taşıyan bir `PwfHttpException`
  fırlatır. 1.3.0 ham `HttpRequestException` istisnasını olduğu gibi geçiriyordu. `LogoutAsync` bu durumda artık istisna
  fırlatmak yerine, belgelendiği gibi `null` döndürür

## 1.3.0 sürümündeki yenilikler

* **Lisans anahtarıyla kayıt olun, hesabı uzatmak için anahtar ekleyin.** `RegisterAccountWithKeyAsync`
  bir anahtardan hesap oluşturur (hesap, anahtarın süresini ve cihaz sınırını alır), `RedeemKeyAsync` ise
  mevcut bir hesaba, süresi dolmuş olsa bile, başka bir anahtarın süresini ekler. Aşağıdaki
  *Lisans anahtarlarıyla çalışan hesaplar* bölümüne bakın
* Yeni `PwfErrorCodes` değerleri: `KeyRequired`, `KeyAlreadyUsed`, `KeyInUse`, `KeyRedeemed`,
  `AlreadyLifetime`, `UsernameExists`

## 1.2.0 sürümündeki yenilikler

* **Yanlış sistem saati kendini düzeltir.** Sunucu, zaman damgası beş dakikadan fazla sapan bir isteği
  reddeder ve artık reddetme yanıtıyla birlikte kendi saatini de gönderir. İstemci zaman damgalarını aradaki
  fark kadar kaydırır ve isteği bir kez daha gönderir; böylece tarihi veya saati yanlış olan bir bilgisayarda da
  oturum açma ve düzenli kontrol (heartbeat) çalışır, "saatinizi kontrol edin" destek talepleri de
  sona erer. Bu davranışı `PwfClientOptions.AutoCorrectClock = false` ile kapatabilirsiniz

## 1.1.0 sürümündeki yenilikler

* **`ResetHardwareIdAsync`**: müşteriler lisanslarını kendileri, anında yeni bir bilgisayara taşır
  (aşağıdaki *Kullanıcıların lisanslarını yeni bir bilgisayara taşımasına izin verin* bölümüne
  bakın). `RequestHardwareResetAsync` artık kullanımdan kaldırıldı (obsolete): panel bu talepleri henüz
  göstermediği için hiç kimse onaylayamıyordu
* **Düz "başarılı" yanıtları reddedilir.** Gerçek sunucu, oturum ve içerik uç noktalarındaki her yanıtı
  şifreler; bu nedenle başarı iddia eden düz bir JSON yanıtı ancak bir proxy'den veya sahte bir sunucudan
  gelebilir. Böyle bir yanıt artık `PwfSecurityException` fırlatır. Düz başarısızlık yanıtları ise hâlâ
  başarısız yanıt olarak döndürülür
* **Uzaktan durdurma, saati değiştirerek atlatılamaz.** Heartbeat artık yalnızca şifreli yanıtları cevap
  olarak sayar; böylece sunucunun beş dakikadan fazla sapan bir saate gönderdiği düz reddetme yanıtları yok
  sayılmak yerine oturumu `CLOCK_SKEW` ile sonlandırır. Düz HTTP 429 yanıtlarının kendi sınırı vardır:
  `MaxRateLimitedBeats` (bkz. *Heartbeat neden önemli*)
* `StartHeartbeat()` arayüz iş parçacığından (UI thread) çağrıldığında **`SessionEnded` olayı da o iş parçacığında
  tetiklenir** (WinForms/WPF), böylece olay işleyicileri kontrollere doğrudan erişebilir. Bu davranışı
  `PwfClientOptions.RaiseEventsOnCapturedContext = false` ile kapatabilirsiniz
* Yeni `PwfErrorCodes` değerleri: `DeviceLimit`, `OpenAccessLimit`, `KeyNotActive`, `NoHwid`,
  `RateLimited`, `SelfResetDisabled` ve istemci tarafındaki `ClockSkew`
* Her istek kendini `User-Agent: PWFAuth-dotnet/1.1.0 (+https://pwfauth.com)` ile tanıtır
* Sizin verdiğiniz bir `HttpClient` artık değiştirilmez: `Timeout` değerini kendiniz ayarlayın

Tüm geçmiş: [CHANGELOG.md](https://github.com/pwfauth/pwfauth-dotnet/blob/main/CHANGELOG.md)

## Hızlı başlangıç

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

Bir formda bu `StartHeartbeat()` çağrısı arayüz iş parçacığında çalışır; bu yüzden `SessionEnded` de arayüz
iş parçacığında tetiklenir ve `MessageBox` güvenle kullanılabilir. Aşağıdaki *WinForms: arayüz iş parçacığında
SessionEnded* bölümüne bakın

## Heartbeat neden önemli

`StartHeartbeat()` isteğe bağlı bir kayıt tutma işi değildir: lisans kurallarının uygulandığı noktanın ta kendisidir

* Sunucu, heartbeat göndermeyi bırakan bir oturumu düşürür; yani hiç heartbeat göndermeyen bir istemci
  hiçbir şey kaybetmez, ama lisansının iptal edildiğini de asla öğrenmez
* Her heartbeat lisansı yeniden kontrol eder: yasaklama, duraklatma, süre dolması, HWID sıfırlama, silme ve
  bakım modu, oturumu hemen bir sonraki heartbeat'te sonlandırır
* Sunucuya art arda `MaxHeartbeatFailures` heartbeat boyunca (varsayılan 3) ulaşılamazsa istemci oturumu
  kendisi `NETWORK_LOST` ile sonlandırır. Bu olmasaydı, lisans alan adını bir güvenlik duvarında engellemek
  uygulamanın sonsuza kadar çalışmasını sağlardı
* Düz, şifrelenmemiş bir "başarılı" yanıtı alan bir heartbeat lisans sunucusuyla konuşmamıştır; bu yüzden
  başarısız heartbeat sayılır. Sunucunun yerine yanıt veren bir proxy de uygulamayı çalışır durumda tutamaz

### Uzaktan durdurma, saati değiştirerek atlatılamaz

Her isteğe zaman damgası eklenir ve sunucu, beş dakikadan fazla sapan bir isteği reddeder; isteği
doğrulayamadığı için bunu düz, şifrelenmemiş bir hatayla yapar. 1.1.0'dan önce heartbeat bu reddetmeleri
görmezden geliyordu; bu yüzden oturum açtıktan sonra sistem saatini değiştirmek uygulamanın çalışmaya devam
etmesini ve yasaklamalardan habersiz kalmasını sağlıyordu. Artık yalnızca şifreli bir yanıt cevap olarak sayılır:
art arda `MaxHeartbeatFailures` reddetme, oturumu `CLOCK_SKEW` ile sonlandırır ve bu kodun mesajı kullanıcıdan
tarihi ve saati düzeltmesini ister (diğer düz reddetmeler oturumu `NETWORK_LOST` ile sonlandırır). Düz
`429 Too Many Requests` yanıtları için daha geniş bir sınır vardır: `MaxRateLimitedBeats` (varsayılan
10). Böylece yoğun, paylaşılan bir IP arkasındaki kullanıcıların oturumu bir dakika içinde kapanmaz, ama sürekli
429 döndüren bir proxy yine de oturumu sonlandırır

1.2.0'dan itibaren istemci önce saati düzeltmeyi dener: sunucunun reddetme yanıtı kendi saatini taşır, istemci
zaman damgalarını aradaki fark kadar kaydırır ve heartbeat'i bir kez daha gönderir (`AutoCorrectClock`,
varsayılan olarak açık). Yasaklamalar yine de ulaşır, çünkü düzeltilmiş heartbeat gerçek, şifreli bir
yanıt alır. Oturum yalnızca bu işe yaramadığında ya da `AutoCorrectClock = false` olduğunda `CLOCK_SKEW` ile sona erer

## WinForms: arayüz iş parçacığında SessionEnded

`StartHeartbeat()`, çağrıldığı `SynchronizationContext` bağlamını hatırlar. Bu metodu arayüz iş parçacığından
çağırırsanız (örneğin bir düğme işleyicisinde `await client.LoginAsync(...)` satırının hemen ardından),
`SessionEnded` yine o iş parçacığında tetiklenir; böylece işleyici kontrolleri `Invoke`/`BeginInvoke`
kullanmadan (WPF'te `Dispatcher` olmadan) doğrudan güncelleyebilir

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

Yalnızca olay arayüz iş parçacığına aktarılır (marshal edilir); heartbeat'lerin kendisi asla arayüz iş
parçacığında çalışmaz. Arayüz bağlamının olmadığı yerlerde (konsol uygulamaları, servisler, bir arka plan iş
parçacığı) olay, 1.0'da olduğu gibi heartbeat'in kendi iş parçacığında tetiklenir. Her zaman bu davranışı
istiyorsanız `PwfClientOptions` içinde `RaiseEventsOnCapturedContext = false` ayarlayın

## Oturum açma yanıtı neler içerir

Başarılı bir `LoginAsync` yanıtı lisans durumunun tamamını taşır; bu yüzden kullanıcıya neye sahip olduğunu
göstermek için ikinci bir çağrıya nadiren ihtiyaç duyarsınız

| Alan | Tür | Notlar |
| --- | --- | --- |
| `user.license_key` | string | Etkinleştirilen anahtar |
| `user.key_type` | string | `days`, `hours`, `lifetime`, … |
| `user.duration` | number | `key_type` biriminde süre |
| `user.hwid` | string | Bu oturumun bağlı olduğu makine |
| `user.activated_at` | string | UTC ISO 8601, ilk etkinleştirme |
| `user.expires_at` | string \| **null** | UTC ISO 8601. **null, ömür boyu anlamına gelir** |
| `user.days_remaining` | number \| **null** | Ömür boyu anahtarlarda **null** |
| `user.status` | string | `active` |
| `features` | object | Panelde ayarladığınız, anahtara özel özellik bayrakları |
| `app.name` / `app.version` / `app.message` | string | Uygulamanızın adı, güncel sürümü ve oturum açma mesajı |
| `texts` / `slides` | object / array | Uzak metinler ve duyuru slaytları |
| `session_id` | string | İstemci tarafından dahili olarak da tutulur |
| `heartbeat_interval` | number | Saniye cinsinden; `StartHeartbeat()` bu değeri zaten dikkate alır |

Ömür boyu anahtarlarda `expires_at` ve `days_remaining` değerleri `null` olur. Biçimlendirmeden önce mutlaka
kontrol edin; aksi halde ömür boyu lisansa sahip bir müşteri "süresi dolmaz" yazısı yerine bir çökme görür

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

VB.NET ile aynı işlem, bir WinForms etiketi (label) için:

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

Bu verilerin hiçbiri istemci tarafından önbelleğe alınmaz: elinizdeki `login` yanıtından yeniden okuyun ya da
bir cihaz hakkı tüketmeden güncel bilgi almak için daha sonra `CheckKeyAsync(key)` çağırın

## Kullanıcıların lisanslarını yeni bir bilgisayara taşımasına izin verin

Bir anahtar, kendisini etkinleştiren makineye bağlanır. Müşteri bilgisayarını değiştirdiğinde, yeni bilgisayarda
`LoginAsync` çağrısı `HwidMismatch` ile başarısız olur (zaten tüm makinelerinde kullanılan çok cihazlı bir anahtar
için `DeviceLimit`). `ResetHardwareIdAsync` anahtarın makine bağını anında kaldırır (destek talebine ya da sizin
bir şey yapmanıza gerek kalmadan) ve bir sonraki `LoginAsync` anahtarı bu makineye bağlar

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

Sıfırlama reddedildiğinde nedenini `reset.ErrorCode` söyler:

| Kod | Anlamı |
| --- | --- |
| `RATE_LIMITED` | Anahtar yakın zamanda sıfırlandı; mesaj kaç saat beklemek gerektiğini belirtir |
| `SELF_RESET_DISABLED` | Bu uygulama için kullanıcıların kendi başına sıfırlama yapmasını kapattınız |
| `KEY_NOT_ACTIVE` | Anahtarın süresi dolmuş, anahtar yasaklanmış ya da duraklatılmış |
| `NO_HWID` | Anahtar hiçbir makineye bağlı değil, sıfırlanacak bir şey yok |
| `INVALID_KEY` | Bu uygulamada böyle bir anahtar yok |

Sıfırlamalar uygulamanızın politikasına göre işler: bunları kapatabilir ve iki sıfırlama arasındaki bekleme
süresini belirleyebilirsiniz (varsayılan 12 saat). İşlem başarılı olursa `reset.GetString("next_reset_at")`
(UTC, ISO 8601) bir sonraki sıfırlamaya ne zaman izin verileceğini söyler. Sıfırlama, anahtarın tüm cihaz
bağlarını kaldırır ve tüm oturumlarını sonlandırır; böylece eski bilgisayarın oturumu bir sonraki heartbeat'te kapanır

Oturumu kapatmak sıfırlama anlamına gelmez: `LogoutAsync` oturumu sonlandırır, ancak anahtar makineye bağlı kalır

## Lisans anahtarlarıyla çalışan hesaplar

Müşteriler bir anahtar satın alır, bu anahtarla bir hesap oluşturur ve daha sonra hesabı uzatmak için aynı hesaba
başka anahtarlar ekler: tek bir kullanıcı adı ve parola yeterlidir, saklanacak bir anahtar yığını olmaz. Kayıt
sırasında anahtarı zorunlu kılmak için App Settings > Account sign-up altında **Sign-up needs a license key**
seçeneğini açın

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

App Settings bölümünde, bir anahtarın süresinin **kalan sürenin üzerine eklenmesini** mi yoksa **şu andan
itibaren yeniden başlamasını** mı istediğinizi ve hesabın **kendi cihaz sınırını koruyacağını** mı yoksa
**anahtarın sınırını alacağını** mı seçersiniz. Kullanılmış bir anahtarla tek başına oturum açılamaz (`LoginAsync`
yanıtı `KEY_REDEEMED` olur) ve bu anahtarı panelde yasaklamak, eklediği süreyi hesaptan geri alır

| Kod | Anlamı |
| --- | --- |
| `KEY_REQUIRED` | Bu uygulama yalnızca anahtarla hesap oluşturur |
| `KEY_ALREADY_USED` | Anahtar zaten bir hesaba eklenmiş |
| `KEY_IN_USE` | Anahtar bir lisans girişiyle etkinleştirilmiş, bu yüzden bir hesaba eklenemez |
| `INVALID_KEY` | Bu uygulamada böyle bir anahtar yok |
| `ALREADY_LIFETIME` | Hesabın zaten ömür boyu erişimi var |
| `INVALID_CREDENTIALS` | Kullanıcı adı veya parola yanlış (oturum açma kilidi için deneme sayısına eklenir) |

## Başka neler yapabilir

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

Bu istemcinin henüz sarmalamadığı uç noktalara yine de erişebilirsiniz: `PostEnvelopeAsync`,
`GetEnvelopeAsync` ve `PostPlainAsync` herkese açıktır (public), `CryptoEnvelope` da öyle. İki zarf (envelope)
aktarımı da sarmalanmış her çağrı gibi, başarı iddia eden düz bir yanıtı reddeder

## Sonuçları okuma

`PwfResponse`; `Success`, `ErrorCode` ve `Message` özelliklerinin yanı sıra tipli okuma metotları (getter) ve
uç noktaya özgü alanlar için ham `JsonElement` sunar:

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

## Donanım kimliği

`HardwareId.Get()`, Windows'ta kriptografi MachineGuid değerini, Linux'ta `/etc/machine-id` dosyasını, macOS'te
ise platform UUID'sini okur; bunlar alınamazsa makine adını kullanır. Windows 11 24H2'de kaldırılan `wmic` aracını
kullanmaz. Farklı bir bağlama politikasına ihtiyacınız olduğunda bu değeri kendiniz belirleyin:

```csharp
var client = new PwfClient(new PwfClientOptions
{
    AppSecret    = secret,
    HardwareId   = myOwnFingerprint,
    BaseUrl      = "https://pwfauth.com",
});
```

## Ağ ayrıntıları

Kullanılabilir bir yanıtın alınamadığı her durum bir `PwfHttpException` olarak bildirilir. `StatusCode` 0, hiç yanıt
gelmediği anlamına gelir (bağlantı yok, DNS, güvenlik duvarı, proxy, TLS ya da `Timeout` süresi içinde yanıt
gelmemesi); ağ hatası `InnerException` içindedir. Diğer tüm değerler, API'nin JSON'u olmayan bir yanıtın HTTP
durum kodudur (bir CDN hata sayfası, yanlış bir temel URL)

Her istek `User-Agent: PWFAuth-dotnet/1.3.1 (+https://pwfauth.com)` başlığını taşır (sürüm numarası paketin
sürümünü izler); böylece SDK trafiği günlüklerde ve güvenlik duvarlarında kolayca ayırt edilir. Başlıklar her
isteğe ayrı ayrı eklenir, asla `HttpClient` üzerine ayarlanmaz. `new PwfClient(options, httpClient)` ile
verdiğiniz bir istemci (`IHttpClientFactory` üzerinden ya da proxy yapılandırılmış olarak) olduğu gibi kullanılır:
SDK onun başlıklarını veya `Timeout` değerini asla değiştirmez ve onu asla serbest bırakmaz (dispose). Bu
istemcinin `Timeout` değerini kendiniz ayarlayın: `PwfClientOptions.Timeout` (varsayılan 15 saniye) yalnızca
SDK'nın kendisi için oluşturduğu `HttpClient` için geçerlidir

## Güvenlik notu

Uygulama gizli anahtarı ikili dosyanızın (binary) içinde dağıtılır; bu, şifreleme için anahtarın istemcide
bulunmasını gerektiren zarf protokolünün doğasından kaynaklanır. Derlenmiş çıktıyı hassas kabul edin: sürüm
derlemelerini karartın (obfuscate) ve gizli anahtarı herkese açık kaynak kontrolünden uzak tutun (dağıtımdan
önce geliştiricinin kontrolündeki bir yapılandırmadan okuyun). Gizli anahtara sahip olan herkes API'yi sizin
uygulamanız gibi çağırabilir

Bağımsız sunucu imzasını doğruladıktan sonra SDK, oturum ve içerik uç noktalarında ayrıca şifreleme de şart koşar;
yalnızca sunucunun isteği doğrulayabilmesinden önce gerçekleşen reddetmeler (hatalı gizli anahtar, istek sınırı,
çok fazla sapan bir saat) düz JSON olarak döner. Dolayısıyla başarı iddia eden düz bir yanıt PWF Auth'tan gelmemiştir:
`LoginAsync` ve diğer şifreli çağrılar ona güvenmek yerine `PwfSecurityException` fırlatır, heartbeat ise bunu
ulaşılamayan bir sunucu gibi değerlendirir. Bu istisnayı `PwfHttpException` yakaladığınız yerde yakalayın ve
buna dayanarak uygulamanın kilidini asla açmayın

## Lisans

MIT © PWF Auth

Özel HttpClient örnekleri güvenilir uygulama kodu sayılır: bunları yönlendirmeleri reddedecek ve TLS sertifikalarını doğrulayacak şekilde yapılandırın. SDK, dışarıdan verilen bir aktarım katmanının iç işleyicisini (handler) inceleyemez. Bağımsız yanıt imzası doğrulaması yine de uygulanır
