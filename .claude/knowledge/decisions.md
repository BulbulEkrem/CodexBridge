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
