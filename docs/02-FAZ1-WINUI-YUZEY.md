# Faz 1 — WinUI 3 Görev Çubuğu Yüzeyi (sahte veri)

> Durum: **kod yazıldı, derleniyor (0 hata) · canlı çalışma testi bekliyor**
> Tarih: 2026-08-06

## Ne yapıldı

Faz 1'in çıktısı: görev çubuğunda çalışan, sahte veriyle sağlayıcı kullanım yüzdelerini
gösteren WinUI 3 yüzeyi + Deskband11'in çözemediği **Explorer-restart hayatta kalma**
mekanizması. İki proje:

### `src/CodexBridge.Core` (net9.0 — Windows SDK gerektirmez)
| Dosya | İçerik |
|---|---|
| `Dashboard/DashboardSnapshot.cs` | `dashboard/v1` şemasının birebir C# karşılığı (host↔telefon sözleşmesi) |
| `Refresh/AdaptiveRefresh.cs` | Üst akışın 2–30 dk adaptive yenileme karar tablosu (saf, bağımsız) |
| `Sources/IUsageSource.cs` | Veri kaynağı soyutlaması (Faz 1 sahte → Faz 2 Win-CodexBar → Faz 5 kendi katman) |
| `Sources/FakeUsageSource.cs` | Faz 1 sahte veri (codex/claude/gemini, oynayan yüzdeler) |

### `src/CodexBridge.Taskbar` (WinUI 3, unpackaged, x64)
| Dosya | İçerik |
|---|---|
| `Taskbar/TaskbarHost.cs` | Pencereyi `Shell_TrayWnd`'e parent'lama (Deskband11'den uyarlandı, MIT) |
| `Taskbar/TaskbarWatchdog.cs` | **Explorer-restart hayatta kalma** — görev çubuğuna parent'lanmamış üst-seviye gözcü pencere, `TaskbarCreated` broadcast'ini dinler |
| `Interop/NativeMethods*.cs` | El yazımı P/Invoke (parent'lama + subclass + gözcü pencere) |
| `App.xaml.cs` | Band'ı ve gözcüyü sahiplenir; gözcü tetiklenince band'ı SIFIRDAN yeniden kurar |
| `MainWindow.xaml[.cs]` | Band UI + WndProc subclass (DPI/çözünürlük/çalışma alanı değişiminde yeniden konumlan) |

## Explorer-restart çözümü — Deskband11 üzerine katkımız

**Sorun:** Band penceresi görev çubuğunun `WS_CHILD`'ı olduğundan, Explorer çöküp
`Shell_TrayWnd` yok olunca band da ölür (child parent'la birlikte ölür) ve WinUI XAML ağacı
çöker. Deskband11 burada uygulamayı kapatıyor.

**Çözüm (`TaskbarWatchdog`):** Band'a güvenmeyen, görev çubuğuna **parent'lanmamış**,
üst-seviye gizli bir gözcü pencere. Üst-seviye pencereler `TaskbarCreated` broadcast'ini
alır (çocuk pencereler almaz). Explorer yeni çubuğu yaratınca gözcü tetiklenir; uygulama
süreci canlı olduğu için band SIFIRDAN yeniden kurulup yeni çubuğa parent'lanır
(`App.OnTaskbarRecreated`, 500 ms gecikmeyle).

## Derleme

```
dotnet build CodexBridge.slnx -c Debug        → 0 hata (Core + Taskbar)
```
Not: `dotnet new sln` bu ortamda `.slnx` (yeni XML formatı) üretti. Taskbar yalnızca
x64/ARM64 hedefler; çözümü platform override'sız derleyin (her proje kendi platformunu kullanır)
ya da doğrudan `dotnet build src/CodexBridge.Taskbar/CodexBridge.Taskbar.csproj -p:Platform=x64`.

## CANLI TEST SONUÇLARI (2026-08-07, kullanıcı makinesinde)

- [x] **Band görev çubuğunda görünüyor** ✅ — WinUI penceresi `Shell_TrayWnd` çocuğu, x=12, üç sağlayıcı.
- [x] **Explorer-restart hayatta kalma** ✅ — **asıl sınav geçti.** `explorer.exe` öldürülüp
  yeniden başlatıldığında app süreci hayatta kaldı, gözcü tetiklendi, band YENİ görev çubuğuna
  otomatik yeniden parent'landı. **Deskband11'in çözemediği şey artık çalışıyor.**
- [x] **Pencere kromu kaldırıldı** ✅ — ilk sürümde WinUI başlık çubuğu min/büyüt/kapat düğmeleri
  görünüyordu; `OverlappedPresenter.SetBorderAndTitleBar(false,false)` + Win32 `WS_CHROME` sıyırma ile giderildi.

### Canlı testte bulunan iki hata ve düzeltmeleri
1. **Başlık çubuğu düğmeleri görünüyordu** → `ExtendsContentIntoTitleBar` tek başına yetmedi;
   `OverlappedPresenter.SetBorderAndTitleBar(false,false)` + caption stillerini Win32'de sıyırma.
2. **Explorer-restart'ta çökme (segfault)** → gözcü tetikleniyordu ama ölmüş band penceresine
   `Window.Close()` çağrısı yakalanamayan native çökmeye yol açıyordu. Düzeltme: ölmüş pencereye
   `Close()` ÇAĞIRMA — referansı bırak, doğrudan yenisini kur.

## HENÜZ DOĞRULANMADI
- [ ] Çoklu monitör / farklı DPI'da konum
- [ ] Görev çubuğu otomatik gizlemede band birlikte gizleniyor mu

## Sıradaki (Faz 2)

Sahte veriyi gerçek veriyle değiştir: **Win-CodexBar `serve`** (`/usage`, `/cost`) çıktısını
`dashboard/v1` şemasına çeviren bir `IUsageSource` adaptörü (`WinCodexBarSource`).
Win-CodexBar MIT lisanslı; sarmalama serbest.
