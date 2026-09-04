# Windows Yüzey Araştırması — Bildirim, Görev Çubuğu ve Start Menü

> **Tarih:** 2026-09-04 · **Kapsam:** Windows 11'de bir kullanım ölçerinin görünebileceği
> **her** kabuk yüzeyi, API seviyesinde doğrulanmış kısıtlarıyla.
> **Devam:** `08-WIN-CODEXBAR-ANALIZ-RAPORU.md` özellik kataloğunun devamı — **Ö-80 … Ö-99**.
> **Görsel karşılığı:** her yüzeyin gerçek ölçekli maketi →
> [Windows Yüzey Maketleri](https://claude.ai/code/artifact/fa0eb901-aa94-448b-9772-39fa0bcffa85)

---

## 1. Tek cümlelik sonuç

Windows 11'de bir kullanım ölçeri **on iki farklı yüzeyde** görünebilir. Bunların **altısı**
bugün elimizdeki imzasız/paketsiz WinUI uygulamasıyla **hemen** yapılabilir; **beşi**
"paket kimliği" (sparse package) arkasında; **biri** (Start menü companion) henüz
resmî API değil.

**En yüksek getirili tek bulgu:** `AppNotificationManager.UpdateAsync` ile **kendini yerinde
güncelleyen, ilerleme çubuklu bir bildirim** — Bildirim Merkezi'nde kalıcı durur, her
yenilemede yüzdeyi günceller, **yeniden pop-up olmaz**. Bu, Windows'ta "widget"a en yakın
şey ve **paket kimliği dışında hiçbir şey gerektirmiyor.**

---

## 2. Kapı: paket kimliği (bunu önce karar ver)

Elimizdeki `CodexBridge.Taskbar` **unpackaged** (`WindowsPackageType=None`). Windows'un
modern kabuk yüzeylerinin çoğu "package identity" ister. Üç yol var:

| Yol | Ne açar | Maliyet |
|---|---|---|
| **Hiçbir şey yapma** (bugünkü hâl) | Görev çubuğu band'ı, tepsi ikonu, taskbar button API'leri, jump list, iconic thumbnail | 0 |
| **Registry + COM aktivatör** (klasik unpackaged toast) | + Toast bildirimleri | Düşük — `HKLM\Software\Classes\AppUserModelId\<AUMID>` altına `DisplayName`, `IconUri`, `IconBackgroundColor`, `CustomActivator` (GUID); `INotificationActivationCallback` uygulayan COM sınıfı |
| **Sparse package / external location** | + Toast (temiz yoldan), **Widgets**, arka plan görevleri, paylaşım hedefi, başlangıç görevi, Start companion | Orta — küçük bir MSIX kimlik paketi + exe'lere side-by-side manifest; **kurulum ve dosya konumları değişmez** |

> **Öneri:** Sparse package'ı **Faz 8'in ilk işi** yap. Tek başına 5 özelliğin kapısını açıyor
> ve kurulum akışımızı (Inno Setup) değiştirmiyor. Windows 10 2004 (19041)+ gerekiyor —
> hedefimiz zaten Windows 11.

**Uyarı:** Toast bildirimleri **yükseltilmiş (admin) süreçlerde çalışmaz** — `Show()` sessizce
başarısız olur. Band'ımız zaten normal kullanıcı hakkıyla çalışıyor, korunmalı.

---

## 3. Bildirim (toast) — yapabileceğimiz her şey

API: `Microsoft.Windows.AppNotifications` + `.Builder` (Windows App SDK 1.2+),
ya da ham `ToastGeneric` XML şeması.

### 3.1 İçerik bütçesi (sert sınırlar)

| Öğe | Sınır / boyut |
|---|---|
| Metin | **en fazla 3** `<text>`: başlık (varsayılan 2 satır) + 2 açıklama (toplam 4 satır) |
| App logo override | **48×48** px @100%, `AppNotificationImageCrop.Circle` ile daire kırpma |
| Hero image | **364×180** px @100%, bildirimin üstünde tam genişlik |
| Inline image | Görsel alanın tam genişliği, metinlerden sonra |
| Düğme | **toplam 5** (bağlam menüsü öğeleri dahil) |
| Düğme ikonu | 16×16 beyaz-şeffaf PNG, padding'siz; **biri varsa hepsinde olmalı** |
| Attribution text | Metinlerin altında, küçük punto |
| Uzak görsel | 3 MB (normal ağ) / 1 MB (kotalı ağ); aşarsa görsel düşer, bildirim gösterilir |
| Tooltip (`szTip`) | — bkz. §5 tepsi |

**Paketsiz uygulamada `http://` görselleri desteklenmiyor** → görseli önce
`%LOCALAPPDATA%`'ya indirip `ms-appdata:///` ile referansla. Bizim için sorun değil:
grafikleri zaten kendimiz çiziyoruz.

### 3.2 İlerleme çubuğu + veri bağlama ⭐

```csharp
var b = new AppNotificationBuilder()
    .AddText("Claude · Pro 20x")
    .AddProgressBar(new AppNotificationProgressBar()
        .BindTitle().BindValue().BindValueStringOverride().BindStatus());

var n = b.BuildNotification();
n.Tag = "claude"; n.Group = "quota";
n.Progress = new AppNotificationProgressData(1) {
    Title = "5 saatlik oturum", Value = 0.42,
    ValueStringOverride = "%42 · 3s 12dk kaldı", Status = "Yenilendi 12:04"
};
AppNotificationManager.Default.Show(n);

// Sonraki her yenilemede — pop-up YOK, konum değişmez:
await AppNotificationManager.Default.UpdateAsync(
    new AppNotificationProgressData(2) { Value = 0.47, ValueStringOverride = "%47 · 2s 58dk" },
    "claude", "quota");
```

| Alan | Zorunlu | Bağlanabilir |
|---|---|---|
| `Title` | hayır | ✅ |
| `Value` (0.0–1.0 veya `Indeterminate`) | hayır | ✅ |
| `ValueStringOverride` ("%42" yerine ne yazsın) | hayır | ✅ |
| `Status` (çubuğun altındaki metin) | **evet** | ✅ |

**Veri bağlama yalnızca** ilerleme çubuğu alanları **ve üst seviye metinlerin `Text`'i** için
çalışır. Dizi numarası (`AppNotificationProgressData(n)`) her güncellemede artmalı.

**Güncelle ≠ Değiştir:**

| | Değiştir (aynı Tag+Group ile yeni `Show`) | Güncelle (`UpdateAsync`) |
|---|---|---|
| Bildirim Merkezi konumu | En üste taşınır | **Yerinde kalır** |
| Değiştirilebilen içerik | Her şey | Yalnızca bağlanabilir alanlar |
| Tekrar pop-up | Evet (`SuppressPopup=false` ise) | **Hayır** |
| Kullanıcı kapattıysa | Yine gönderilir | **Başarısız olur** |

→ **Tasarım:** her sağlayıcı için `Tag=<providerId>`, `Group="quota"` ile bir kez
`SuppressPopup` ile sessiz gönder; her yenilemede `UpdateAsync`. Sonuç: Bildirim
Merkezi'nde canlı bir kota panosu. Eşik geçilince **değiştir** (pop-up olsun).

### 3.3 Senaryolar

| Senaryo | Davranış | Bizim kullanımımız |
|---|---|---|
| (yok) | Normal, birkaç saniye görünür | Rutin eşik bildirimi |
| `Reminder` | **Kullanıcı kapatana kadar ekranda kalır**; en az 1 düğme şart | "Haftalık kota %90 — devam edersen Perşembe biter" |
| `Alarm` | Reminder + döngüsel alarm sesi | Fazla agresif, kullanmayalım |
| `IncomingCall` | Tam ekran/ön açık, özel düzen | Bize uymaz |
| `Urgent` | **Rahatsız Etmeyin / Focus Assist'i deler** | "Kota bitti — ajan duracak". `AppNotificationBuilder.IsUrgentScenarioSupported()` ile koru |

### 3.4 Etkileşim

- **Düğmeler:** `AddButton` → tıklamada uygulama argümanlarla açılır. Windows App SDK'da
  düğmeler **daima ön planda** aktive eder; "arka plan eylemi" istiyorsan argümanı işleyip
  pencere açmadan çık.
- **Renkli düğmeler (Win11):** `AppNotificationButtonStyle.Success` / `.Critical` (yeşil/kırmızı).
- **Düğme tooltip'i (Win11):** `SetToolTip` — ikonlu düğmelerde Narrator için şart.
- **Bağlam menüsü:** `SetContextMenuPlacement()` → sağ tık menüsüne düşer ("1 saat sustur").
  5 düğme bütçesinden yer.
- **Girdi:** `AddTextBox` (hızlı yanıt) ve `AddComboBox` (seçim) — bizde erteleme süresi seçimi için.
- **Sistem erteleme/kapatma:** `ToastButtonSnooze` / `ToastButtonDismiss` (ham XML).
  `SelectionBoxId` ile erteleme aralığı dakika cinsinden seçtirilebilir → **"reset saatine kadar ertele"**.
- **Ses:** `SetAudioEvent(AppNotificationSoundEvent.*)` veya `SetAudioUri` (`ms-appdata:///`).

### 3.5 Diğer

- **Özel zaman damgası:** `SetTimeStamp(...)` → Bildirim Merkezi'nde "veri ne zaman üretildi"
  yazar; bizim "veri yaşı" ilkemizle birebir örtüşüyor.
- **Başlıklar (headers):** Bildirim Merkezi'nde bildirimleri kendi başlığımız altında gruplar
  → "CodexBridge · Kota" grubu.
- **Adaptive gruplar/sütunlar:** sol/sağ hizalı iki sütun (ör. sol: sağlayıcı adı, sağ: reset).
  **Yalnızca ham XML** — builder desteklemiyor.

---

## 4. Görev çubuğu düğmesi (taskbar button) — hiç kimsenin kullanmadığı 4 API

Bunlar Windows 7'den beri var, **paket kimliği gerektirmiyor**, ve bir kullanım ölçeri için
biçilmiş kaftan. Hepsi `ITaskbarList3` (COM, `shobjidl_core.h`).

| API | Ne yapar | Bizim kullanımımız |
|---|---|---|
| `SetProgressValue` / `SetProgressState` | Görev çubuğu düğmesinin **içine** ilerleme çubuğu çizer | **Kota yüzdesi doğrudan düğmenin içinde.** `TBPF_NORMAL` (yeşil) / `TBPF_PAUSED` (sarı) / `TBPF_ERROR` (kırmızı) ile eşik rengi bedava gelir |
| `SetOverlayIcon` | Düğmenin köşesine **16×16** rozet | Severity noktası veya "!" — kota kritikken |
| `ThumbBarAddButtons` | Küçük resim önizlemesinin altına **en fazla 7** düğme | "Yenile", "Ayarlar", "Panoyu aç" |
| `DwmSetIconicThumbnail` + `DwmSetIconicLivePreviewBitmap` | Görev çubuğu **hover önizlemesini tamamen bizim çizdiğimiz bitmap yapar** | ⭐ Fare düğmenin üstüne gelince **mini pano**: tüm sağlayıcılar, yüzdeler, reset saatleri. Pencereyi açmaya gerek yok |

**Iconic thumbnail mekaniği:** pencereye `DWMWA_FORCE_ICONIC_REPRESENTATION` ve
`DWMWA_HAS_ICONIC_BITMAP` set et; DWM `WM_DWMSENDICONICTHUMBNAIL` /
`WM_DWMSENDICONICLIVEPREVIEWBITMAP` gönderdiğinde bitmap'i üret ve ilgili `DwmSet...` ile ver.
Yani **DWM istediğinde çiziyoruz** — sürekli render maliyeti yok.

**Jump list** (`ICustomDestinationList`, sağ tık menüsü):
- `AddUserTasks` → "Tasks" kategorisi (kullanıcı sabitleyemez/silemez) — "Yenile", "Ayarlar", "Panoyu aç"
- `AppendCategory` → **kendi adlandırdığımız kategori** — "Sağlayıcılar: Claude %42 · Codex %18 · Cursor %71"
- Uygulamanın açık bir AUMID'si varsa `BeginList`'ten **önce** `SetAppID` çağrılmalı.

---

## 5. Bildirim alanı (tepsi) ikonu

`Shell_NotifyIcon` / `NOTIFYICONDATA`:

- **`szTip` en fazla 128 karakter** (sonlandırıcı dahil), `\r\n` ile **çok satırlı**.
  → 3–4 sağlayıcı + yüzde + reset sığar: `"Claude %42 · 3s12dk\r\nCodex %18 · 2g\r\nCursor %71"`.
- İkon her yenilemede `NIM_MODIFY` ile değiştirilebilir → Win-CodexBar'ın
  `render_bar_icon_rgba` (32×32 RGBA, iki çubuk: oturum üstte, haftalık altta) algoritması
  buraya birebir uyarlanır (**Ö-25**).
- `guidItem` ile ikon kimliği yeniden başlatmalar arasında sabit kalır (konum korunur).
- `NIF_INFO` balon bildirimi → Windows 10/11'de kabuk bunu **zaten toast'a çeviriyor**;
  ayrı bir yol değil, toast kullan.

---

## 6. Windows Widgets (Widgets Board / Win+W)

**Sert kısıt: yalnızca paketli (MSIX) uygulamalar widget sağlayıcısı olabilir.**
(PWA da olur; unpackaged olmaz.) Sparse package bu kapıyı açar.

- **Arayüz:** Adaptive Cards JSON (şablon + veri ayrı). XAML/WinUI **değil**.
- **Boyutlar:** `small` / `medium` / `large`. Aynı şablonda
  `"$when": "${$host.widgetSize==\"medium\"}"` ile boyuta göre dallanma.
- **Tasarım kuralları:** 16px kenar boşluğu, **48px attribution alanı** (içerik konulamaz),
  4px gutter, her ölçü 4'ün katı. Segoe UI tip rampası:
  Caption 12/16 · Body 14/20 · Body Large 18/24 · Subtitle 20/28 · Title 28/36.
- **Widget seçicideki ekran görüntüsü:** **300×304 px**, şeffaf yuvarlatılmış köşeler
  (manifest'te `<Screenshot Path=... DisplayAltText=...>`).
- **Güncelleme:** sağlayıcı `WidgetManager.GetDefault().UpdateWidget(...)` çağırır.
  `Activate`/`Deactivate` widget board açılıp kapandığında gelir — **"aradaki pencere kısa
  olabilir, güncelleme yolunu hızlı tut"** (Microsoft'un kendi uyarısı). Bizim `SnapshotCache`
  tam da bunun için var.
- **Etkileşim:** `Action.Execute` + `verb` → `OnActionInvoked`. Widget başına `CustomState`
  saklanabilir (hangi sağlayıcı seçili gibi).
- **Manifest:** `com:Extension` (COM server) + `uap3:Extension Category="windows.widgetProvider"`
  içinde `<Definition Id DisplayName Description>` + `<Capabilities><Size Name=.../>` +
  `<ThemeResources>` (ikon + ekran görüntüsü, açık/koyu tema ayrı).
- **Web widget:** artık içerik bir URL'den HTML olarak da servis edilebiliyor, ama
  **yine de bir Adaptive Card payload'ı vermek zorunlu.**

---

## 7. Start menü "Companions" — henüz değil

- İlk kez Windows 11 Insider build **26212** (Mayıs 2024). 2026 itibarıyla **hâlâ resmî
  Microsoft Learn dokümantasyonu yok**; Deneysel/Insider kanallarında.
- Teknik: paketli uygulama manifest'ine bir uzantı + **Adaptive Cards JSON** dosya yolu;
  kabuk JSON değişince paneli otomatik tazeliyor. Start menüsünün soluna veya sağına
  yerleşebiliyor (manifest'te belirtiliyor).
- Topluluk örneği: `thebookisclosed/StartMenuCompanionSample`.
- Windows 11 26H2 (2026 sonbaharı) Start menü/görev çubuğu için büyük değişiklikler getiriyor;
  companions bu dalgada stabilleşebilir.

**Karar: üzerine inşa etme, izle.** Feature-flag arkasında, dokümantasyonsuz bir API'ye
bağlanmak `00-MIMARI` §10'daki "Microsoft tekniği engeller" riskinin aynısı. Ama widget
tarafını Adaptive Cards ile yazarsak, companion çıktığında **aynı JSON'u** oraya da veririz —
bedava opsiyon.

---

## 8. Özellik kataloğu — Ö-80 … Ö-99

> Her maddenin gerçek ölçekli maketi:
> [**Windows Yüzey Maketleri**](https://claude.ai/code/artifact/fa0eb901-aa94-448b-9772-39fa0bcffa85)

Efor ölçeği `08` raporuyla aynı. "Kimlik" sütunu: **yok** = bugünkü unpackaged hâlle olur ·
**reg** = registry+COM aktivatör yeter · **paket** = sparse package şart.

### 8.A Bildirimler

| # | Özellik | Kimlik | Efor | Not |
|---|---|---|---|---|
| **Ö-80** | Eşik toast'ı (yüksek/kritik/tükendi) | reg | S | `08`'deki Ö-36'nın Windows tarafı |
| **Ö-81** | ⭐ **Canlı kota kartı** — progress bar + `UpdateAsync`, Bildirim Merkezi'nde kalıcı | reg | S | Windows'ta widget'a en yakın şey, kimlik dışında hiçbir şey gerekmiyor |
| **Ö-82** | `Urgent` senaryosu — kota bitince DND'yi deler | reg | XS | `IsUrgentScenarioSupported()` ile koru |
| **Ö-83** | `Reminder` senaryosu — kapatana kadar ekranda kalan pace uyarısı | reg | XS | En az 1 düğme şart |
| **Ö-84** | Toast düğmeleri: Yenile / Ayarlar / 1 saat sustur (bağlam menüsü) | reg | S | Toplam 5 düğme bütçesi |
| **Ö-85** | Hero image (364×180): haftalık burn sparkline'ı, PNG olarak `%LOCALAPPDATA%`'ya çizilir | reg | M | Paketsizde `http://` yasak, yerel dosya şart |
| **Ö-86** | Reset saatine hizalı erteleme (`ToastButtonSnooze` + `SelectionBoxId`) | reg | S | Ham XML gerekiyor |
| **Ö-87** | Bildirim başlıkları ile sağlayıcı bazlı gruplama | reg | XS | |
| **Ö-88** | Özel zaman damgası = veri üretim anı | reg | XS | "veri yaşı" ilkemizle birebir |
| **Ö-89** | Adaptive sütunlar (sol: sağlayıcı, sağ: reset) — ham XML | reg | S | Builder desteklemiyor |

### 8.B Görev çubuğu düğmesi

| # | Özellik | Kimlik | Efor | Not |
|---|---|---|---|---|
| **Ö-90** | `SetProgressValue` — kota çubuğu düğmenin içinde | **yok** | XS | Eşik rengi `TBPF_NORMAL/PAUSED/ERROR` ile bedava |
| **Ö-91** | `SetOverlayIcon` — 16×16 severity rozeti | **yok** | XS | |
| **Ö-92** | ⭐ `DwmSetIconicThumbnail` — **hover önizlemesi = mini pano** | **yok** | M | DWM istediğinde çiziyoruz, sürekli maliyet yok |
| **Ö-93** | `ThumbBarAddButtons` — önizleme altında 7 düğmeye kadar eylem | **yok** | S | |
| **Ö-94** | Jump list: Tasks + "Sağlayıcılar" özel kategorisi | **yok** | S | `SetAppID` sırası önemli |

### 8.C Tepsi

| # | Özellik | Kimlik | Efor | Not |
|---|---|---|---|---|
| **Ö-95** | Dinamik 32×32 tepsi ikonu (Win-CodexBar algoritması) | **yok** | S | `08`'deki Ö-25 |
| **Ö-96** | 128 karakter çok satırlı tooltip (3–4 sağlayıcı sığar) | **yok** | XS | `\r\n` destekli |

### 8.D Kimlik gerektirenler

| # | Özellik | Kimlik | Efor | Not |
|---|---|---|---|---|
| **Ö-97** | **Sparse package / external location** — kimlik kapısı | — | M | Ö-93, Ö-98, Ö-99 + temiz toast yolu bunun arkasında |
| **Ö-98** | Windows Widget sağlayıcısı (S/M/L, Adaptive Cards) | **paket** | L | Kurulum akışı değişmez ama MSIX kimlik paketi şart |
| **Ö-99** | Start menü companion (deneysel — **izle, inşa etme**) | **paket** | ? | Adaptive Cards JSON'u widget'la paylaşılır |

---

## 9. Öncelik tavsiyesi

**Hemen (kimlik gerekmiyor, toplam ~1 hafta):**
`Ö-90` + `Ö-91` + `Ö-96` — üçü birlikte, band'ın yanında görev çubuğunda ikinci bir ölçer
katmanı verir ve hiçbir altyapı değişikliği istemez.

**Sonra (registry + COM aktivatör, ~1 hafta):**
`Ö-81` (canlı kota kartı) + `Ö-80` + `Ö-82`. Bu üçü "bildirim" özelliğini tam yapar ve
`NotificationEngine.Diff` zaten hazır — sadece Windows tarafı eksik.

**Sonra (görsel etki en yüksek tekil iş):**
`Ö-92` iconic thumbnail. Görev çubuğu düğmesine fare gelince tam bir pano çıkması,
üründe başka kimsede olmayan bir an.

**Karar gerektiren:**
`Ö-97` sparse package. Evet dersek `Ö-98` widget açılır ve toast yolu temizlenir;
hayır dersek registry yoluyla idare ederiz ama widget board kapalı kalır.

---

## 10. Kaynaklar

- [App notification content — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-content)
- [App notification progress bar and data binding](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-progress-bar)
- [Quickstart: Send and Handle App Notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart)
- [Send a local toast from other types of unpackaged apps](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast-other-apps)
- [Grant package identity by packaging with external location](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps-overview)
- [Widget providers](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-providers) · [Implement a widget provider (C#)](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs) · [Widget design fundamentals](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-design-fundamentals)
- [ITaskbarList3::SetProgressValue](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist3-setprogressvalue) · [SetOverlayIcon](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist3-setoverlayicon)
- [Customize an Iconic Thumbnail and a Live Preview Bitmap](https://learn.microsoft.com/en-us/windows/win32/dwm/dwm-sample-customizethumbnail)
- [ICustomDestinationList](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icustomdestinationlist)
- [NOTIFYICONDATAW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)
- Start companions (resmî değil): [StartMenuCompanionSample](https://github.com/thebookisclosed/StartMenuCompanionSample) · [Tom's Hardware](https://www.tomshardware.com/software/windows/windows-adds-custom-widgets-called-companions-to-the-start-menu-heres-how-to-make-and-use-your-own)
