# Win-CodexBar Analiz Raporu — CodexBridge Özellik Kaynağı

> **Analiz edilen:** `nesszer/Win-CodexBar` @ `8fdcc66` (2026-09-03) · sürüm **0.55.0** (build 90) · MIT
> **Karşılaştırılan:** `BulbulEkrem/CodexBridge` @ `634d850` (bu depo)
> **Tarih:** 2026-09-04 · **Amaç:** Özellik seçim menüsü. Aşağıdaki `Ö-xx` kodlarını
> söyleyerek doğrudan iş emri verebilirsin ("Ö-04, Ö-17 ve Ö-31'i istiyorum").

---

## 0. Bu rapor nasıl kullanılır

- **Bölüm 1** — projeyi yeniden planlarken bilmen gereken 5 kritik bulgu. Önce bunu oku.
- **Bölüm 4** — 60+ özelliğin `Ö-xx` kodlu kataloğu. Her satırda: kaynak dosya, bizdeki durum,
  tahmini efor. **Seçimini buradan yap.**
- **Bölüm 7** — kopyala-yapıştır seviyesinde alınabilecek hazır varlıklar (MIT).
- **Bölüm 9** — "sıfırdan başlıyoruz" senaryosu için üç mimari seçenek + tavsiye.

Efor ölçeği: **XS** ≤ yarım gün · **S** 1–2 gün · **M** 3–7 gün · **L** 2–4 hafta · **XL** 1 ay+

---

## 1. Yönetici özeti — 5 kritik bulgu

### 1.1 ⚠️ Farklılaşma noktalarımızdan biri KAPANDI

`docs/00-MIMARI-VE-YOL-HARITASI.md` §2, projenin iki gerekçesinden birini şöyle yazıyordu:

> "macOS ve Linux'ta `codexbar serve` telefona veri servis edebiliyor. **Windows'ta hiçbir
> CodexBar türevi bunu yapamıyor.**"

**Bu artık doğru değil.** Win-CodexBar 0.53.0 ile üst akışın `dashboard/v1` sözleşmesini
Windows'a taşıdı. Bugün `codexbar serve` şunları sunuyor:

| Rota | Ne yapıyor |
|---|---|
| `GET /dashboard/v1/snapshot` | **Bizim ürettiğimiz şemanın aynısı** — bearer, `no-store`, kimlik maskeleme |
| `GET /` | Gömülü HTML kullanım panosu (267 satır, 61 sağlayıcı ikonu) |
| `GET /usage`, `/cost` | Ham JSON (Faz 2 adaptörümüzün okuduğu uçlar) |
| `GET /health` | Liveness |
| `GET /icons/<id>.svg` | Sağlayıcı ikonları |
| `codexbar dashboard` | Tek-atış snapshot JSON'u (stdout veya `--output`) |

Kaynak: `rust/src/cli/serve/mod.rs:442-460`, `rust/src/cli/serve/dashboard/snapshot.rs`.

**Sonuç:** "Windows'ta dashboard/v1 host'u yok" gerekçesi düştü. **Ama** `.claude/knowledge/decisions.md`'deki
"`/dashboard/v1/snapshot` YOK" notu da artık eskimiş — ikisi de güncellenmeli.

### 1.2 ✅ Ana farklılaşma noktamız HÂLÂ tek

**Görev çubuğunun içinde kalıcı ölçer.** Win-CodexBar'da yok, üst akışta yok, hiçbir türevde yok.
Win-CodexBar'ın en yakın karşılığı **FloatBar** — ayrı, her zaman üstte yüzen bir pencere
(`apps/desktop-tauri/src-tauri/src/floatbar/`), görev çubuğunun *içinde* değil. Bizim
`SetParent(Shell_TrayWnd)` + Explorer-restart gözcüsü tekniğimiz bu makinede canlı doğrulandı ve
**Deskband11'in çözemediği** restart sorununu çözüyor.

Bu tek başına bir ürünü taşır mı? Taşır — ama tek pill'lik bir band'ın arkasında 158k satırlık bir
veri katmanı gerekiyor. Bölüm 9'daki karar bu.

### 1.3 🎁 Bizim için hazır duran bir köprü var: PowerToys named pipe

Win-CodexBar, dışarıdan tüketilmek üzere bir Windows named pipe yayınlıyor:

```
\\.\pipe\WinCodexBar.Status      → apps/desktop-tauri/src-tauri/src/powertoys.rs
```

Ayar: `powertoys_status_pipe_enabled` (Settings → Advanced). Yayınlanan payload zaten
görev çubuğu pill'i için ideal: `statusText` ("%42"), `subtitle` ("Weekly 18% · Today $1.04"),
primary/secondary pencere, bugünkü/30 günlük maliyet, token sayıları, `topModel`, hata.

**Bu, HTTP polling'e göre çok daha ucuz bir veri yolu** — band süreç-içi pipe'tan okur, HTTP
sunucusu ayağa kaldırmaya gerek kalmaz. Faz 2 adaptörümüzün üçüncü (ve muhtemelen en iyi) seçeneği.

### 1.4 📊 Ölçek farkı gerçekçi bakmayı gerektiriyor

| | Win-CodexBar | CodexBridge |
|---|---|---|
| Sağlayıcı | **70** kimlik (`ProviderId`), 2'si soft-removed | 2 gerçek kaynak (JS: xai/openai/perplexity) + fake |
| Rust/C# çekirdek | 113.026 satır (`rust/src`) | 3.222 satır (tüm C#) |
| Masaüstü kabuk | 20.116 satır Rust (Tauri) | 485 satır (WinUI band) |
| Frontend | 24.477 satır TS/TSX | — (XAML kod-arkası) |
| Telefon | yok | 563 satır Kotlin + 212 satır Swift (iskele) |
| Dil | 8 locale (`.ftl`) | — |
| CLI | 12 alt komut | — |

`00-MIMARI` §2'nin "67 sağlayıcıyı C#'a yeniden yazmayacağız" kararı **hâlâ doğru** ve artık
daha da net: sağlayıcı sayısında yarışmak 113k satırlık bir açığı kapatmak demek.

### 1.5 🔓 Lisans engeli yok

Win-CodexBar **MIT**. Kod kopyalamak, algoritma uyarlamak, sarmalamak, pipe'ından okumak,
`serve`'ünü arka uç yapmak — hepsi serbest. Tek şart telif bildirimini korumak
(`NOTICE` + `LICENSE` dosyaları örnek alınabilir).

---

## 2. Win-CodexBar anatomisi

### 2.1 Katmanlar

```
apps/desktop-tauri/src/          React 18 + Vite  (24k satır) — tray panel, pop-out, settings, floatbar
        ↕ typed invoke bridge (src/lib/tauri.ts, src/types/bridge.ts)
apps/desktop-tauri/src-tauri/    Tauri 2 kabuk    (20k satır) — tray, pencereler, IPC, floatbar, proof
        ↕ codexbar = { path = "../../../rust" }
rust/                            codexbar crate   (113k satır) — sağlayıcılar, ayarlar, çerez,
                                                  tray piksel, maliyet, CLI (codexbar.exe)
```

Cargo workspace; **default-member Tauri crate'i**. İki ikili: `codexbar.exe` (CLI),
`codexbar-desktop-tauri.exe` (masaüstü). Kurulumda CLI `codexbar-cli.exe` adıyla dağıtılıyor.

### 2.2 Veri akışı (ezberlenmeye değer)

```
instantiate_provider (core/provider_factory.rs)   ← TEK fabrika, exhaustive match
    → Provider::fetch_usage(&FetchContext)
    → commands/providers.rs  (semaphore + timeout)
    → AppState.provider_cache
    → Tauri event → React useProviders
```

Ayarlar: `%AppData%\Roaming\CodexBar\settings.json` + kardeş depolar (`manual_cookies.json`,
`api_keys.json`, `token-accounts.json`, `hooks.json`), hepsi `secure_file` (DPAPI) üzerinden.
Önbellek: `%LocalAppData%\CodexBar\` (maliyet taraması, çerez snapshot'ları, SQLite sidecar).

### 2.3 Çekirdek tipler (bizim modellerimizle kıyas)

| Win-CodexBar | Dosya | CodexBridge karşılığı |
|---|---|---|
| `ProviderId` (70 varyant) | `rust/src/core/provider.rs:13` | yok (string id) |
| `trait Provider` | `core/provider.rs` | `IUsageSource` (daha kaba: tüm snapshot) |
| `UsageSnapshot` | `core/usage_snapshot.rs` | `ProviderRow` |
| `RateWindow` | `core/rate_window.rs` | `RateWindow` ✅ uyumlu |
| `SourceMode` (Auto/Web/Cli/OAuth) | `core/provider.rs` | `ProviderRow.Source` (string) |
| `SnapshotPayload` (dashboard/v1) | `cli/serve/dashboard/snapshot.rs:52` | `DashboardSnapshot` ✅ **şema uyumlu** |

`UsageSnapshot` bizimkinden zengin: `primary` + `secondary` + `model_specific` + `tertiary` +
sınırsız `extra_rate_windows` (etiketli), ayrıca `account_email`, `account_organization`,
`login_method`.

### 2.4 dashboard/v1 şeması: bizde eksik alanlar

Bizim `DashboardSnapshot.cs` üst akış sözleşmesine sadık, **ama** Win-CodexBar'ın ürettiği
snapshot'ta bizde olmayan üç alan var:

| Alan | Ne için |
|---|---|
| `providers[].accounts[]` | Çok hesap (Claude swap / Codex accounts) — hesap başına pencere + pace |
| `providers[].accountsError` | Hesap çekme hatası |
| `providers[].windows[].idle` | "Bu pencere boşta" görüntü ipucu |
| `accounts[].pace` | 7 aşamalı pace tahmini (stage, deltaPercent, etaSeconds, summary) |

Ayrıca dokümante edilmiş sapmaları var: `credits` daima `null`, `status` daima `null`
(status polling ayrı bir yol), `display.accentColor` sabit `#6E6E6E`.

---

## 3. Kanıtlanmış teknik detaylar (kod okumasından)

- **Tray ikonu piksel üretimi** platformdan bağımsız: `render_bar_icon_rgba(session, weekly, has_error)`
  → 32×32 ham RGBA döner (`rust/src/tray/render.rs`). Renk `UsageLevel::from_percent`,
  hata durumunda gri tonlama. **Bizim band pill'imiz için doğrudan uyarlanabilir algoritma.**
- **serve güvenlik matrisi** (`serve/mod.rs:165-180`): loopback + token yok → serve eder ama veri
  rotaları açık; non-loopback + token yok → **hata**; non-loopback + token + `--allow-plain-http`
  yoksa → **hata**. Bizim host'umuz aynı politikayı uyguluyor ✅ ama biz **fails-closed**
  (token yoksa 401), onlar loopback'te açık bırakıyor. Bizimki daha sıkı.
- **serve DoS sertleştirmesi**: `MAX_CONNECTIONS` semaphore, `HEAD_CAP` bayt sınırı, tüm-head
  okuma deadline'ı. Bizim ASP.NET Core host'umuzda Kestrel varsayılanları var, eşdeğer sayılır.
- **`FetchContext` + semaphore + timeout** yenileme motoru kabukta, çekirdekte değil.
- **Hook motoru** `hooks.json` + dar env whitelist + JSON stdin ile harici ikili çalıştırıyor.
  6 olay: `quota_low`, `quota_reached`, `quota_reset`, `provider_unavailable`,
  `provider_recovered`, `refresh_failed`.
- **Bildirim tipleri**: HighUsage, CriticalUsage, Exhausted, StatusIssue, SessionDepleted,
  SessionRestored. Eşikler global + sağlayıcı başına override
  (`provider_usage_thresholds: HashMap<String, UsageThresholdOverride>`).

---

## 4. ÖZELLİK KATALOĞU — seçim menüsü

> "Bizde" sütunu: ✅ var · 🟡 kısmen/iskele · ❌ yok
> Efor: CodexBridge'e (C#/WinUI) taşıma maliyeti tahmini.

### 4.A — Veri kaynağı ve sağlayıcılar

| # | Özellik | Win-CodexBar kaynağı | Bizde | Efor |
|---|---|---|---|---|
| **Ö-01** | Win-CodexBar `serve`'ünü arka uç olarak sarmalama (`/usage`+`/cost` → dashboard/v1) | `cli/serve/mod.rs` | 🟡 `WinCodexBarSource` yazıldı, canlı test edilmedi | S |
| **Ö-02** | **`/dashboard/v1/snapshot`'ı doğrudan tüketme** (çeviri gerekmez, şema aynı) | `serve/dashboard/snapshot.rs` | ❌ | XS |
| **Ö-03** | **PowerToys named pipe'ından okuma** (`\\.\pipe\WinCodexBar.Status`) | `powertoys.rs` | ❌ | S |
| **Ö-04** | JS sağlayıcı eklenti katmanı (ClearScript/V8) | üst akış `.js` dosyaları | ✅ kanıtlandı (xai/openai/perplexity) | — |
| **Ö-05** | Üst akıştan `.js` eklenti senkronizasyon aracı (fiyat/uç nokta güncellemesi) | — | ❌ | S |
| **Ö-06** | C# API-key stratejileri (~29 sağlayıcı: OpenRouter, DeepSeek, Groq, Venice…) | `providers/<id>/mod.rs` | ❌ | L |
| **Ö-07** | OAuth stratejileri (Claude, Codex, Copilot device flow, Gemini/Vertex gcloud) | `providers/claude/oauth/`, `copilot/device_flow.rs` | 🟡 sadece `ClaudeOAuthSource` | M |
| **Ö-08** | Yerel CLI/config okuyucular (Warp, OpenCode, JetBrains, Windsurf, Antigravity LSP) | `providers/<id>/local*.rs` | ❌ | M |
| **Ö-09** | Sağlayıcı kayıt mimarisi: tek fabrika + exhaustive enum (derleme zamanı güvenlik) | `core/provider_factory.rs` | ❌ (string id) | S |
| **Ö-10** | `SourceMode` çoklu kaynak + auto fallback sırası (`auto\|web\|cli\|oauth`) | `core/provider.rs` | ❌ | S |
| **Ö-11** | Çok hesap desteği (`token-accounts.json`, hesap başına paralel çekim, hesap değiştirme) | `core/token_accounts.rs` (885 satır) | ❌ | M |
| **Ö-12** | Codex çok hesap yönetimi + tepsi flyout'undan hesap değiştirme | `rust/src/codex_accounts/` | ❌ | M |
| **Ö-13** | Sağlayıcı durum sayfası (statuspage/Google incidents) polling | `rust/src/status/` | ❌ | S |
| **Ö-14** | HTTP proxy desteği (kimlikli) | `core/http_proxy.rs` + 4 ayar | ❌ | XS |

### 4.B — Kimlik bilgisi ve çerez

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-15** | Chrome/Edge/Brave çerez çıkarma (DPAPI + AES-256-GCM) | `rust/src/browser/cookies.rs` | ✅ `WindowsCookieStore` (sentetik doğrulandı) | — |
| **Ö-16** | **Firefox çerez desteği** (şifrelenmemiş SQLite) | `browser/detection.rs` | ❌ | S |
| **Ö-17** | v20 app-bound çerez (32 bayt başlık sıyırma) | — (bizde daha ileri) | ✅ + ⚠️ SYSTEM DPAPI katmanı COM `IElevator` gerektiriyor | — |
| **Ö-18** | Çerez önbelleği + tarayıcı kilidi/watchdog | `browser/cookie_cache.rs`, `watchdog.rs` | ❌ | S |
| **Ö-19** | Manuel çerez header yapıştırma (fallback) + `curl` sarmalayıcı temizleme | `providers/ollama/cookies.rs` | ❌ | XS |
| **Ö-20** | Sırların DPAPI ile korunması (`secure_file` katmanı) | `rust/src/secure_file.rs` | ❌ | S |
| **Ö-21** | Windows Credential Manager (keyring) entegrasyonu | `keyring = "3"` | ❌ | S |
| **Ö-22** | Log/diagnostik redaksiyonu (sır asla loga düşmez) | `core/redactor.rs` (252 satır) | ❌ | XS |
| **Ö-23** | WSL tespiti + Windows yol çözümleme | `rust/src/wsl.rs` | ❌ | XS |

### 4.C — Yüzeyler (UI)

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-24** | **Görev çubuğu içi band** (SetParent + Explorer-restart gözcüsü) | — (**bizde özgün**) | ✅ canlı kanıtlandı | — |
| **Ö-25** | Tepsi ikonu + piksel-seviye kullanım çubuğu ikonu | `rust/src/tray/render.rs` | ❌ | S |
| **Ö-26** | Tepsi popover paneli (sağlayıcı grid + kart) | `src/surfaces/TrayPanel` | ❌ | M |
| **Ö-27** | Pop-out / flyout büyük pano penceresi | `shell/flyout_window.rs` | ❌ | M |
| **Ö-28** | **FloatBar** — her zaman üstte, tıklama-geçirgen, opaklık/ölçek/yön ayarlı yüzen şerit | `src-tauri/src/floatbar/` + 10 ayar | ❌ (band'ımız farklı bir şey) | M |
| **Ö-29** | Ayarlar penceresi (8 sekme: general/providers/notifications/menuBar/menu/usageSpend/advanced/about) | `src/surfaces/settings/` | ❌ | L |
| **Ö-30** | Sağlayıcı sıralama (sürükle/düğme ile yeniden dizme) | `provider_order` ayarı | ❌ | S |
| **Ö-31** | Grafikler: bar/line chart, mini bar, pace grafiği, saatlik ısı haritası | `src/components/charts/` | ❌ | M |
| **Ö-32** | Tema (light/dark/auto) + pencere/tepsi ölçek yüzdesi | `theme`, `window_scale_percent` | ❌ | S |
| **Ö-33** | Global kısayol tuşu (menüyü aç) | `rust/src/shortcuts.rs` | ❌ | S |
| **Ö-34** | DPI/çoklu monitör/çalışma alanı değişiminde yeniden konumlanma | `shell/position.rs`, `window_positioner.rs` | ✅ (WndProc subclass) | — |
| **Ö-35** | Proof harness (env ile yüzey açma, otomasyon/CUA yakalama) | `proof_harness.rs`, `CODEXBAR_PROOF_MODE` | ❌ | S |

### 4.D — Bildirim ve otomasyon

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-36** | Windows toast bildirimleri (6 olay tipi) | `rust/src/notifications.rs` (37k) | 🟡 `NotificationEngine` diff motoru var, toast yok | S |
| **Ö-37** | Eşik sistemi: global + **sağlayıcı başına override** | `provider_usage_thresholds` | 🟡 global var | XS |
| **Ö-38** | Bildirim sesi: yerleşik temalar + olay başına özel WAV | `rust/src/sound.rs` (20k) | ❌ | S |
| **Ö-39** | **Harici hook motoru** — `hooks.json`, dar env whitelist, JSON stdin, 6 olay | `core/hooks.rs` (770) + `hook_transition.rs` (1111) | ❌ | M |
| **Ö-40** | `guard` komutu — kota eşiğine göre otomasyonu kapıla (çıkış kodu ile) | `cli/guard.rs` | ❌ | S |
| **Ö-41** | **Telefona push** (APNs + FCM, eşik geçişinde) | — (**bizde özgün**) | ✅ derleniyor, cihaz teslimi test edilmedi | — |
| **Ö-42** | Öngörücü pace uyarısı ("bu hızla resetten önce biter") | `core/usage_pace.rs` | ❌ | S |

### 4.E — Maliyet ve harcama

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-43** | **Yerel JSONL maliyet tarayıcı** (Codex + Claude oturum logları, artımlı, mtime/size cache) | `cost_scanner.rs` (67k satır!) | ❌ | L |
| **Ö-44** | models.dev fiyat kataloğu + yerleşik fiyatlar + tarih-kapılı geçmiş fiyatlar | `core/models_dev_pricing.rs`, `cost_pricing.rs` | ❌ | M |
| **Ö-45** | Özel fiyat overlay'i (`custom-pricing.json`, tam eşleşme) | `cost_pricing.rs` | ❌ | S |
| **Ö-46** | **Harcama sözleşmesi**: provenance (ListPriceEstimate/VendorMetered/Mixed/Unknown) + coverage — "bilinmiyor" asla "$0" olmuyor | `spend_contract.rs` (28k) | ❌ | M |
| **Ö-47** | Codex Workspaces: proje/oturum/model başına kullanım + SQLite sidecar | `codex_workspaces/` | ❌ | L |
| **Ö-48** | Proje bazlı harcama paneli + token karışımı + konuşma toplamları | `UsageSpendTab` | ❌ | M |
| **Ö-49** | OpenCodex `usage.jsonl` içe aktarma (request_id ile tekilleştirme, doğru aboneliğe yönlendirme) | `spend_contract/opencodex.rs` | ❌ | M |
| **Ö-50** | Haftalık "kaç oturum kotası kaldı" tahmini (medyan burn geçmişinden) | `core/session_equivalent_forecast.rs` (1238) | ❌ | M |
| **Ö-51** | 7 aşamalı pace motoru (OnTrack/SlightlyAhead/…/FarBehind) | `core/usage_pace.rs` | ❌ | S |

### 4.F — HTTP / entegrasyon yüzeyi

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-52** | `dashboard/v1` HTTP host (bearer, fails-closed, no-store) | `serve/mod.rs` | ✅ curl ile doğrulandı | — |
| **Ö-53** | **Gömülü HTML kullanım panosu** (`GET /` — tarayıcıdan aç) | `serve/dashboard/dashboard.html` + 61 SVG | ❌ | M |
| **Ö-54** | Kimlik detay modu (`--identity redacted\|full`, ayara bağlı) | `snapshot.rs:DashboardIdentity` | 🟡 daima maskeli | XS |
| **Ö-55** | Tek-atış snapshot komutu (`codexbar dashboard --output x.json`) | `cli/dashboard.rs` | ❌ | XS |
| **Ö-56** | CLI: `usage`/`cost`/`guard`/`diagnose`/`sessions`/`config`/`hooks`/`account`/`workspaces` | `rust/src/cli/` | ❌ | L |
| **Ö-57** | TOON v4.1 çıktı formatı (`--format toon`) | `cli/toon.rs` | ❌ | S |
| **Ö-58** | Güvenli diagnostik dışa aktarımı (sır yok, sadece metadata) | `cli/diagnose.rs` | ❌ | S |
| **Ö-59** | Ajan oturumları: yerel + **SSH üzerinden uzak** Codex/Claude/Pi oturum listesi + odaklama | `agent_sessions.rs` (30k) | ❌ | L |
| **Ö-60** | Çok makineli birleşik görünüm (maliyet toplanır, kota tekilleştirilir) | — (**bizde özgün**) | ✅ `HttpUsageSource` | — |
| **Ö-61** | Stream Deck entegrasyonu (`serve` panosunu tüketiyor) | 3. parti | ❌ | — |

### 4.G — Sistem / platform

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-62** | **Adaptif yenileme** (2–30 dk karar tablosu) | `core/adaptive_refresh.rs` | ✅ C#'a çevrildi, 7 assertion | — |
| **Ö-63** | Düşük güç modu Off/On/**Automatic** (Windows Battery Saver okur) | `LowPowerModePreference` | 🟡 girdi var, okuyan yok | XS |
| **Ö-64** | Kodlama etkinliği tespiti (yerel ajan süreçleri → yenileme hızlandırma) | `coding_activity.rs` | 🟡 `RefreshContext` alanı var, dolduran yok | S |
| **Ö-65** | Boot'ta otomatik başlatma (`HKCU\...\Run`) | `settings.rs` + `cli/autostart.rs` | ❌ | XS |
| **Ö-66** | Otomatik güncelleme: GitHub Releases + **SHA-256 yeniden doğrulama** + kanal seçimi | `rust/src/updater.rs` (30k) | ❌ | M |
| **Ö-67** | i18n — 8 dil (`.ftl`, fluent) + drift kontrol scripti | `rust/src/locale/`, `check-locale-drift.mjs` | ❌ | M |
| **Ö-68** | Kişisel bilgileri gizle (`hide_personal_info`) | ayar | 🟡 daima maskeli | XS |
| **Ö-69** | Widget snapshot dışa aktarımı (harici entegrasyonlar için serileştirilmiş durum) | `core/widget_snapshot.rs` | ❌ | S |

### 4.H — Dağıtım ve süreç

| # | Özellik | Kaynak | Bizde | Efor |
|---|---|---|---|---|
| **Ö-70** | Inno Setup kurulumu + portable exe + SHA-256 sidecar | `scripts/windows-release-build.ps1` | ❌ | M |
| **Ö-71** | WebView2 + VC++ runtime bootstrap | installer | ❌ (WinUI'de farklı) | S |
| **Ö-72** | winget dağıtımı | `microsoft/winget-pkgs` manifest | ❌ | S |
| **Ö-73** | Kod imzalama politikası (SignPath Foundation, ücretsiz OSS) | `docs/CODE_SIGNING.md`, `.signpath/` | ❌ | M |
| **Ö-74** | CI bütçe modu (`CI_BUDGET_MODE: normal\|thin\|off`) + docs-only atlama | `CONTEXT.md`, `.circleci/config.yml` | 🟡 sadece android.yml | S |
| **Ö-75** | Yerel CI dilimi (`local-check.ps1 -Slice ci` — hosted gate'i birebir aynalar) | `scripts/local-check.ps1` | 🟡 SelfTest konsolu | S |
| **Ö-76** | ADR pratiği (`docs/adr/0001..0005`) | `docs/adr/` | 🟡 `decisions.md` | XS |
| **Ö-77** | Üst akış port kapanış denetimi (her commit için PORT/ALREADY/SKIP + kanıt) | `.review/porting-*.md` | ❌ | S |
| **Ö-78** | Gizlilik politikası belgesi (uçtan uca veri akışı beyanı) | `docs/PRIVACY.md` | ❌ | XS |
| **Ö-79** | Katkıcı etkileşim koruması (hesap yaşı + PR hız limiti) | `.github/scripts/interaction-guard.mjs` | ❌ | S |

---

## 5. CodexBridge mevcut durumu (dürüst envanter)

### Gerçekten çalışan / kanıtlanan
- **Görev çubuğu band'ı** — `SetParent(Shell_TrayWnd)`, krom sıyırma, DPI yeniden konumlanma.
  Kullanıcı makinesinde canlı doğrulandı.
- **Explorer-restart hayatta kalma** — `TaskbarWatchdog` (üst-seviye gizli pencere,
  `TaskbarCreated` broadcast). Deskband11'in çözemediği sorun. Canlı GEÇTİ.
- **dashboard/v1 host** — curl ile 401/200 doğrulandı; DNS-rebinding koruması, fails-closed bearer,
  sabit zamanlı SHA-256 token karşılaştırma, `no-store`.
- **JS sağlayıcı çalıştırma** — gerçek `xai.js` ClearScript/V8'de çalıştı (11/11 prob).
- **Çerez kripto** — Chrome/Edge DPAPI + AES-GCM v10, sentetik veriyle doğrulandı; v20 app-bound
  başlık sıyırma implemente.
- **Push** — APNs (ES256) + FCM (RS256→OAuth2) tam implemente, 0 hata derleniyor.
- **SelfTest** — 22/22 assertion geçiyor.

### İskele / doğrulanmamış
- Faz 2 `WinCodexBarSource` — saf eşleme fonksiyonu test edildi, **canlı Win-CodexBar'a hiç bağlanmadı**.
- Telefon widget'ları — Kotlin/Swift yazıldı, **hiçbir cihazda derlenmedi**. (Android için CI eklendi.)
- v20 çerez SYSTEM DPAPI katmanı — COM `IElevator` gerekiyor, `null` dönüyor.
- Push cihaz teslimi — gerçek `.p8` / service account ile denenmedi.

### Hiç yok
Kalıcı ayar deposu, tepsi ikonu, ayarlar UI'ı, bildirim toast'ı, maliyet takibi, i18n, kurulum
paketi, otomatik güncelleme, CLI, gerçek sağlayıcı çeşitliliği.

---

## 6. Fark matrisi — kim neyi yapıyor

| Yetenek | Win-CodexBar | CodexBridge |
|---|---|---|
| Görev çubuğu **içinde** ölçer | ❌ (FloatBar ayrı pencere) | ✅ **tek** |
| Telefon widget (iOS/Android) | ❌ | 🟡 iskele — **tek** |
| Host→telefon push (APNs/FCM) | ❌ | ✅ **tek** |
| Çok makineli birleşik görünüm | ❌ | ✅ **tek** |
| v20 app-bound çerez | ❌ | 🟡 kısmi — **tek** |
| `dashboard/v1` HTTP | ✅ | ✅ (paralel) |
| 70 sağlayıcı | ✅ | ❌ |
| Yerel maliyet takibi | ✅ (67k satır) | ❌ |
| Ayarlar UI / i18n / kurulum / güncelleme | ✅ | ❌ |
| Tepsi ikonu + popover | ✅ | ❌ |
| Hook / CLI / guard otomasyonu | ✅ | ❌ |

**Özet:** 4 özgün yeteneğimiz var, hepsi *yüzey* ve *dağıtım* tarafında; veri tarafında sıfıra yakınız.

---

## 7. Doğrudan alınabilir varlıklar (MIT — kopyala/uyarla)

| Varlık | Dosya | Nasıl alınır |
|---|---|---|
| Tray/band çubuk çizim algoritması | `rust/src/tray/render.rs` | Mantığı C#/Win2D'ye çevir (~150 satır) |
| 61 sağlayıcı SVG ikonu | `rust/src/cli/serve/dashboard/icons/` | Doğrudan kopyala |
| HTML pano | `serve/dashboard/dashboard.html` | Doğrudan kopyala, veri kaynağını değiştir |
| Pace karar tablosu | `core/usage_pace.rs` | Saf fonksiyon → C# çevirisi (AdaptiveRefresh gibi) |
| Oturum-eşdeğeri tahmin | `core/session_equivalent_forecast.rs` | Saf; medyan burn algoritması |
| Redaksiyon yardımcıları | `core/redactor.rs` | Saf; C#'a 1:1 |
| Hook payload sözleşmesi | `core/hooks.rs` | JSON şeması + env whitelist listesi |
| serve güvenlik karar tablosu | `serve/mod.rs:165` | Zaten uyguladık; tabloyu doğrulama için kullan |
| Gizlilik politikası şablonu | `docs/PRIVACY.md` | Uyarla |
| Sağlayıcı-uç nokta bilgisi | `providers/<id>/mod.rs` | Okuma referansı; JS eklentileri zaten hazır |

**Lisans notu:** MIT → türev çalışma serbest, telif bildirimi korunmalı. Kod kopyalanan
dosyalara üstte kaynak + MIT atfı yaz; kökte `NOTICE` tut.

---

## 8. Teknik tuzaklar (her iki taraftan derlenmiş)

### Bizim öğrendiklerimiz (`decisions.md`)
1. **SAC enforce**: `dotnet run` / `dotnet test` imzasız DLL'i `0x800711C7` ile engelliyor →
   **daima derlenmiş apphost `.exe`'yi çalıştır**. Testler bu yüzden konsol assertion'ı.
2. **Ölü pencereye `Window.Close()`** → yakalanamayan native segfault (exit 139).
   Explorer-restart'ta referansı null'la, yeni pencere kur.
3. **`ExtendsContentIntoTitleBar=true` yetmiyor** → `OverlappedPresenter.SetBorderAndTitleBar(false,false)`
   **VE** Win32 `WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MIN/MAXBOX` sıyırma gerek.
4. **Delegate'i alanda tut** (WndProc, subclass) — yoksa GC toplar, fonksiyon işaretçisi çöker.
5. **ClearScript**: `[ScriptMember]` ile host metotları `is not a function` veriyor →
   `host` nesnesini JS'de kur, C# delegate'lerine bağla. Sonucu `JSON.stringify` ile marshal et.

### Win-CodexBar'ın öğrendikleri (bizim için geçerli)
6. **WebView2 paylaşımlı profil** → tema `auto`'da bir pencere diğerlerinin temasını çeviriyor.
   (WinUI'de doğrudan geçerli değil ama WebView2 kullanırsak geçerli.)
7. **WSL'de Chromium DPAPI çözülemez** → manuel çerez veya CLI auth şart.
8. **Tarayıcı açıkken çerez DB kilitli** → retry + kullanıcıya "tarayıcıyı kapat" mesajı.
9. **Token yakan probe'lar**: Azure OpenAI ve Doubao gerçek `max_tokens:1` inference isteği atıyor;
   Doubao'da probe ölçtüğü kotadan düşüyor. **Varsayılan KAPALI + açık uyarı.**
   (`00-MIMARI` §9'daki etik kural — Win-CodexBar kodunda da bu sağlayıcılar ayrı ele alınıyor.)
10. **Fiyatlandırma tarih-kapılı olmalı** (GPT-5.6 Terra/Luna örneği: 2026-07-30 öncesi/sonrası
    farklı oran). Sabit fiyat tablosu geçmiş veriyi yanlış hesaplar.
11. **"Bilinmiyor" ≠ "$0"** — kapsam kısmiyse provenance/coverage göster, sahte toplam üretme.
12. **Kotayı sağlayıcılar arası karıştırma** — A sağlayıcısının e-posta/plan bilgisi B'nin UI'ında
    asla görünmemeli.

---

## 9. "Sıfırdan başlıyoruz" — üç seçenek

### Seçenek A — İnce istemci (Win-CodexBar arka uç)
CodexBridge = görev çubuğu band'ı + telefon köprüsü + push. Veri **tamamen** Win-CodexBar'dan
(`Ö-02` snapshot veya `Ö-03` named pipe).

- ➕ İlk sürüme **haftalar**, 70 sağlayıcı bedava, güncel kalır
- ➖ Üçüncü taraf ikiliye bağımlılık; kullanıcı iki uygulama kurar; sağlayıcı hatalarını düzeltemeyiz
- **Efor:** `Ö-02` + `Ö-03` + `Ö-25` + `Ö-65` + `Ö-70` ≈ 2–3 hafta

### Seçenek B — Hibrit (mevcut plan, düzeltilmiş)
Win-CodexBar varsa ondan oku, yoksa kendi JS/C# katmanımıza düş. `IUsageSource` zaten bu soyutlama.

- ➕ Bağımsızlık yolu açık, hemen değer üretir, kademeli göç
- ➖ İki veri yolu = iki test yüzeyi
- **Efor:** A + `Ö-04`(var) + `Ö-05` + `Ö-20` + `Ö-36` ≈ 5–7 hafta
- 🎯 **Tavsiye edilen.** `00-MIMARI` §6'daki "B, A ile başlayarak" kararı hâlâ geçerli;
  tek fark artık A'nın çok daha ucuz bir kapısı var (`Ö-02`/`Ö-03`).

### Seçenek C — Tam bağımsız ürün
70 sağlayıcı, maliyet taraması, ayarlar UI, i18n, kurulum, güncelleme — hepsi C#'ta.

- ➕ Tek uygulama, tam kontrol
- ➖ 113k satırlık bir açık; `Ö-43` tek başına 67k satır
- **Efor:** 6+ ay. `00-MIMARI` §6'da zaten "Hayır" denmişti — bu analiz o kararı doğruluyor.

### Ne olursa olsun yapılması gerekenler
Hangi seçenek seçilirse seçilsin şunlar taban: `Ö-20` (sır koruma), `Ö-22` (redaksiyon),
`Ö-65` (autostart), `Ö-70` (kurulum), `Ö-63` (düşük güç), `Ö-36` (toast).
Bunlar olmadan "ürün" değil "demo" olur.

---

## 10. Açık sorular (senin kararın gereken)

1. **Kullanıcı iki uygulama kurmayı kabul eder mi?** Seçenek A/B'nin tek gerçek sorusu bu.
   "CodexBridge, Win-CodexBar'ın görev çubuğu eklentisi" diye konumlanmak meşru bir üründür.
2. **Ana yüzey band mi, telefon mu?** İkisini birden cilalamak için kaynak yok; hangisi önce?
3. **Sağlayıcı hedefi kaç?** 3 (Claude/Codex/Cursor) yeterli mi, yoksa "hepsi" mi gerekiyor?
   Cevap doğrudan Seçenek A↔C ekseninde yer belirliyor.
4. **Kimin için?** Sadece sen mi kullanacaksın, yoksa dağıtılacak mı? Dağıtılacaksa
   `Ö-70`/`Ö-72`/`Ö-73` (kurulum/winget/imza) opsiyonel değil, zorunlu.
5. **Win-CodexBar'a katkı bir seçenek mi?** Band'ı oraya PR olarak eklemek (MIT, aktif depo)
   bakım yükünü sıfırlar ama ürün sahipliğini bırakır.

---

## 11. Bu raporun tetiklediği belge güncellemeleri

- [ ] `docs/00-MIMARI-VE-YOL-HARITASI.md` §2 — "Windows'ta dashboard/v1 host'u yok" iddiası artık yanlış
- [ ] `.claude/knowledge/decisions.md` 2026-08-06 girdisi — "`/dashboard/v1/snapshot` YOK" notu eskimiş
- [ ] `docs/00-...` §11 açık soru #1 (lisans) — kapandı, MIT
- [ ] `docs/00-...` §11 açık soru #3 (prelude/JavaScriptCore) — kapandı, V8'de çalışıyor
- [ ] `README.md` "Neden bu var" bölümü — farklılaşma gerekçesi yeniden yazılmalı
