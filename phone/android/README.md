# CodexBridge Android Widget (Glance)

`dashboard/v1` konuşan bir Android AppWidget'ı. Ekranda sağlayıcı kotası + **veri yaşı** gösterir
("%28 · 12 dk önce"). Canlı gösterge değildir: WorkManager ile en az 15 dakikada bir yenilenir;
acil bilgi PC'den **push** ile gelir (Faz 7, opsiyonel).

> Bu proje **temiz bir ortamda ilk denemede derlenecek** şekilde hazırlanmıştır. Derlemek için
> yalnızca JDK 17 + Android SDK gerekir (bu geliştirme ortamında ikisi de yoktu, burada derlenmedi).

## Yığın / sürümler

| Bileşen | Sürüm |
|---|---|
| Android Gradle Plugin | 8.5.2 |
| Kotlin | 1.9.24 |
| Compose compiler extension | 1.5.14 |
| Gradle | 8.7 |
| compileSdk / targetSdk | 34 |
| minSdk | 26 |
| Jetpack Glance appwidget | 1.1.0 |
| WorkManager (work-runtime-ktx) | 2.9.1 |
| kotlinx-serialization-json | 1.7.3 |
| Ktor (client-android + content-negotiation + kotlinx-json) | 2.3.12 |
| DataStore (preferences) | 1.1.1 |

`applicationId` / `namespace`: `com.codexbridge.widget`. Bağımlılıklar version catalog'da
(`gradle/libs.versions.toml`).

## Proje yapısı

```
phone/android/
  settings.gradle.kts          ← :app modülü
  build.gradle.kts             ← kök (plugin'ler apply false)
  gradle.properties            ← AndroidX açık, kotlin.code.style=official
  gradle/
    libs.versions.toml         ← version catalog
    wrapper/gradle-wrapper.properties  ← Gradle 8.7 (JAR aşağıdaki nota bkz.)
  gradlew / gradlew.bat        ← wrapper başlatıcıları
  app/
    build.gradle.kts
    proguard-rules.pro
    src/main/AndroidManifest.xml
    src/main/java/com/codexbridge/widget/
      DashboardModels.kt         dashboard/v1 şeması (kotlinx.serialization)
      DashboardClient.kt         snapshot çekme + çok-host birleştirme
      Support.kt                 DashboardCodec + HostConfig (SharedPreferences)
      HttpClientFactory.kt       ortak Ktor istemcisi (ContentNegotiation json)
      RefreshWorker.kt           WorkManager worker: çek → DataStore → widget güncelle
      RefreshScheduler.kt        periyodik (15 dk) + anında yenileme planlama
      UsageWidget.kt             Glance widget (yüzde + veri yaşı)
      UsageWidgetReceiver.kt     GlanceAppWidgetReceiver (manifest'te tanımlı)
      MainActivity.kt            yapılandırma (host+token) + tanılama ekranı
      PushRegistration.kt        POST/DELETE /dashboard/v1/devices (opsiyonel push)
      CodexMessagingService.kt   FCM servisi (opsiyonel — aşağıya bkz.)
    src/main/res/
      xml/usage_widget_info.xml  appwidget-provider meta (updatePeriodMillis=0)
      layout/activity_main.xml
      values/{strings,themes,colors}.xml
      drawable/{ic_launcher_foreground,widget_preview}.xml
      mipmap-anydpi-v26/{ic_launcher,ic_launcher_round}.xml
```

## Derleme ve APK

```bash
cd phone/android
./gradlew assembleDebug        # Windows: gradlew.bat assembleDebug
```

Çıktı APK:

```
phone/android/app/build/outputs/apk/debug/app-debug.apk
```

Release için `./gradlew assembleRelease` → `app/build/outputs/apk/release/`.

## Gradle wrapper JAR'ı nasıl gelir?

`gradle-wrapper.jar` **binary** olduğu için bu iskeletle birlikte elle üretilmedi. Şu yollardan
biriyle gelir (hepsi otomatik, ek elle iş yok) ve `.gitignore` bu JAR'ı **hariç tutmaz**:

- **Android Studio**: projeyi açtığında wrapper'ı otomatik tamamlar/senkronlar.
- **Yerelde Gradle kuruluysa**: `gradle wrapper --gradle-version 8.7` JAR + betikleri üretir.
- **CI**: `gradle/actions/setup-gradle` ya da `gradle/wrapper-validation-action` ile checkout'ta gelir.

JAR üretildikten sonra `./gradlew` doğrudan çalışır.

## Yapılandırma + tanılama ekranı (MainActivity)

Uygulama açılınca host adresi (ör. `http://100.x.x.x:8765`) ve Bearer token girilir; değerler
`HostConfig` üzerinden SharedPreferences'a yazılır (`RefreshWorker`/`DashboardClient` buradan okur).
"Kaydet ve yenile" periyodik işi planlar ve bir kez anında çeker.

Aynı ekranda **"Widget güncellenmiyor mu?"** tanılaması ve ilgili sistem ayarlarına giden
intent'ler bulunur:

- **Pil optimizasyonu** (`ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS`) — üretici pil
  yöneticileri periyodik işi öldürebilir.
- **Veri kullanımı / Data Saver** (`ACTION_DATA_USAGE_SETTINGS`) — Data Saver açıkken arka
  planda ağ erişimi kesilir, widget sessizce eskir.
- **Uygulama ayarları** (`ACTION_APPLICATION_DETAILS_SETTINGS`) — arka plan verisi kısıtları.

Bu iki tuzağa (Data Saver + pil optimizasyonu) karşı asıl savunma: widget **her zaman veri yaşını
gösterir**, böylece kullanıcı verinin eskidiğini fark eder.

## Push (opsiyonel — google-services.json ile aktifleşir)

Push kodu (`PushRegistration`, `CodexMessagingService`) **her zaman derlenir**; `firebase-messaging`
sabit bağımlılıktır. Ancak `google-services.json` yoksa FirebaseApp başlatılmaz → FCM hiç tetiklenmez,
**widget + WorkManager akışı bundan bağımsız çalışır**.

Push'u aktifleştirmek için:

1. Firebase Console'da bir Android uygulaması (paket adı `com.codexbridge.widget`) oluştur.
2. İndirilen **`google-services.json`** dosyasını `phone/android/app/` altına koy.
3. Yeniden derle. `app/build.gradle.kts` bu dosyayı görünce `com.google.gms.google-services`
   plugin'ini **otomatik uygular** (dosya yoksa uygulanmaz, build kırılmaz).

Aktifken `CodexMessagingService.onNewToken` FCM token'ını host'a kaydeder
(`POST /dashboard/v1/devices { token, platform:"fcm", label }`, girilen host+token ile). Host,
kota eşiği aşıldığında push iter; gelen push widget'ı anında tazeler.

> `google-services.json` gizli sayılmaz ama depoya eklemek istemiyorsan `.gitignore`'a sen ekleyebilirsin.

## dashboard/v1 sözleşmesi

`GET {host}/dashboard/v1/snapshot` (`Authorization: Bearer <token>`). Şema
`docs/00-MIMARI-VE-YOL-HARITASI.md` §7. `staleAfterSeconds` sunucudan gelir (telefonda sabit
kodlanmaz). Çok-host: **maliyet toplanır, kota yüzdeleri (aynı id) tekilleştirilir**.

## Gereken dış girdiler (özet)

| Girdi | Zorunlu mu? | Nereden |
|---|---|---|
| JDK 17 + Android SDK (compileSdk 34) | Evet | Android Studio / SDK manager |
| `gradle-wrapper.jar` | Evet | Android Studio / `gradle wrapper` / CI (yukarı bkz.) |
| `app/google-services.json` | Hayır (yalnızca push için) | Firebase Console |
