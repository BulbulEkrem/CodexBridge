# Faz 7+ — Canlı Test Kontrol Listesi (kullanıcı makine + gerçek kimlik başındayken)

> Durum: **hazır — kullanıcı uygulaması bekleniyor** · Tarih: 2026-08-07
> Kapsam: Bu doküman, otonom oturumlarda kod düzeyinde/sentetik veriyle doğrulanan ama **gerçek
> makine, gerçek kimlik bilgisi ve gerçek cihaz** gerektirdiği için ertelenen tüm doğrulamaları
> tek bir uygulanabilir kontrol listesinde toplar.
>
> Bu bir QA dokümanıdır; kod değiştirmez. Her madde başarısız olursa **kod düzeltme değil,
> bulguyu raporlama** beklenir (nasıl tetiklendi + beklenen/gerçekleşen + etkilenen dosya).

## Nasıl kullanılır

- Her madde: **(a) ön koşul/donanım/kimlik**, **(b) adımlar**, **(c) geçme kriteri**, **(d) hata -> nereye bak**.
- Bu makinede **Smart App Control (SAC) enforce** modda: `dotnet run` / `dotnet test` imzasız DLL'i
  `0x800711C7` ile engeller. **Her zaman derlenmiş apphost `.exe`'yi doğrudan çalıştır.**
  (Kaynak: `.claude/knowledge` gotcha, 2026-08-06.)
- Derleme: `dotnet build CodexBridge.slnx -c Debug` (karışık platform -> `-p:Platform` override'sız).
  WinUI için: `dotnet build src/CodexBridge.Taskbar/CodexBridge.Taskbar.csproj -c Debug -p:Platform=x64`.
- Kimlik bilgileri **asla** komut satırına/sohbete girmez; User-scope ortam değişkeni veya dosya yolu
  ile verilir (mevcut harness'lar buna göre yazılmış).

---

## Ön koşul özeti (teste başlamadan hazırla)

Donanım / ortam:
- [ ] Windows 11 makinesi (görev çubuğu testleri için bu makine).
- [ ] Mümkünse **ikinci monitör** (farklı DPI/ölçek ideal, ör. %100 + %150).
- [ ] Gerçek bir **iOS cihaz** (push testi için) ve/veya **Android cihaz**.
- [ ] Chrome ve/veya Edge kurulu, en az bir `web` stratejili sağlayıcıda (ör. Perplexity) açık oturum.

Kimlik bilgileri / hesaplar:
- [ ] **OpenAI API anahtarı** (`OPENAI_API_KEY`, opsiyonel `OPENAI_PROJECT_ID`) — JS sağlayıcı canlı testi.
- [ ] Yerelde **Claude Code oturumu** açık (OAuth token dosyada taze) — Claude canlı testi (API anahtarı gerekmez).
- [ ] **APNs `.p8` anahtarı** + Key ID + Team ID + Bundle ID (iOS push için) VEYA
- [ ] **FCM service account JSON** (Android push için).
- [ ] Widget'ların bearer ile bağlanacağı **dashboard token** (`CODEXBAR_DASHBOARD_TOKEN`).
- [ ] (Faz 2 canlı entegrasyon için) derlenmiş/çalışan bir **Win-CodexBar `serve`** örneği (opsiyonel).

Telefon araç zinciri (widget derlemesi için):
- [ ] iOS: macOS + Xcode. Android: Android Studio + SDK. (Bu Windows ortamında **yok** -> ayrı makine.)

---

## 1. Görev çubuğu yüzeyi (WinUI 3) — Faz 0/1 gitme-gitmeme kriterleri

Ön koşul (tümü): `CodexBridge.Taskbar` x64 derlenmiş, apphost exe çalıştırılabilir. Faz 0 belgesindeki
"gitme/gitmeme" tablosunda ⏳/⬜ işaretli maddeler burada kapatılır.

### 1.1 Band görev çubuğunda görünüyor (golden path — regresyon)
- (a) Bu Windows 11 makinesi.
- (b) `CodexBridge.Taskbar` exe'sini çalıştır. Görev çubuğuna bak.
- (c) Band görev çubuğunun **içinde** (üstünde yüzen ayrı pencere değil), boş sol alanda, üç
  sağlayıcı pill'i görünür. Pencere kromu (başlık/min/büyüt/kapat) **yok**.
- (d) Hata -> `src/CodexBridge.Taskbar/Taskbar/TaskbarHost.cs` (SetParent/konum), `MainWindow.xaml.cs`
  (WndProc/kromu sıyırma). Krom görünüyorsa: `OverlappedPresenter.SetBorderAndTitleBar` + Win32 caption
  stil sıyırma (bkz. decisions.md 2026-08-07 BULUNAN HATA 1).

### 1.2 Explorer-restart hayatta kalma (regresyon — daha önce GEÇTİ)
- (a) Kaydedilmemiş iş olmamalı (explorer yeniden başlar).
- (b) Görev Yöneticisi -> `Windows Gezgini` -> **Yeniden başlat**. (Alternatif: `taskkill /f /im explorer.exe`
  sonra `start explorer.exe`.)
- (c) App süreci **hayatta kalır**, gözcü tetiklenir, band ~500 ms içinde YENİ görev çubuğuna otomatik
  yeniden parent'lanır. Segfault/çökme (exit 139) **yok**.
- (d) Hata -> `Taskbar/TaskbarWatchdog.cs`, `App.xaml.cs::OnTaskbarRecreated`. Çökme olursa: ölmüş
  pencereye `Window.Close()` çağrılmadığını doğrula (decisions.md 2026-08-07 BULUNAN HATA 2).

### 1.3 Çoklu monitör / farklı DPI (HENÜZ DOĞRULANMADI)
- (a) İkinci monitör bağlı; ideal olarak iki monitör farklı ölçek faktöründe (%100 + %150).
- (b) Görev çubuğunu farklı monitöre taşı; ana ekranı değiştir; ölçek faktörünü Ayarlar'dan değiştir.
  `WM_DISPLAYCHANGE` ve DPI değişimi tetiklenir.
- (c) Band doğru monitörde, doğru boyutta (bulanık değil), doğru konumda kalır; pill metni kırpılmaz.
- (d) Hata -> `MainWindow.xaml.cs` WndProc (`WM_DISPLAYCHANGE`), `GetDpiForWindow()/96.0` ölçekleme;
  `TaskbarHost` konum hesabı.

### 1.4 Görev çubuğu otomatik gizleme (HENÜZ DOĞRULANMADI)
- (a) —
- (b) Ayarlar -> Görev çubuğu -> "Görev çubuğunu otomatik gizle" aç. Fareyi kenardan uzaklaştır/yaklaştır.
- (c) Band, görev çubuğu ile **birlikte** gizlenir ve birlikte geri gelir (çocuk pencere olduğundan
  otomatik olmalı); ekranda asılı kalan artık band yok.
- (d) Hata -> parent-child ilişkisi bozulmuş olabilir (`SetParent`); çubuk gizliyken band görünür
  kalıyorsa z-order/parent kaybı -> `TaskbarHost`.

### 1.5 Görev çubuğunun üste/yana taşınması + ortalı çubukta boş alan
- (a) —
- (b) (Win11'de yan/üst konum kısıtlı olabilir; mümkünse dene.) Görev çubuğu hizalamasını
  ortalı/sola al; genişliği/ölçeği değiştir. `WM_SETTINGCHANGE(SPI_SETWORKAREA)` tetiklenir.
- (c) Band, ReBar'a göre yeniden konumlanır; ortalı çubukta boş sol alana oturur, diğer çubuk
  öğelerinin üstüne binmez.
- (d) Hata -> `MainWindow.xaml.cs` `WM_SETTINGCHANGE` debounce; boş alan hesabı (Deskband11
  `UpdateTaskbarButtons` mantığı).

### 1.6 Windows Update sonrası davranış (sahada gözlem)
- (a) Bir Windows kümülatif güncellemesi + yeniden başlatma.
- (b) Güncelleme sonrası uygulamayı yeniden çalıştır (veya otomatik başlıyorsa gözle).
- (c) Band hâlâ parent'lanıp görünür; `Shell_TrayWnd`/`ReBarWindow32` sınıf adları değişmemiş.
- (d) Hata -> sınıf adı/pencere ağacı değişmişse `TaskbarHost` FindWindow zinciri; uzun vadeli
  kırılganlık riski (bkz. 00-MIMARI risk tablosu: "Microsoft tekniği engeller" -> tepsi ikonu fallback).

### 1.7 (Opsiyonel) Salt Win32 spike ile karşılaştırma
- (a) `spikes/taskbar-parenting/` derlenmiş.
- (b) WinForms spike'ını çalıştır (Windows SDK gerektirmez, hızlı sağlık kontrolü).
- (c) Spike penceresi `Shell_TrayWnd` çocuğu, x~12'de görünür (daha önce doğrulandı — teknik sağlık kontrolü).
- (d) Hata -> `spikes/taskbar-parenting/Program.cs`.

---

## 2. dashboard/v1 HTTP host — Faz 3 (regresyon + gerçek istemci)

Ön koşul: `CodexBridge.Host` derlenmiş apphost exe. curl mevcut. Daha önce 401/200 curl ile doğrulandı;
burada regresyon + LAN/telefon senaryosu.

### 2.1 fails-closed 401 (token yok / yanlış)
- (a) `CODEXBAR_DASHBOARD_TOKEN` ayarlı host çalışıyor (varsayılan loopback).
- (b) `curl -i http://127.0.0.1:<port>/dashboard/v1/snapshot` (Authorization başlığı **olmadan**),
  ardından yanlış token ile.
- (c) İkisi de **401**. `/health` ise Authorization'sız **200** (`{status:ok}`).
- (d) Hata -> `src/CodexBridge.Host` bearer middleware; token yoksa açık geçmemeli (fails-closed).

### 2.2 doğru token -> 200 + şema doğruluğu
- (a) Geçerli bearer token.
- (b) `curl -i -H "Authorization: Bearer <token>" http://127.0.0.1:<port>/dashboard/v1/snapshot`.
- (c) **200**, `Cache-Control: no-store`, gövde `schemaVersion:1`, `providers[]`, kimlik **maskeli**
  (`accountEmail` yerel kısmı gizli), `staleAfterSeconds` mevcut.
- (d) Hata -> `CodexBridge.Core/Dashboard/DashboardSnapshot.cs` serileştirme (camelCase, küçük enum,
  null atlama); maskeleme `JsSnapshotMapper`/host.

### 2.3 Loopback dışı bind güvenliği (LAN / telefon erişimi)
- (a) Telefonun host'a erişebilmesi için LAN IP'sine bind gerekiyor.
- (b) Host'u loopback dışı bir adrese token'sız ve `--allow-plain-http`'siz başlatmayı dene; sonra
  token + `--allow-plain-http` ile başlat. Başka bir cihazdan/telefondan `curl`/tarayıcı ile eriş.
- (c) Loopback dışı bind, token yoksa **reddedilir/uyarı verir**; token + allow-plain-http ile açılır.
  DNS-rebinding koruması (Host header kontrolü) çalışır.
- (d) Hata -> `CodexBridge.Host` bind/Host-header kontrolü. Düz HTTP'de token açık geçtiği için
  TLS proxy/Tailscale önerisini not et (00-MIMARI risk).

---

## 3. JS sağlayıcı katmanı (ClearScript/V8) — Faz 5 gerçek sağlayıcı bağlantısı

Bu, README'deki "JS eklentilerin GERÇEK sağlayıcıya bağlanması" maddesidir. **İki hazır canlı harness var.**
Runtime daha önce mock http ile 11/11 kanıtlandı; burada **gerçek ağ + gerçek kimlik**.

### 3.1 OpenAI — gerçek API anahtarıyla openai.js (harness: CodexBridge.RealData)
- (a) User ortam değişkeni: `[Environment]::SetEnvironmentVariable("OPENAI_API_KEY","sk-...","User")`
  (opsiyonel `OPENAI_PROJECT_ID`). Not: OpenAI usage endpoint'i inference harcamaz, güvenli.
- (b) `dotnet build src/CodexBridge.RealData/... -c Debug` -> derlenmiş **exe**'yi çalıştır (SAC nedeniyle).
- (c) Konsol "GERÇEK OpenAI verisi (dashboard/v1 satırı)" başlığıyla gerçek kullanım/maliyet yazar;
  anahtar **asla yazdırılmaz** (yalnızca uzunluk+önek). E-posta/kimlik maskeli. Çıkış kodu 0.
- (d) Hata -> `src/CodexBridge.RealData/Program.cs`, `CodexBridge.JsHost/HttpJsHostBridge.cs`,
  `JsProviderRuntime.cs`, `plugins/openai.js`. `host.http is not a function` görülürse delegate
  bağlama regresyonu (decisions.md Faz 5). 401 -> anahtar/endpoint.

### 3.2 Claude — yerel OAuth token ile (harness: CodexBridge.ClaudeData, API anahtarı YOK)
- (a) Yerelde açık/taze bir Claude Code oturumu (OAuth token dosyada geçerli).
- (b) `CodexBridge.ClaudeData` exe'sini çalıştır.
- (c) "GERÇEK Claude kullanımı" başlığıyla plan + kota pencereleri yazılır; token yazdırılmaz.
  Çıkış kodu 0. (Pencere yoksa "pencere yok" mesajı normal olabilir.)
- (d) Hata -> `src/CodexBridge.ClaudeData/Program.cs`, `CodexBridge.Core/Sources/.../ClaudeOAuthSource`.
  **401 -> token süresi dolmuş**; bir Claude Code oturumu aç/kapat, tekrar dene (program bunu söylüyor).

### 3.3 Kısmi başarı / hata izolasyonu (JsUsageSource)
- (a) 3.1/3.2 kurulu; ayrıca kasıtlı bozuk bir kimlik (yanlış anahtar) hazırla.
- (b) Birden fazla eklentiyi (biri geçerli, biri geçersiz kimlikli) `JsUsageSource` ile çalıştır.
- (c) Geçerli sağlayıcı verisi gelir; hatalı olan snapshot'ta `error` alanıyla işaretlenir, **diğerini
  çökertmez** (kısmi başarı korunur).
- (d) Hata -> `CodexBridge.JsHost/JsUsageSource.cs`, `JsSnapshotMapper.cs`.

### 3.4 (Opsiyonel) .ts eklenti + Sucrase
- (a) Bir `.ts` sağlayıcı eklentisi.
- (b) Sucrase transpile adımıyla yükle (`sucrase-3.35.1.min.js` kopyalı).
- (c) TS -> JS transpile edilir ve çalışır. (Bundled sağlayıcılar zaten düz JS; bu yalnız TS yolu için.)
- (d) Hata -> JsHost transpile entegrasyonu (henüz devrede olmayabilir — 04 belgesi "henüz devrede değil").

---

## 4. Çerez katmanı (Chrome/Edge DPAPI + AES-GCM) — Faz 6/7

**Gizlilik uyarısı:** bu adım GERÇEK tarayıcı oturum çerezlerini okur. Otonom oturumda bilinçli olarak
çalıştırılmadı. Yalnızca kullanıcı bilinçli onayıyla, kendi makinesinde. Çözülen çerez değerleri
loglanmamalı/dışarı verilmemeli.

### 4.1 v10 çerez çözme — canlı (Chrome/Edge)
- (a) Chrome veya Edge'de bir `web` stratejili sağlayıcıda açık oturum. **Tarayıcıyı kapat** (SQLite
  kilidi sorunları için; katman salt-okunur açsa da temiz).
- (b) `WindowsCookieStore` ile ilgili domain için `GetCookieHeader(domain)` çağıran bir doğrulama
  yolu çalıştır (exe olarak). Çıktıda **çerez değeri değil**, yalnızca "N çerez çözüldü" gibi özet olmalı.
- (c) `Local State`'ten AES anahtarı DPAPI ile çözülür; v10 çerezler AES-256-GCM ile çözülür; domain
  eşleşen çerezlerden `name=value; ...` header üretilir. Değer **loglanmaz**.
- (d) Hata -> `WindowsCookieStore` (Local State parse, `ProtectedData.Unprotect`, GCM çözme). SQLite
  kilitliyse tarayıcıyı kapat. Yanlış anahtar -> GCM reddi (null) beklenen davranış.

### 4.2 web stratejili sağlayıcının uçtan uca gerçek verisi (çerez -> JS eklenti)
- (a) 4.1 çalışıyor; ilgili `web` sağlayıcı eklentisi (`.js`).
- (b) `HttpJsHostBridge`'in `cookieResolver`'ı `WindowsCookieStore`'a bağlı olarak eklentiyi çalıştır
  (`ctx.browser.cookieHeader(domain)`).
- (c) Eklenti gerçek oturum çereziyle sağlayıcıya bağlanıp kullanım verisi döndürür; kimlik maskeli.
- (d) Hata -> `HttpJsHostBridge` cookieResolver bağlama; domain eşleşmesi; oturum süresi dolmuşsa
  tarayıcıda yeniden giriş yap.

### 4.3 v20 app-bound çerez — **BU TURDA KAPSAM DIŞI (bilinen borç)**
- [ ] ~~v20 app-bound çerezlerin SYSTEM DPAPI katmanının canlı çözümü~~
- **Neden kapsam dışı:** app-bound anahtarın SYSTEM DPAPI katmanı yalnızca SYSTEM bağlamında çalışan
  COM `IElevator` sunucusuyla açılır; yükseltme ister, otonom/normal oturumda çalıştırılmaz.
  Çözülemezse `null` döner ve v20 çerezleri **sessizce atlanır** (v10 yolu etkilenmez).
- **Bu turda test edilecek tek şey:** v20 çerezinin varlığında uygulamanın **çökmediğini** ve v10
  çerezlerin çalışmaya devam ettiğini doğrula (graceful skip). Kaynak: 06 belgesi, decisions.md 2026-08-07.

---

## 5. Push bildirimi (host -> telefon) — Faz 7 gerçek teslim

README'deki "gerçek `.p8`/service account ile cihaza push teslimi" maddesi. Kimlik bilgisi yoksa
dispatcher loga düşer (boru hattı yine uçtan uca doğrulanabilir).

### 5.1 Kimlik-bilgisiz boru hattı (regresyon — LoggingPushDispatcher)
- (a) APNs/FCM env değişkenleri **ayarsız**; host `--push` açık, `--source fake`.
- (b) Bir cihaz kaydet: `curl -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json"
  -d '{"token":"test","platform":"apns"}' http://127.0.0.1:<port>/dashboard/v1/devices`. Eşik geçişi
  üretecek şekilde kota değerini tetikle (veya eşik geçişi üreten iki snapshot).
- (c) Eşik geçişinde `LoggingPushDispatcher` **loga** bir push olayı yazar (gerçek ağ yok). İlk
  snapshot'ta bildirim yağmuru yok. Aynı dedupeKey cooldown süresi (30 dk) içinde tekrar gitmez.
- (d) Hata -> `NotificationEngine.Diff`, `PushNotificationService`, `CompositePushDispatcher`,
  `JsonFileDeviceRegistry`. Cihaz kaydı `%LOCALAPPDATA%\CodexBridge\devices.json`'da olmalı.

### 5.2 APNs gerçek teslim (iOS)
- (a) `.p8` anahtar dosyası + `CODEXBRIDGE_APNS_KEY_ID`, `_TEAM_ID`, `_BUNDLE_ID`, `_P8_PATH`,
  (sandbox için) `_SANDBOX`. Gerçek iOS cihazda kurulu, push izinli app kabuğu; cihaz token'ı kayıtlı.
- (b) Host'u APNs env ile başlat, iOS cihazdan `POST /dashboard/v1/devices` ile gerçek APNs token'ını
  kaydet. Bir kota eşik geçişi tetikle.
- (c) iOS cihazda **push bildirimi görünür**. Host logunda APNs HTTP/2 200. ES256 JWT ~40 dk cache'lenir.
- (d) Hata -> `Push/ApnsPushDispatcher`, `Push/Jwt.cs` (ES256, IeeeP1363 ham R||S — DER değil, bkz.
  patterns.md). 403/BadDeviceToken -> sandbox/prod uyumsuzluğu (`_SANDBOX`). 410 -> cihaz otomatik kayıttan düşer.

### 5.3 FCM gerçek teslim (Android)
- (a) FCM service account JSON + `CODEXBRIDGE_FCM_SERVICE_ACCOUNT` yolu. Gerçek Android cihazda kurulu,
  FCM token üretmiş app kabuğu.
- (b) Host'u FCM env ile başlat, Android cihazdan gerçek FCM token'ını kaydet. Eşik geçişi tetikle.
- (c) Android cihazda **push görünür**. Host logunda FCM 200. RS256->OAuth2 access token ~55 dk cache.
- (d) Hata -> `Push/FcmPushDispatcher`, `Push/Jwt.cs` (RS256/PKCS1, OAuth2). UNREGISTERED -> cihaz
  otomatik kayıttan düşer. 401 -> service account/OAuth.

### 5.4 Cihaz kaydı güvenliği + ölü cihaz temizliği
- (a) 5.2 veya 5.3 kurulu.
- (b) Geçersiz/eski bir token kaydet; DELETE ile kaldırmayı dene; snapshot ve logları push token'ı
  için tara.
- (c) `DELETE /dashboard/v1/devices` token'ı siler. Ölü token (APNs 410 / FCM UNREGISTERED) otomatik
  düşürülür. Push token'ı **snapshot'a/loga sızmaz** (yalnızca devices.json'da).
- (d) Hata -> `JsonFileDeviceRegistry` (atomik yazım), `PushNotificationService` temizlik, maskeleme.

---

## 6. Telefon istemcileri (widget derlemesi + bağlanma) — Faz 4

README'deki "telefon cihazda derlenmesi" maddesi. **Bu Windows ortamında derlenemez** -> ayrı araç zinciri.

### 6.1 iOS widget derleme + bağlanma
- (a) macOS + Xcode; `phone/ios/`.
- (b) Xcode'da aç, derle, gerçek iOS cihaza dağıt. Widget'ı dashboard/v1 host'una (LAN IP + bearer) yönlendir.
- (c) Widget derlenir; snapshot'ı çeker; kullanım pill'lerini gösterir; **resetAt'e hizalı** timeline ile
  güncellenir; çok-host verisini birleştirir (maliyet toplanır, kota tekilleştirilir).
- (d) Hata -> `phone/ios/` DashboardClient/TimelineProvider; host erişimi için bölüm 2.3 (LAN bind).

### 6.2 Android widget derleme + bağlanma
- (a) Android Studio + SDK; `phone/android/`.
- (b) Derle, gerçek Android cihaza kur. Host'a (LAN IP + bearer) yönlendir.
- (c) Glance widget derlenir; WorkManager ile periyodik günceller; "veri yaşı" göstergesi doğru;
  Data Saver/pil kısıtı altında son bilinen durumu gösterir.
- (d) Hata -> `phone/android/` Glance/WorkManager; kotlinx.serialization model uyumu; host erişimi bölüm 2.3.

### 6.3 Push kayıt akışının uygulama kabuğuna bağlanması
- (a) 6.1/6.2 + bölüm 5 kimlik bilgileri.
- (b) iOS `didRegisterForRemoteNotifications` / Android `FirebaseMessagingService.onNewToken`
  callback'lerinden gelen token'ı `POST /dashboard/v1/devices`'a gönder.
- (c) Token host'a kaydolur; bölüm 5.2/5.3 teslimleri bu cihaza ulaşır.
- (d) Hata -> `phone/*` push kayıt kodu; token boş/gecikmeli geliyorsa OS izin akışı.

---

## Kapsam dışı / bilinen borçlar (bu turda test EDİLMEZ)

- [ ] **v20 app-bound çerez SYSTEM DPAPI katmanı** — COM `IElevator` (SYSTEM bağlamı) gerektirir; gelecek
  tur. (Sadece graceful-skip doğrulaması yapılır — bkz. 4.3.)
- [ ] **Faz 2 canlı Win-CodexBar entegrasyonu** — Win-CodexBar (Rust/Tauri) bu ortamda derlenmedi;
  `WinCodexBarSource.MapUsage` eşlemesi saf fonksiyon olarak test edildi. Çalışan bir `serve` örneği
  varsa `HttpUsageSource`/adaptörle canlı denenebilir (opsiyonel, ön koşula bağlı).
- [ ] **Azure OpenAI / Doubao probe'ları** — gerçek inference isteği harcar (Doubao kotadan düşer);
  varsayılan KAPALI kalmalı, canlı testte kasıtlı açılmadıkça denenmez (00-MIMARI token/etik kuralı 1).

---

## Sonuç kaydı (test bittiğinde doldur)

- Görev çubuğu (bölüm 1): ___ / 7  geçti
- HTTP host (bölüm 2): ___ / 3
- JS sağlayıcı (bölüm 3): ___ / 4
- Çerez (bölüm 4): ___ / 2 (+ 4.3 graceful-skip)
- Push (bölüm 5): ___ / 4
- Telefon (bölüm 6): ___ / 3

**Genel karar:** GEÇTİ / GEÇMEDİ — gerekçe: ___
