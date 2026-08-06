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

## Yapı

```
src/CodexBridge.Core/      platformsuz: dashboard/v1 modelleri, AdaptiveRefresh, IUsageSource,
                           FakeUsageSource, HttpUsageSource, WinCodexBarSource (Faz 2 adaptörü)
src/CodexBridge.Host/      dashboard/v1 HTTP host (ASP.NET Core) — telefon buraya bağlanır
src/CodexBridge.Taskbar/   WinUI 3 görev çubuğu yüzeyi + parent'lama + Explorer-restart gözcüsü
src/CodexBridge.JsHost/    Faz 5: ClearScript/V8 ile üst akışın .js sağlayıcılarını çalıştırma
src/CodexBridge.SelfTest/  Core assertion konsolu (SAC dotnet test'i engellediği için)
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

## Durum

- ✅ Faz 0 — görev çubuğu tekniği bu makinede canlı kanıtlandı (WinForms spike), lisanslar MIT
- ✅ Faz 1 — WinUI yüzey + Explorer-restart gözcüsü, 0 hata derleniyor
- ✅ Faz 2 — Win-CodexBar `serve` → dashboard/v1 adaptörü (eşleme test edildi; canlı entegrasyon bekliyor)
- ✅ Faz 3 — dashboard/v1 HTTP host, **curl ile uçtan uca doğrulandı** (401 fails-closed / 200)
- ✅ Testler — self-test konsolu 22/22 geçiyor
- 🧩 Faz 4 — iOS/Android widget iskeleleri yazıldı (ilgili araç zincirinde derlenir)
- ✅ **Faz 5 — kendi sağlayıcı katmanı (ClearScript/V8): gerçek `xai.js` V8'de ÇALIŞTIRILDI** (11/11 prob)
- ⏳ **Canlı çalışma testi (kullanıcı makine başında olunca):** band gerçekten çubukta
  görünüyor mu, Explorer-restart hayatta kalma; telefon widget'larının cihazda derlenmesi
- ⬜ Faz 6–7 — çerez katmanı (DPAPI), push bildirimi; JS eklentilerin canlı sağlayıcı entegrasyonu

Ayrıntı: [docs/03-FAZ2-3-VE-TELEFON.md](docs/03-FAZ2-3-VE-TELEFON.md)
