# Faz 7 — Push Bildirimi (host → telefon) + v20 app-bound çerez

> Durum: **kod tamamlandı, 0 hata derleniyor** · gerçek APNs/FCM kimlik bilgisiyle canlı test bekliyor
> Tarih: 2026-08-07

## Neyi çözer

Telefon widget'larının yenileme bütçesi dar (iOS ~40–70/gün, Android min 15 dk). Kota tükenmesi
gibi önemli anları yakalamak için telefonun sık sık yoklaması pahalı ve yavaş. Çözüm: **host,
kota eşiği aşıldığında telefonu uyandırır.** Telefon pasif dinler, host itiverir.

## Mimari

```
SnapshotCache ──► NotificationEngine.Diff(prev, cur) ──► [NotificationEvent...]
   (tek yenileme)         (saf, Core)                           │
                                                                 ▼
                                    PushNotificationService (arka plan, cooldown'lu)
                                                                 │
                                            CompositePushDispatcher (platforma göre yönlendir)
                                              ├─ ApnsPushDispatcher  (iOS, HTTP/2 + ES256 JWT)
                                              ├─ FcmPushDispatcher   (Android, OAuth2 + RS256 JWT)
                                              └─ LoggingPushDispatcher (kimlik bilgisi yoksa yedek)
```

### Bileşenler

| Katman | Tip | Sorumluluk |
|---|---|---|
| Core | `NotificationEvent`, `NotificationKind`, `NotificationThresholds` | Platformsuz olay modeli |
| Core | `NotificationEngine.Diff` | İki snapshot'ı karşılaştırıp **eşik geçişlerini** üretir (saf fonksiyon) |
| Host | `IDeviceRegistry` / `JsonFileDeviceRegistry` | Telefon push token'larını dosyada tutar (atomik yazım) |
| Host | `IPushDispatcher` + APNs/FCM/Logging | Bir olayı tek cihaza iter |
| Host | `PushNotificationService` (`BackgroundService`) | Periyodik tarama + cooldown + fan-out + ölü cihaz temizliği |
| Telefon | `PushRegistration` (iOS/Android) | Cihaz token'ını `/dashboard/v1/devices`'a kaydeder |

## Eşik mantığı (kenar tetikleme)

Yalnızca **eşik geçişlerinde** olay üretilir — aynı yüksek kullanım her yenilemede yeniden
bildirmesin diye önceki değer eşiğin altında, şimdiki üstünde olmalı:

- **QuotaWarning** — kullanım %75'i yukarı geçti.
- **QuotaCritical** — %90'ı yukarı geçti (uyarıyı gölgeler, aynı turda ikisi birden gitmez).
- **QuotaReset** — önce ≥%75 iken şimdi <%50'ye düştü (histerezis).
- **ProviderError / ProviderRecovered** — hata durumuna giriş/çıkış.

İlk snapshot'ta (taban yok) hiç olay üretilmez → açılışta bildirim yağmuru olmaz.

**Spam koruması:** her `dedupeKey` (`provider:quota:window:kind`) için cooldown penceresi
(varsayılan 30 dk). Uyarı ve kritik ayrı anahtarlarda: uyarıdan sonra kritik de gidebilir.

## HTTP uç noktaları (host)

- `POST /dashboard/v1/devices` — `{ "token": "...", "platform": "apns"|"fcm", "label"?: "..." }` (bearer)
- `DELETE /dashboard/v1/devices` — `{ "token": "..." }` (bearer)

Kayıt token'ları hassastır → yalnızca `%LOCALAPPDATA%\CodexBridge\devices.json`'da tutulur,
snapshot'a/loga sızmaz.

## Yapılandırma (env / CLI)

| Env | CLI | Varsayılan | Açıklama |
|---|---|---|---|
| `CODEXBRIDGE_SOURCE` | `--source` | `fake` | `fake` \| `http` (başka dashboard/v1 host'undan oku) |
| `CODEXBRIDGE_SOURCE_URL` | `--source-url` | — | http kaynağı taban URL'i |
| `CODEXBRIDGE_SOURCE_TOKEN` | `--source-token` | — | http kaynağı bearer |
| `CODEXBRIDGE_PUSH_ENABLED` | `--push` | `true` | Push arka plan servisi |
| `CODEXBRIDGE_PUSH_COOLDOWN_MIN` | `--push-cooldown` | `30` | dedupeKey cooldown (dk) |
| `CODEXBRIDGE_DEVICES_PATH` | `--devices-path` | LocalAppData | Cihaz deposu yolu |
| `CODEXBRIDGE_APNS_KEY_ID` / `_TEAM_ID` / `_BUNDLE_ID` / `_P8_PATH` / `_SANDBOX` | — | — | APNs token auth |
| `CODEXBRIDGE_FCM_SERVICE_ACCOUNT` | — | — | FCM service account JSON yolu |

APNs veya FCM yapılandırılmamışsa dispatcher **loga düşer** — boru hattı kimlik bilgisi olmadan
da uçtan uca çalışır ve doğrulanabilir.

## v20 app-bound çerez

Faz 6 v10'u tam çözüyordu; bu turda **v20 (Chrome 127+ app-bound)** eklendi:

- **Çözme yolu (`WindowsCookieStore`):** v20 önekli değerler ayrı **app-bound anahtarla** çözülür;
  çözülen düz metnin ilk **32 baytı** app-bound başlıktır ve sıyrılır. `IsV20` ile sürüm ayrımı.
- **Anahtar (`AppBoundKeyProvider`):** `Local State → os_crypt.app_bound_encrypted_key` okunur,
  `"APPB"` öneki atılır, kullanıcı DPAPI katman(lar)ı en iyi çabayla soyulur.
- **Bilinçli sınır:** app-bound anahtarın **SYSTEM DPAPI** katmanı yalnızca SYSTEM olarak çalışan
  `IElevator` COM sunucusuyla açılır. Bu, yükseltme ister ve otonom oturumda çalıştırılmaz →
  çözülemezse `null` döner, v20 çerezleri sessizce atlanır (**v10 yolu etkilenmez**). COM elevator
  entegrasyonu gelecek tur.

## Doğrulama

- **SelfTest** (Core): NotificationEngine eşik geçişleri — uyarı/kritik/sıfırlama/hata/kurtarma,
  kenar tetikleme, dedupe ayrımı (10 yeni assertion).
- **JsProbe** (JsHost): v20 biçim öneki + `IsV20` + 32 bayt başlık sıyırma + yanlış anahtar reddi
  (sentetik veriyle, gerçek çerezlere dokunmadan — 4 yeni prob).

## Kalan (canlı, kullanıcı makine başında)

- Gerçek `.p8` (APNs) / service account (FCM) ile cihaza push teslimi.
- iOS `didRegisterForRemoteNotifications` + Android `FirebaseMessagingService.onNewToken`
  akışlarının uygulama kabuğuna bağlanması.
- v20 için COM `IElevator` ile SYSTEM katman çözümü.
