package com.codexbridge.widget

import android.os.Build
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * OPSİYONEL push servisi (Faz 7).
 *
 * Bu sınıf her zaman derlenir (firebase-messaging bağımlılığı sabit), ancak yalnızca
 * google-services.json eklenip FirebaseApp başlatıldığında tetiklenir. json yoksa FCM hiç
 * çalışmaz; widget + WorkManager akışı bundan bağımsız çalışmaya devam eder.
 *
 * Görevi: FCM registration token'ını CodexBridge host'una kaydetmek
 * (POST /dashboard/v1/devices { token, platform:"fcm", label }). Host, kota eşiği aşıldığında
 * buraya push iter → Glance'in min 15 dk yenileme kısıtına takılmadan önemli anlar iletilir.
 */
class CodexMessagingService : FirebaseMessagingService() {

    private val scope = CoroutineScope(Dispatchers.IO)

    override fun onNewToken(token: String) {
        val host = HostConfig.primary(applicationContext) ?: return // henüz yapılandırılmadı
        val label = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
        scope.launch {
            val http = HttpClientFactory.create()
            try {
                PushRegistration(http, host).register(token, label)
            } catch (_: Exception) {
                // Kayıt başarısızsa sessiz geç; bir sonraki token yenilenmesinde tekrar denenir.
            } finally {
                http.close()
            }
        }
    }

    override fun onMessageReceived(message: RemoteMessage) {
        // Push geldiğinde widget'ı hemen tazele (eşik/sıfırlama anını yakala).
        RefreshScheduler.refreshNow(applicationContext)
    }
}
