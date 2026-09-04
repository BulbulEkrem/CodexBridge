# CodexBridge

Windows görev çubuğunun **içinde** her an görünür bir AI kullanım ölçeri + telefonun
(iOS/Android widget) bağlanabileceği, Windows'ta çalışan bir **`dashboard/v1` HTTP host'u.**
Üst akış [CodexBar](https://github.com/steipete/CodexBar)'ın platformdan bağımsız veri
katmanına iki yeni yüz takar.

> **Neden bu var:** Windows'ta AI kullanım göstergesi zaten var (Win-CodexBar, tepsi ikonu).
> Hiçbir yerde olmayan iki şey: (1) görev çubuğu *içinde* kalıcı ölçer, (2) telefonu üç OS'a
> birden bağlayan HTTP host. CodexBridge bu ikisini inşa eder.

## Belgeler

| # | Doküman |
|---|---|
| 00 | [Mimari ve Yol Haritası](docs/00-MIMARI-VE-YOL-HARITASI.md) |
| 01 | [Faz 0 — Görev Çubuğu Spike'ı ve Gitme/Gitmeme Kararı](docs/01-FAZ0-GOREV-CUBUGU-SPIKE.md) |
| 02 | [Faz 1 — WinUI 3 Görev Çubuğu Yüzeyi](docs/02-FAZ1-WINUI-YUZEY.md) |
| 03 | [Faz 2-3 — Host + Adaptör + Telefon](docs/03-FAZ2-3-VE-TELEFON.md) |
| 04 | [Faz 5 — JS Sağlayıcı Katmanı](docs/04-FAZ5-JS-SAGLAYICI-KATMANI.md) |
| 05 | [Faz 6 — Çerez Katmanı](docs/05-FAZ6-CEREZ-KATMANI.md) |
| 06 | [Faz 7 — Push Bildirimi + v20 app-bound](docs/06-FAZ7-PUSH-BILDIRIMI.md) |
| 08 | [Win-CodexBar Analiz Raporu — Özellik Kaynağı](docs/08-WIN-CODEXBAR-ANALIZ-RAPORU.md) |
| 09 | [Windows Yüzey Araştırması — Bildirim / Görev Çubuğu / Start](docs/09-WINDOWS-YUZEY-ARASTIRMASI.md) |
| 10 | [Yüzen Ambient HUD — Tasarım Önerileri](docs/10-YUZEN-AMBIENT-HUD-ONERILERI.md) *(D4 uygulandı)* |

## Yapı

```
src/CodexBridge.Core/      platformsuz: dashboard/v1 modelleri, AdaptiveRefresh, IUsageSource,
                           FakeUsageSource, HttpUsageSource, WinCodexBarSource (Faz 2 adaptörü)
src/CodexBridge.Host/      dashboard/v1 HTTP host (ASP.NET Core) — telefon buraya bağlanır;
                           Faz 7 push (cihaz kaydı + APNs/FCM dispatcher + eşik servisi)
src/CodexBridge.Taskbar/   WinUI 3 görev çubuğu yüzeyi + parent'lama + Explorer-restart gözcüsü;
                           Hud/ = yüzen ambient HUD (her zaman üstte, sürüklenebilir)
src/CodexBridge.JsHost/    Faz 5: ClearScript/V8 ile üst akışın .js sağlayıcılarını çalıştırma
src/CodexBridge.SelfTest/  Core assertion konsolu (SAC dotnet test'i engellediği için)
src/CodexBridge.Widget/    Windows Widget sağlayıcısı (Adaptive Cards; paketli COM sunucusu)
src/CodexBridge.JsProbe/   Faz 5 fizibilite probu (gerçek xai.js V8'de)
spikes/taskbar-parenting/  Faz 0 kanıt spike'ı (WinForms, Windows SDK gerektirmez)
phone/android/  phone/ios/ telefon widget iskeleleri (ilgili araç zincirinde derlenir)
docs/                      mimari ve faz belgeleri
```

## Derleme

Gereksinim: .NET 9+ SDK, Windows 11 SDK 10.0.26100
(`winget install Microsoft.WindowsSDK.10.0.26100`).

```powershell
dotnet build CodexBridge.slnx -c Debug
# veya sadece uygulama:
dotnet build src/CodexBridge.Taskbar/CodexBridge.Taskbar.csproj -c Debug -p:Platform=x64
```

### Çalıştırma gereksinimi: Windows App Runtime

Bildirimler için **Windows App Runtime**'ın kurulu olması şart. NuGet paketi yetmiyor:
paketsiz (unpackaged) bir uygulamada `AppNotificationManager`'ın COM sunucusunu
**Singleton** paketi barındırıyor ve o yalnızca redistributable ile geliyor. Eksikse
kayıt `0x80040154 REGDB_E_CLASSNOTREG` ile düşer ve **hiçbir bildirim gönderilmez** —
band ve tepsi ikonu çalışmaya devam ettiği için bu sessiz bir arıza olarak görünür.

```powershell
# https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe
.\WindowsAppRuntimeInstall-x64.exe --quiet
```

Kurulu olup olmadığını doğrulamak için (yayıncı adının `MicrosoftCorporationII` olduğuna
dikkat — `Microsoft.WindowsAppRuntime.*` araması bu paketi bulmaz):

```powershell
Get-AppxPackage | Where-Object { $_.Name -like "*Singleton*" }
```

## Durum

### Bağımsız veri katmanı (bu ortamda derlendi ve test edildi ✓)
- **Claude** — `~/.claude/.credentials.json` → `api.anthropic.com/api/oauth/usage`.
  Token yenileme bizde; yenilenen token **yalnızca kendi DPAPI korumalı önbelleğimizde**,
  kullanıcının CLI dosyasına asla yazılmıyor.
- **Codex** — `~/.codex/auth.json` → `chatgpt.com/backend-api/wham/usage`.
  Token yenilenmiyor; onu CLI yapıyor, 401'de `codex login`e yönlendiriliyor.
- İkisi de yalnızca GET: **abonelik kotasından bir şey düşmüyor.**
- 429'da `Retry-After`'a uyuluyor, yoksa 2→30 dk üstel geri çekilme.
- Bir sağlayıcının hatası diğerini düşürmüyor; son bilinen değer korunuyor ve
  `updatedAt` bilerek eskide bırakılıyor ki yüzeyler "veri yaşı"nı dürüstçe göstersin.
- Tek yenileme noktası (`RefreshCoordinator`) + atomik `snapshot.json`.
- **SelfTest: 171 assertion, tümü geçiyor.**

### Windows yüzeyleri (2026-09-04'te gerçek makinede derlendi ve çalıştırıldı ✓)
- **Görev çubuğu bandı** ✅ — B varyantı: sağlayıcı başına tek pill, içinde iki çubuk
  (üst oturum, alt haftalık); pill rengi **en kısıtlayıcı** pencereden. Çubuğun gerçek
  çocuğu olduğu doğrulandı (`Shell_TrayWnd` altında, `WS_CHILD`, krom yok). Ortadaki
  kümenin sol kenarına yaslanıyor — Win11'de sabit sol ofset hava durumu widget'ının
  üstüne biniyor.
- **Tepsi ikonu** ✅ — ikon, tooltip (iki satır, en kısıtlayıcı pencere + geri sayım) ve
  sağ tık menüsü canlı doğrulandı. Windows yeni ikonları taşma menüsüne koyar.
- **Bildirim** ✅ — canlı kota kartı Bildirim Merkezi'nde doğrulandı: tek ilerleme çubuğu
  = en kısıtlayıcı pencere, diğer pencere durum satırında, geri sayım çubuğun sağında.
  **Windows App Runtime gerektiriyor** (bkz. Derleme).
- **Ayarlar penceresi** ⏳ — yazıldı, canlı açılıp kaydetmesi doğrulandı ama gözden geçirilmedi.
- **Yüzen ambient HUD** ✅ — görev çubuğunun üstünde duran, sürüklenebilir, her zaman üstte
  pencere. Saatlik ve haftalık ayrı satırlarda (yüzde + `1S:52D` geri sayımı + tam genişlik
  bar), resmî marka logolarıyla. Konum hatırlanıyor; Alt-Tab ve görev çubuğu düğmesi yok.
  Band'dan bağımsız, ayarlardan kapatılabilir. Bkz. [10](docs/10-YUZEN-AMBIENT-HUD-ONERILERI.md).
- **Widget sağlayıcısı** ⏳ — Adaptive Cards içeriği Core'da ve test edilmiş; COM host'u ve
  sparse package manifest'i yazıldı, **paketleme Windows'ta tamamlanmalı**.


### Önceki fazlar
- ✅ Faz 0/1 — görev çubuğu tekniği + Explorer-restart gözcüsü, kullanıcı makinesinde canlı doğrulandı
- ✅ Faz 3 — `dashboard/v1` HTTP host, curl ile uçtan uca doğrulandı (401 fails-closed / 200)
- ✅ Faz 5 — JS sağlayıcı katmanı (ClearScript/V8), gerçek `xai.js` çalıştırıldı
- ✅ Faz 6 — çerez katmanı kripto doğrulandı (sentetik veri; v10)
- ✅ Faz 7 — push (APNs/FCM) + v20 app-bound çerez başlık sıyırma
- 🧩 Faz 4 — iOS/Android widget iskeleleri (ilgili araç zincirinde derlenir)

### Canlı test bekleyenler (kullanıcı makinesinde)
Eşik uyarıları (kota gerçekten eşiği geçtiğinde), widget paketleme ve panoda görünme,
ayarlar penceresinin gözden geçirilmesi, ikinci monitör / farklı DPI, telefon widget'ları.

2026-09-04'te kapananlar: band ve konumlanması, tepsi ikonu + tooltip + menü, bildirim
kartı, Explorer-restart hayatta kalması, gerçek Claude/Codex kimlikleriyle uçtan uca
çekim (band'daki değerler ayrı bir süreçten yapılan bağımsız çekimle karşılaştırıldı).

Ayrıntı: [docs/07-CANLI-TEST-KONTROL-LISTESI.md](docs/07-CANLI-TEST-KONTROL-LISTESI.md)
