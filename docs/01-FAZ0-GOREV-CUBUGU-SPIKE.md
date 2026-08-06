# Faz 0 — Görev Çubuğu Tekniği Spike'ı ve Gitme/Gitmeme Kararı

> Durum: **devam ediyor** · Tarih: 2026-08-06
> Kaynaklar: `zadjii/Deskband11` (MIT), `Finesssee/Win-CodexBar` (MIT), üst akış `steipete/CodexBar`
> Bu faz araştırmanın #1 açık sorusunu kapatıyor: **parent'lama tekniği gerçekte nasıl çalışıyor?**

## Ön karar: **GO (koşullu)**

Teknik gerçek, MIT lisanslı ve doğrudan C#'a uyarlanabilir. **Tek gerçek engel** araştırmanın
öngördüğü yerde çıktı: **Explorer yeniden başladığında yüzeyin hayatta kalması.** Referans
uygulama bunu *çözmüyor* — açıkça pes edip kendini kapatıyor. Bu bizim çözmemiz gereken
somut mühendislik işi; teknik bir imkânsızlık değil.

---

## 1. Teknik gerçekte nasıl çalışıyor (kod okundu)

`Deskband11/MainWindow.xaml.cs` · `MoveToTaskbar()` (satır 171-217). Adımlar:

```
1. FindWindow("Shell_TrayWnd")                    → görev çubuğu penceresi
2. FindWindowEx(taskbar, "ReBarWindow32")         → çubuğun içerik bandı (konum referansı)
3. GetWindowLong(GWL_STYLE): WS_POPUP çıkar, WS_CHILD ekle
4. SetParent(bizimPencere, taskbarWindow)         → pencere artık çubuğun ÇOCUĞU
5. GetWindowRect(taskbar) + GetWindowRect(rebar)  → yeni konum/boyut hesapla
6. SetWindowPos(..., SWP_FRAMECHANGED|SWP_NOACTIVATE)
7. SetWindowRgn(hwnd, clipRegion)                 → içerik boyutuna kırp (diğer çubuk öğelerine değme)
```

**Kritik içgörü doğrulandı:** pencere çubuğun *üstünde yüzen* bağımsız bir katman değil,
`SetParent` ile çubuğun **çocuğu**. Bu sayede z-order kavgası yok, çubukla birlikte hareket
ediyor. "Naif always-on-top" yaklaşımının sorunları burada baştan çözülü. (Araştırmadaki
`03` raporunun beyanı kodla teyit edildi.)

**Boş sol alan sorusu çözülü:** `UpdateTaskbarButtons()` (satır 220-285) Windows logosu
(60px) + görev çubuğu düğmelerinin en-sağı + bildirim alanı genişliğini ölçüp
`forContent = available - reserved` hesaplıyor. Ortalanmış Win11 çubuğunda bile kullanılabilir
alanı dinamik buluyor. WidBar'ın "boş alan tespiti" için model almamıza gerek yok, bu koddan
alınabilir.

**DPI / çoklu monitör kısmen çözülü:** `CustomWndProc` (WndProc subclass'ı,
`SetWindowLongPtr(GWL_WNDPROC)`):
- `WM_DISPLAYCHANGE` → `MoveToTaskbar()` (monitör/çözünürlük değişimi)
- `WM_SETTINGCHANGE(SPI_SETWORKAREA)` → debounce'lu layout güncelleme (çubuk taşıma/boyut)
- DPI ölçek faktörü her yerde `GetDpiForWindow()/96.0` ile hesaplanıyor.

## 2. Somut risk: Explorer restart — referans uygulama ÇÖZMÜYOR

Bu, araştırmanın "yüksek olasılık / orta etki" riskinin **kod düzeyinde teyidi.** Yazarın
kendi yorumu (satır 322-336):

> *"I cannot for the life of me get the window to reparent to the new taskbar window...
> Every time we get this, the content in the ContentAreaPresenter is already null and our
> ActualWidth is now 0, and the DPI is 0... This is just not getting fixed during a hackathon"*

- `TaskbarCreated` mesajı (`RegisterWindowMessage`) alınıyor ama `CustomWndProc`'taki
  işleme **yorum satırı** (satır 139-143).
- `Receive(TaskbarRestartMessage)` handler'ı yeniden parent'lamayı deniyor, başaramıyor,
  sonunda sadece `this.Close()` çağırıyor — yani **uygulama ölüyor.**
- `WM_DESTROY` da yeniden ele geçirilmek isteniyor ama "XAML UI ağacını söküyor" notuyla
  bırakılmış (satır 130-138).

**Sonuç:** Deskband11'i "hazır çözüm" gibi kabul edersek, kullanıcı Explorer'ı her yeniden
başlattığında (veya bazı Windows Update'lerinde) ölm çubuk kaybolur. Ürün kalitesi için
bu **kabul edilemez** ve **Faz 0'ın çözmesi gereken tek şey.**

### Çözüm hipotezi (Faz 1'de doğrulanacak)
- Yeniden parent'lamanın başarısız olması, `Close()` sonrası XAML ağacının çökmesindendir.
  Alternatif: `TaskbarCreated` alındığında pencereyi kapatmak yerine **kısa gecikmeyle
  yeni bir görev çubuğu HWND'sine `SetParent` + tam layout yeniden kurulumu** (yeni bir
  pencere/XAML kaynağıyla). Bir **watchdog** süreci, ölm yüzeyi tespit edip yeniden
  başlatabilir (Win-CodexBar'ın `browser/watchdog.rs` deseni gibi ayrı bir gözcü).
- Kesin yol Faz 1 spike'ında ölçülecek.

## 3. Lisans ve araç zinciri — açık sorular kapandı

| Açık soru (plandan) | Cevap | Kaynak |
|---|---|---|
| Deskband11 tekniği gerçek mi, uyarlanabilir mi? | **Evet, MIT** | `LICENSE.md` (MIT, Microsoft) + kod okundu |
| Win-CodexBar lisansı sarmalamaya izin veriyor mu? (Faz 2) | **Evet, MIT** | `LICENSE` (MIT, Peter Steinberger 2025) |
| Araç zinciri var mı? | **Evet** | .NET 10.0.302 SDK + VS 18.8; Deskband11 net9.0-windows, WindowsAppSDK 1.8 |

## 4. Araştırmaya iki düzeltme (kod okumasıyla)

Araştırma çoğunlukla doğru, ama iki noktada güncelleme gerekti:

### 4.1 Win-CodexBar'ın HTTP host'u **VAR** (araştırma "yok" demişti)
`Win-CodexBar/rust/src/cli/serve.rs` (623 satır) — `TcpListener::bind`, bearer token
(`CODEXBAR_DASHBOARD_TOKEN`, SHA-256 digest, sabit zamanlı), `--allow-plain-http`,
loopback dışı bind koruması, `refresh-interval` önbelleği. Uç noktalar:

```
GET /health   → {"status":"ok",...}
GET /usage    → ham kullanım (bearer gated)
GET /cost     → maliyet (bearer gated)
```

README'deki "loopback integrations" tam olarak bu. **Ama `/dashboard/v1/snapshot` YOK** —
bu, üst akışın daha yeni, telefon-dostu (maskeli kimlik, `staleAfterSeconds`, versiyonlu)
sözleşmesi. Win-CodexBar upstream 0.44 #2227 seviyesindeki basit serve'de kalmış.

**Yol haritasına etkisi:** Faz 3 artık "sıfırdan HTTP host" değil. İki daha ucuz seçenek açıldı:
- **3-A (hızlı):** Win-CodexBar'ın `serve`'ünü çalıştır, önüne `/usage`+`/cost` → `dashboard/v1`
  şemasına çeviren ince bir C# adaptörü koy. Telefon `dashboard/v1` görür, veriyi Rust host üretir.
- **3-B (bağımsız):** Kendi ASP.NET Core host'umuzda `dashboard/v1`'i baştan sun (plandaki yol).

### 4.2 "Telefona veri servisi Windows'ta imkânsız" hükmü yumuşadı
Yukarıdakinin sonucu: Windows'ta bearer korumalı HTTP kullanım verisi **bugün mümkün**.
Eksik olan yalnızca versiyonlu snapshot şeması + widget'a özel alanlar (renk, sıralama,
maskeleme). Bu, telefon işini (Faz 4) düşündüğümüzden erken başlatılabilir kılıyor.

## 5. Canlı spike durumu

### 5.1 Deskband11 (WinUI 3) — bu makinede DERLENMEDİ
`dotnet build -p:WindowsPackageType=None` → NuGet restore başarılı, ama derleme başarısız:

```
error : Could not find the Windows SDK in the registry
  (cswinrt.exe ve XamlCompiler.exe hata 1 ile çıktı)
```

**Bulgu:** VS 18.8 workload manifest'leri Windows SDK'ya referans veriyor ama asıl Windows
10/11 SDK bileşeni kayıtlı/kurulu değil. **Bu makinede WinUI 3 derlemesi şu an bloke.**
Gerçek geliştirmeden önce Windows SDK kurulmalı (VS Installer → "Windows 11 SDK").

### 5.2 Salt Win32/WinForms spike — DERLENDİ ve ÇALIŞTI ✅
Tekniğin çekirdeği WinUI'ye bağlı olmadığından, Windows SDK gerektirmeyen minimal bir
WinForms spike'ı yazıldı: `spikes/taskbar-parenting/` (`Program.cs`, ~120 satır).
`FindWindow(Shell_TrayWnd)` → `WS_CHILD` + `SetParent` + `SetWindowPos`.

**Derleme:** `dotnet build` başarılı (Windows SDK'sız, 4 sn).
**Çalıştırma + programatik doğrulama** (EnumChildWindows ile Shell_TrayWnd'in çocukları tarandı):

```
spike penceresi  : hwnd=7799200  class=WindowsForms10.Window...
parent           : Shell_TrayWnd  (PARENT == TASKBAR ✅)
visible          : True
ekran konumu     : [12, 1032, 162, 1080]  → görev çubuğunun İÇİNDE, en solda, 150x48
```

> **Teknik bu Windows 11 makinesinde canlı kanıtlandı.** Pencere gerçekten görev çubuğunun
> çocuğu, görünür ve ortalanmış çubuğun boş sol alanına (x=12) oturuyor. `Process.MainWindowHandle`
> = 0 dönüyor — bu beklenen: `WS_CHILD` olan pencere artık top-level "main window" sayılmıyor.

**Henüz canlı test edilmedi:** Explorer restart hayatta kalma (spike'ın `WM_TASKBARCREATED`
handler'ı yeniden parent'lamayı deniyor — referansın pes ettiği yer; `explorer.exe` yeniden
başlatmak kullanıcının oturumunu böldüğü için onay bekliyor), çoklu monitör, otomatik gizleme.

## 6. Faz 0 kapanış kriterleri (kontrol listesi)

| Kontrol | Durum | Not |
|---|---|---|
| Parent'lama tekniği kod düzeyinde anlaşıldı | ✅ | `MoveToTaskbar` okundu |
| MIT lisansları doğrulandı (Deskband11 + Win-CodexBar) | ✅ | Sarmalama serbest |
| Araç zinciri mevcut | ✅ | .NET 10 SDK + VS 18.8 |
| **Parent'lama tekniği CANLI çalışıyor (bu makine)** | ✅ | WinForms spike, Shell_TrayWnd çocuğu, doğrulandı |
| Ortalanmış çubukta boş sol alan | ✅ | Spike x=12'de görünür oturdu |
| Çoklu monitör / DPI mekanizması | ✅ (kodda) | WndProc canlı test bekliyor |
| **Explorer restart hayatta kalma** | ⏳ | Referans çözmüyor; Faz 1'de gözcü mekanizması yazıldı, canlı test bekliyor |
| WinUI 3 derlemesi (asıl yığın) | ✅ | Windows 11 SDK 26100 kuruldu (winget); Deskband11 + CodexBridge.Taskbar 0 hata derleniyor |
| Windows Update sonrası | ⬜ | Sahada gözlemlenecek |

**Not:** Windows 11 SDK 10.0.26100 `winget install Microsoft.WindowsSDK.10.0.26100` ile kuruldu
(VS Installer'daki asıl SDK bileşeni eksikti). Bu, WinUI 3 derleme engelini kaldırdı.

## 7. Karar

**GO** — teknik uygulanabilir ve lisanslar uygun. Faz 1'e geçilebilir, **tek şartla:**
Faz 1'in ilk işi, sahte veriyle çalışan minimal WinUI 3 yüzeyi *değil*, **Explorer-restart
hayatta kalma mekanizması** olmalı. Bu çözülmezse plan tepsi ikonuna düşer (araştırmadaki
yedek). Ama artık biliyoruz ki: HTTP host tarafı (Win-CodexBar serve) zaten yarı hazır,
yani "tepsi ikonu + telefon" fallback ürünü bile hızlı ulaşılabilir.
