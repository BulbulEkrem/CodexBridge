# Teknik Kararlar / Mimari (CodexBridge'e özel)

Bu ürüne özel teknik kararlar. Ürünler arası paylaşılanlar için `urun_claude/.claude/knowledge/decisions.md`.

Giriş formatı: `## YYYY-MM-DD — <aşama>` başlığıyla kısa maddeler.

## 2026-08-06 — Faz 0 (görev çubuğu spike)
- **Görev çubuğu tekniği:** Pencereyi `Shell_TrayWnd`'e `SetParent` ile çocuk yap (WS_POPUP çıkar, WS_CHILD ekle), ReBar'a göre konumla, içerik boyutuna clip. Deskband11'den (MIT) uyarlandı. WinForms spike'ında bu makinede CANLI doğrulandı (pencere gerçekten çubuğun çocuğu, x=12'de görünür).
- **Explorer-restart:** Deskband11 bunu ÇÖZMÜYOR (child parent'la ölür → XAML çöker → uygulamayı kapatıyor). Kararımız: görev çubuğuna parent'lanmamış üst-seviye gizli "gözcü" pencere `TaskbarCreated` broadcast'ini dinler, band'ı sıfırdan yeniden kurar. `TaskbarWatchdog` + `App.OnTaskbarRecreated`.
- **Lisans:** Hem Deskband11 hem Win-CodexBar **MIT** → sarmalama/uyarlama serbest (Faz 2 açık sorusu kapandı).
- **Araştırma düzeltmesi:** Win-CodexBar'ın HTTP `serve`'ü VAR (`rust/src/cli/serve.rs`: `/health`,`/usage`,`/cost`, bearer token, allow-plain-http) — araştırma "yok" demişti. Ama `/dashboard/v1/snapshot` YOK. Sonuç: Faz 3 sıfırdan değil; Win-CodexBar serve + ince çeviri adaptörü kısayolu mümkün.

## 2026-08-07 — Faz 1 CANLI TEST (kullanıcı makinesinde doğrulandı)
- **Explorer-restart hayatta kalma GEÇTI** ✅ — Deskband11'in çözemediği risk. Gözcü (üst-seviye pencere, TaskbarCreated dinler) tetiklenip band'ı yeni Shell_TrayWnd'e otomatik yeniden parent'lıyor; app süreci hayatta kalıyor.
- **BULUNAN HATA 1 — krom:** `ExtendsContentIntoTitleBar=true` tek başına WinUI caption düğmelerini (min/büyüt/kapat) kaldırmıyor. Çözüm: `OverlappedPresenter.SetBorderAndTitleBar(false,false)` (+ IsMaximizable/Minimizable=false) VE Win32 `WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MIN/MAXBOX` stillerini SetParent sırasında sıyır.
- **BULUNAN HATA 2 — Explorer-restart çökmesi:** gözcü tetikleniyordu ama `OnTaskbarRecreated`'ta ölmüş band penceresine `Window.Close()` çağrısı YAKALANAMAYAN native segfault (exit 139) veriyordu (try/catch işe yaramaz). Çözüm: ölmüş pencereye Close() ÇAĞIRMA — referansı null'la, doğrudan yeni pencere kur. Stack: `Microsoft.UI.Xaml.Window.Close() ← OnTaskbarRecreated`.
- Band genişliği 160→280 dip (üç pill sığsın).

## 2026-08-06 — Faz 1 (WinUI yüzey)
- **Yığın:** .NET 9 / WinUI 3 unpackaged (`WindowsPackageType=None`), WinUIEx, el yazımı P/Invoke (CsWin32 yerine — öngörülebilir derleme). Windows 11 SDK 26100 kuruldu.
- **Katman ayrımı:** `CodexBridge.Core` (net9.0, platformsuz) modelleri + AdaptiveRefresh + IUsageSource taşır; `CodexBridge.Taskbar` (WinUI) yalnızca sunum + parent'lama. Veri kaynağı `IUsageSource` arkasında (Faz1 sahte → Faz2 Win-CodexBar → Faz5 kendi katman).

## 2026-08-06 — Faz 2/3 (host + adaptör, otonom)
- **Faz 3 host CANLI doğrulandı:** `CodexBridge.Host` (ASP.NET Core minimal API) `/health` + `/dashboard/v1/snapshot` (bearer, fails-closed, no-store, sabit zamanlı SHA-256). curl ile 401/200 teyit edildi. Yapılandırma env + CLI; loopback dışı bind token + `--allow-plain-http` şart.
- **Faz 2 adaptör:** `WinCodexBarSource.MapUsage` Win-CodexBar `/usage` (primary/secondary rate window, snake_case) → dashboard/v1. Saf fonksiyon, örnek JSON ile test edildi. Canlı entegrasyon beklemede (Win-CodexBar Rust bu ortamda derlenmedi).
- **`HttpUsageSource`:** dashboard/v1 host'undan okuyan istemci — görev çubuğu "host'tan oku" modu + telefon + çok-makine birleştirmenin ortak çekirdeği. Çok-makine: maliyet toplanır, kota (aynı id) tekilleştirilir.
- **Telefon:** iOS (WidgetKit, resetAt'e hizalı timeline) + Android (Glance+WorkManager, "veri yaşı") iskeleleri `phone/`. Derleme ilgili araç zincirinde (bu ortamda yok).

## 2026-08-07 — Faz 7 (push bildirimi + v20 çerez, otonom)
- **Mimari:** `NotificationEngine.Diff` (Core, saf) iki snapshot'tan eşik geçişlerini üretir → `PushNotificationService` (BackgroundService) tek yenileme noktasından (SnapshotCache) periyodik çeker, cooldown'lu fan-out yapar → `CompositePushDispatcher` cihaz platformuna göre APNs/FCM'e yönlendirir. Katman ayrımı: olay modeli+motor Core'da (platformsuz, test edilebilir), HTTP dispatcher'lar Host'ta.
- **Push token güvenliği:** cihaz kayıtları yalnızca `%LOCALAPPDATA%\CodexBridge\devices.json`'da; snapshot'a/loga sızmaz (telefona maskeli snapshot gider, çerez/kimlik PC'de kalır ilkesiyle tutarlı). Kayıt uç noktaları bearer korumalı.
- **APNs/FCM gerçek ama opsiyonel:** ES256 (APNs provider token, ~40dk cache) ve RS256→OAuth2 (FCM, ~55dk cache) tam implemente; kimlik bilgisi (env) yoksa loga düşer. Ağ yalnızca yapılandırılınca. Ölü cihaz (APNs 410 / FCM UNREGISTERED) otomatik kayıttan düşer.
- **Kaynak seçimi bağlandı:** Host artık `--source fake|http` ile FakeUsageSource veya HttpUsageSource (başka dashboard/v1 host'undan oku) seçer; önceden FakeUsageSource'a sabitti.
- **v20 app-bound çerez:** çözülen düz metnin ilk 32 baytı app-bound başlık → sıyrılır (`IsV20` ile sürüm ayrımı, ayrı app-bound anahtar). `AppBoundKeyProvider` `app_bound_encrypted_key`'i okuyup "APPB" önekini atar, kullanıcı DPAPI katmanını soyar. **SINIR:** SYSTEM DPAPI katmanı COM `IElevator` (SYSTEM bağlamı) gerektirir → otonom çözülmez, null döner, v10 etkilenmez.

## 2026-08-06 — Faz 5 (JS sağlayıcı katmanı, otonom, KANITLANDI)
- **Araştırma #4 açık sorusu cevaplandı:** üst akışın `.js` sağlayıcıları JavaScriptCore'a bağlı DEĞİL — gerçek `xai.js` prelude ile **ClearScript/V8**'de çalıştırıldı, doğru çıktı (11/11 prob). "15 JS sağlayıcı bedava" bahsi doğrulandı.
- **ClearScript gotcha:** `[ScriptMember("http")]` ile host nesnesi metotları `host.http is not a function` verdi. ÇÖZÜM: `host`'u JS tarafında kurup C# delegate'lerine bağla (`__hostHttp` vb.). Delegate yolu sağlam.
- **Sonuç marshalling:** ScriptObject dolaşmak yerine JS'de `JSON.stringify(result)` → C#'ta System.Text.Json parse (Date'ler otomatik ISO string). Çok daha sağlam.
- **Async:** mock/senkron host.http ile promise'ler `engine.Execute` sonrası microtask drenajında çözülür; ayrı event loop gerekmez. `V8ScriptEngineFlags.EnableDateTimeConversion`.
- ClearScript.V8 7.4.5 + Native.win-x64; native Microsoft-imzalı olduğundan SAC'a takılmaz (kendi apphost exe'mizden yüklenir).

## 2026-09-04 — Bağımsız veri katmanı + Windows yüzeyleri
- **Kapsam kararları (kullanıcı):** iki abonelik (Claude + Codex), her birinde oturum + haftalık
  pencere; band **B varyantı** (sağlayıcı başına tek pill, ikişer çubuk); veri katmanı **bağımsız**
  (Win-CodexBar'a bağlanmıyoruz); Windows önce; sparse package **evet**; yüzeyler =
  band + tepsi + bildirim + widget + minimal ayarlar.
- **Token yakma sorusu kapandı:** Claude ve Codex uç noktaları yalnızca GET; inference isteği yok,
  abonelik kotasından düşmüyor. Karşı örnek gerçek: Win-CodexBar'da Azure OpenAI (`azureopenai.rs:237`)
  ve Doubao (`doubao/mod.rs:166`) `max_tokens:1` ile gerçek istek atıyor. Bizimkiler o kategoride değil.
  **Ama** uç noktanın kendi hız sınırı var (Claude tarafında 429 + Retry-After işleme kodu duruyor),
  bu yüzden adaptif aralık korunuyor, sabit 2 dk yapılmıyor.
- **Token geri yazma YOK:** Claude token'ı yenileniyor ama sonuç yalnızca kendi DPAPI korumalı
  önbelleğimize yazılıyor. `~/.claude/.credentials.json`'a yazsaydık Claude Code'un eşzamanlı
  yenilemesiyle birbirimizin token'ını ezerdik. Codex tarafında yenileme hiç yapılmıyor —
  onu CLI kendisi yapıyor (Win-CodexBar da refresh_token'ı bilerek saklamıyor).
- **Süreçler arası paylaşım = dosya, pipe değil:** widget sağlayıcısı Widgets host tarafından pano
  açılınca uyandırılıp kapanınca öldürülüyor. Pipe için bizim sürecin ayakta olması gerekirdi;
  kullanıcı band'ı kapattıysa widget boş kalırdı. Atomik yazılan `snapshot.json` her koşulda okunur.
  Şema `dashboard/v1` — dosya, widget, telefon ve HTTP host tek şema konuşuyor.
- **En kısıtlayıcı pencere kuralı:** band pill'inin rengi ve tepsi ikonunun alarmı
  `MostRestrictive()`'ten geliyor, ilk pencereden değil. Aksi halde Codex'in haftalığı %91 iken
  band oturum %18'i gösterip susardı.
- **HICON üretimi CreateDIBSection ile:** `CreateBitmap` cihaz bağımlı bitmap üretiyor ve satır
  sırası garanti değil. İkonda üst çubuk oturum, alt çubuk haftalık — ters çevrilmiş bir ikon iki
  kotayı sessizce takas ederdi. Negatif `biHeight` sırayı garanti ediyor.
- **Widget'ta metin ölçer:** Adaptive Cards'ta yerel ilerleme çubuğu yok. Sütun genişliği + arka
  plan stiliyle taklit sürümler arası oynak; `TextBlock.color` (good/warning/attention) güvenilir.
  Ölçer blok karakterlerle çiziliyor, renk oradan geliyor. Aynı JSON Start companion'a da verilebilir.
- **Ortam notu:** bu oturum Linux konteynerde geçti. .NET 9 SDK kuruldu; Core/SelfTest/Host/
  ClaudeData/JsHost derlendi ve 147 assertion çalıştırıldı. WinUI ve WindowsAppSDK kodu Linux'ta
  derlenemez — Roslyn ile sözdizimi ayrıştırması temiz ama **Windows'ta derlenmedi ve çalıştırılmadı.**
