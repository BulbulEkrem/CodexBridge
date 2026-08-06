# CodexBridge — Mimari ve Yol Haritası

> Kaynak araştırma: `arastirma_claude/depo/2026-08-06-codexbar-cok-platform/`
> Üst akış: `steipete/CodexBar` @ `5cb69f0c` · yerel kopya `C:\project\CodexBar`
> Tarih: 2026-08-06 · Durum: **plan (kod yazılmadı)**

---

## 1. Ürün tek cümlede

CodexBar'ın platformdan bağımsız veri katmanına **iki yeni yüz** takan Windows-merkezli
bir ürün: (1) Windows **görev çubuğunun içinde** her an görünür bir AI kullanım ölçeri,
(2) telefonun (iOS/Android widget) bağlanacağı, Windows'ta çalışan bir **`dashboard/v1`
HTTP host'u.**

## 2. Gerçek boşluk (neyi inşa ediyoruz, neyi etmiyoruz)

Araştırmanın en kritik bulgusu bir teknik detay değil, bir **kapsam düzeltmesi**:

- Windows'ta **AI kullanım göstergesi zaten var** — `Finesssee/Win-CodexBar` (56 sağlayıcı,
  2.508 commit, 921 yıldız, `winget`). Ama yalnızca **tepsi ikonu**; HTTP API yok, widget yok.
- macOS ve Linux'ta `codexbar serve` telefona veri servis edebiliyor. **Windows'ta hiçbir
  CodexBar türevi bunu yapamıyor.**

Dolayısıyla **hiçbir platformda olmayan** ve bizim inşa ettiğimiz iki şey:

| # | Boşluk | Neden değerli |
|---|---|---|
| 1 | Görev çubuğu **içinde** kalıcı ölçer (tepsi ikonu değil) | Pencere açmadan, her an görünür kota |
| 2 | Windows'ta çalışan `dashboard/v1` **HTTP host'u** | Telefon istemcisini **üç OS'a birden** bağlar |

**Yapmayacağımız şey:** 67 sağlayıcıyı C#'a yeniden yazmak. Bu, Win-CodexBar'ın
2.508 commit'lik olgunluğuyla kazanılamayacak bir yarış ve kullanıcıya yeni değer katmıyor.
Bizim yeni değerimiz **yüzey** ve **HTTP host**, sağlayıcı sayısı değil.

## 3. Mimari — katmanlar

```
┌──────────────────────────────────────────────────────────────┐
│  YÜZEY                                                        │
│   ├─ Görev çubuğu ölçeri   (WinUI 3, parent'lanmış pencere)   │  ← ana yüzey
│   └─ Tepsi ikonu + popover (WinUI 3)                          │  ← yedek/tamamlayıcı yüzey
├──────────────────────────────────────────────────────────────┤
│  YENİLEME MOTORU  (AdaptiveRefreshCore'un C# çevirisi)        │  ← 2–30 dk karar tablosu
├──────────────────────────────────────────────────────────────┤
│  SAĞLAYICI KATMANI                                            │
│   ├─ JS host (ClearScript)  → 15 hazır .js sağlayıcı          │
│   ├─ C# HTTP stratejileri   → apiToken (29) + oauth (5)       │
│   └─ Çerez katmanı (DPAPI)  → web stratejileri (31)  [geç faz]│
├──────────────────────────────────────────────────────────────┤
│  dashboard/v1 HTTP HOST  (ASP.NET Core minimal API)          │  ← telefon buraya bağlanır
└──────────────────────────────────────────────────────────────┘
                              │  dashboard/v1 (aynı şema)
          ┌───────────────────┼───────────────────┐
     iOS widget          Android widget        (macOS/Linux serve de aynı şemayı konuşur)
    (WidgetKit)         (Glance+WorkManager)
```

**Neden bu kadar taşınabilir:** Üst akışta her sağlayıcı tek bir *descriptor* + bir/birkaç
*fetch stratejisi* ile tanımlı. Sağlayıcılar platformu değil, **9 host API**'sini görüyor
(`KeychainAPI`, `BrowserCookieAPI`, `HTTPAPI`, `PTYAPI`, ...). Port için taşınacak yüzey
bu 9 arayüz. Üstelik 15 sağlayıcının mantığı Swift değil, düz **JavaScript** (`.js` dosyası)
— C# host'ta ClearScript/Jint ile olduğu gibi çalışır, yeniden yazma yok.

## 4. Teknoloji yığını ve gerekçeler

| Alan | Seçim | Gerekçe |
|---|---|---|
| Windows dili/UI | **.NET 8 / C# + WinUI 3** | Tek çalışan görev çubuğu tekniği bu dünyada (Deskband11 C#/WinUI 3) |
| HTTP host | **ASP.NET Core minimal API** | `serve` POSIX soketi; .NET'te yeniden yazmak kolay |
| JS runtime | **ClearScript (V8)** | 15 hazır `.js` sağlayıcıyı çalıştırmak için; Jint yedek |
| Görev çubuğu tekniği | **Parent'lanmış saydam WinUI 3 penceresi** | Deskband API'si öldü; sahada çalışan tek teknik (Deskband11) |
| iOS widget | **WidgetKit** (üst akıştaki 6 görünüm) | `Sources/CodexBarWidget/` görünümleri aynen taşınır; tek değişen veri kaynağı |
| Android widget | **Jetpack Glance + WorkManager** | Compose tabanlı AppWidget; `updatePeriodMillis=0` + WorkManager |
| Telefon↔host güvenlik | **TLS ters proxy veya Tailscale** | `serve`'de TLS yok; düz HTTP'de bearer token açık geçer |
| Swift'i Windows'ta derlemek | **Hayır** | `os(Windows)` sıfır kez geçiyor; `VISION.md:26` "aspiration, not a commitment" |

## 5. Repo yapısı (hedef)

```
CodexBridge/
  docs/
    00-MIMARI-VE-YOL-HARITASI.md   ← bu dosya
    dashboard-v1-schema.md          ← host↔telefon sözleşmesi (üst akıştan uyarlanır)
  src/
    CodexBridge.Core/               ← sağlayıcı katmanı, yenileme motoru, modeller
    CodexBridge.JsHost/             ← ClearScript host + ctx nesnesi (9 alt nesne)
    CodexBridge.Host/               ← ASP.NET Core dashboard/v1 minimal API
    CodexBridge.Taskbar/            ← WinUI 3 görev çubuğu yüzeyi + tepsi ikonu
  phone/
    ios/                            ← WidgetKit istemcisi
    android/                        ← Glance + WorkManager istemcisi
  spikes/
    taskbar-parenting/              ← Faz 0 deneme kodu
```

## 6. Sağlayıcı stratejisi: sıfırdan yazmadan veri

Üç seçenek vardı; kararı **B, A ile başlayarak**:

| Seçenek | İlk sürüme kadar | Bağımsızlık | Karar |
|---|---|---|---|
| A. `codexbar-cli.exe`/Win-CodexBar çıktısını sarmala | En hızlı | Yok | **Geçici** (Faz 2) |
| **B. 15 JS sağlayıcı + C# HTTP stratejileri** | Orta | Yüksek | **Hedef** (Faz 5) |
| C. 67 sağlayıcıyı C#'a yeniden yaz | Çok uzun | Tam | **Hayır** |

C# tarafında yapılacak iş: `docs/plugins.md`'deki `ctx` nesnesini (9 alt nesne:
`http`, `settings`, `fail`, `browser.cookieHeader`, `html`, `cache`, `date`, `jwt`, `pct`)
implemente etmek ve `.js` dosyalarını üst akıştan periyodik senkronlamak. Fiyat/uç nokta
değişiminde tek yapılacak `.js`'i kopyalamak.

## 7. dashboard/v1 sözleşmesi (host ↔ telefon)

`GET /dashboard/v1/snapshot` (`Authorization: Bearer <token>`). Sürümlenmiş, görüntülemeye
yönelik, kimlik **maskeli** (e-posta yerel kısmı gizli). `staleAfterSeconds` ile bayatlık
eşiği sunucudan gelir. Şema özeti:

```json
{ "schemaVersion": 1, "generatedAt": "...", "staleAfterSeconds": 180,
  "host": { "codexBarVersion": "...", "refreshIntervalSeconds": 60 },
  "providers": [{ "id": "codex", "status": {"level":"ok"},
    "identity": {"accountEmail":"redacted@example.com","plan":"Pro 20x"},
    "windows": [{"kind":"session","usedPercent":28,"remainingPercent":72,"resetAt":"..."}],
    "cost": {"todayUSD":1.04,"last30DaysUSD":18.22}, "error": null }] }
```

Bu şemaya sadık kalmanın getirisi: **tek Android/iOS uygulaması üç OS host'una da bağlanır.**
Mimarideki en önemli tek karar.

## 8. Yol haritası — fazlar

| Faz | İş | Çıktı | Bağımlılık |
|---|---|---|---|
| **0** | Görev çubuğu tekniği spike'ı (Deskband11 oku + parent'lama denemesi) | **Gitme/gitmeme kararı** | — |
| 1 | WinUI 3 görev çubuğu yüzeyi, sahte veri | Çubukta çalışan ölçer | 0 |
| 2 | Veri kaynağı sarmalayıcı (geçici) + lisans kontrolü | Gerçek veri gösteren yüzey | 1 |
| 3 | **`dashboard/v1` HTTP host** (ASP.NET Core) | Telefon geliştirilebilir | 2 |
| 4a | iOS widget (WidgetKit görünümleri + HTTP kaynağı) | iOS sürümü | 3 |
| 4b | Android widget (Glance + WorkManager) | Android sürümü | 3 |
| 5 | Kendi sağlayıcı katmanı: JS host (ClearScript) + C# HTTP | Üçüncü taraf ikiliden kurtulma | 3 |
| 6 | Çerez katmanı (Chrome/Edge/Firefox DPAPI) | 31 `web` stratejisi açılır | 5 |
| 7 | Push bildirimi (host → telefon) | Yenileme bütçesi kısıtının çözümü | 3 + 4 |

### Faz 0 — gitme/gitmeme kapısı (önce bu)

**Formalite değil; projenin ana yüzeyi buna bağlı.** Doğrulanacaklar:
- [ ] Çok monitör, farklı DPI
- [ ] Görev çubuğu otomatik gizleme
- [ ] Çubuğun üste/yana taşınması
- [ ] Explorer çökmesi/restart (`TaskbarCreated` mesajıyla yeniden parent'lama)
- [ ] Görev çubuğu **ortalıyken** boş sol alan ne kadar kalıyor
- [ ] Windows Update sonrası davranış

**Çıkmazsa:** plan tepsi ikonuna düşer (resmî, kırılgan değil); ürünün değeri HTTP host +
telefon widget'ına daralır — hâlâ geçerli bir ürün, ama bunu faz 4'te değil **faz 0'da**
öğrenmek gerekir.

**Kritik sıralama:** Faz 3 tüm telefon işini kilitliyor, o da faz 2'ye bağlı. Bu yüzden
faz 2'nin lisans kontrolü (Win-CodexBar sarmalanabilir mi?) **faz 0 ile paralel** yürümeli.

## 9. Token/etik kuralları (üst akıştan çıkarılan)

1. **Azure OpenAI ve Doubao probe'larını varsayılan KAPALI getir** — bunlar gerçek
   `max_tokens:1` inference isteği atıyor; Doubao'da probe ölçtüğü kotadan düşüyor.
   Açarken "bu sağlayıcı kotanızdan istek harcar" uyarısı göster. (65/67 sağlayıcı token yakmıyor.)
2. **`codex exec` ile log tohumlama fikrini ALMA** — gerçek ajan turu başlatır, kelepçesi yok.
3. **Adaptive kadans mantığını aynen taşı** (saf karar tablosu, bağımsız).
4. **Telefon asla doğrudan sağlayıcıya gitmesin** — PC host'un önbelleğinden okusun
   (3 cihaz = 3 kat istek olmasın).

## 10. Riskler

| Risk | Olasılık | Etki | Azaltma |
|---|---|---|---|
| Microsoft parent'lama tekniğini engeller | Orta | **Yüksek** | Tepsi ikonu her zaman ikinci yüzey olarak dursun |
| Explorer restart'ta yüzey kaybolur | Yüksek | Orta | `TaskbarCreated` dinle, yeniden parent'la |
| Win-CodexBar lisansı sarmalamaya izin vermez | Orta | Düşük | Faz 5 zaten bağımsızlık getiriyor; faz 2 geçici |
| Telefon yenileme bütçesi dar (iOS 40–70/gün, Android min 15 dk) | Yüksek | Orta | "Son bilinen durum + yaşı" + resetAt'e hizalı timeline + push (faz 7) |
| Düz HTTP'de token sızar | Orta | Orta | Varsayılan loopback; TLS proxy/Tailscale; LAN'da açık uyarı |
| JS sağlayıcıları ClearScript'te çalışmaz | Düşük | Orta | Faz 5'te erken doğrula; C# yeniden yazma yedek |

## 11. Açık sorular (karar öncesi çözülmeli)

1. **Win-CodexBar lisansı ve kaynağı** — Faz 2'nin tamamı buna bağlı. "loopback
   integrations" ifadesi belgelenmemiş bir HTTP sunucusu olabilir mi?
2. **Deskband11 kaynak kodu** — teknik yalnızca README beyanından biliniyor; gerçek pencere
   yönetimi kodu okunmadı. Faz 0'ın ilk işi.
3. **`provider-plugin-prelude.js` + Sucrase** — JavaScriptCore'a özel davranışa dayanıyor mu?
   Faz 5'in fizibilitesi buna bağlı.
4. **iOS Live Activities / Dynamic Island** — kota tükenmesi için push'tan daha uygun olabilir.
5. **Rust sağlayıcı mantığını FFI ile C#'a bağlamak** — süreç çağırmaktan temiz bir dördüncü
   seçenek olabilir; hiç araştırılmadı.

## 12. İlk somut adımlar (öneri)

1. **Faz 0 spike** — Deskband11 kaynağını oku, minimal bir WinUI 3 parent'lama denemesi yaz.
2. **Paralel:** Win-CodexBar lisansını ve HTTP arayüzü olup olmadığını incele (Faz 2 kritik yol).
3. `dashboard/v1` şemasını `docs/dashboard-v1-schema.md`'ye üst akıştan tam çıkar.
4. Sonuca göre faz 1'e geç veya planı tepsi ikonu yüzeyine daralt.
