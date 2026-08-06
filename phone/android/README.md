# CodexBridge Android Widget (Glance) — iskele

> Derlenmedi (Android SDK yok). Android Studio'da bir "Empty" projeye taşınıp bağımlılıklar
> eklenerek derlenmeli.

## Yığın
- **Jetpack Glance** (Compose tabanlı AppWidget) — ekranda yüzde çubuğu + veri yaşı
- **WorkManager** `PeriodicWorkRequest` (min 15 dk) — `updatePeriodMillis = 0`, güncelleme Worker'a devredilir
- **Ktor/OkHttp + kotlinx.serialization** — `dashboard/v1` çekme

## Gradle bağımlılıkları (özet)
```kotlin
implementation("androidx.glance:glance-appwidget:1.1.0")
implementation("androidx.work:work-runtime-ktx:2.9.1")
implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")
implementation("io.ktor:ktor-client-android:2.3.12")
```

## Dosyalar (paket `com.codexbridge.widget`)
| Dosya | Sorumluluk |
|---|---|
| `DashboardModels.kt` | `dashboard/v1` şemasının kotlinx.serialization karşılığı |
| `DashboardClient.kt` | `{host}/dashboard/v1/snapshot` çağrısı (Bearer), çok-host birleştirme kancası |
| `UsageWidget.kt` | Glance widget — sağlayıcı yüzdeleri + **veri yaşı** rozeti |
| `RefreshWorker.kt` | WorkManager worker — snapshot çeker, DataStore'a yazar, widget'ı günceller |

## Android'e özgü iki tuzak (tasarım gereksinimi)
1. **Data Saver** açıkken arka planda ağ erişimi yok → widget sessizce eskir.
2. **Üretici pil optimizasyonları** (Xiaomi/Huawei/Samsung) periyodik işi öldürebilir.

İkisinin de karşılığı: widget **her zaman veri yaşını göstersin** + uygulama içi bir
**tanılama ekranı** ("Widget güncellenmiyor mu? Data Saver / pil optimizasyonu / arka plan
verisi izinlerini kontrol et", ilgili sistem ayarına intent).
