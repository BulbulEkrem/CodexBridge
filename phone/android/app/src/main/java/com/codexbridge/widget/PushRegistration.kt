package com.codexbridge.widget

import io.ktor.client.HttpClient
import io.ktor.client.request.delete
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import kotlinx.serialization.Serializable

/**
 * Faz 7: Android cihazının FCM registration token'ını CodexBridge host'una kaydeder.
 *
 * Host, kota eşiği aşıldığında (uyarı/kritik/sıfırlama) buraya push iter — böylece Glance
 * widget'ının min 15 dk yenileme kısıtı önemli anları kaçırmaz (host uyandırır).
 *
 * Kullanım (FirebaseMessagingService.onNewToken):
 *   override fun onNewToken(token: String) {
 *       scope.launch { PushRegistration(http, host).register(token) }
 *   }
 */
class PushRegistration(
    private val http: HttpClient,
    private val host: DashboardClient.Host,
) {
    @Serializable
    private data class Body(val token: String, val platform: String, val label: String? = null)

    /** FCM token'ını host'a kaydeder (idempotent). */
    suspend fun register(fcmToken: String, label: String? = null) {
        http.post("${host.baseUrl.trimEnd('/')}/dashboard/v1/devices") {
            contentType(ContentType.Application.Json)
            host.token?.let { header(HttpHeaders.Authorization, "Bearer $it") }
            setBody(Body(fcmToken, "fcm", label))
        }
    }

    /** Kaydı siler (bildirim kapatıldığında / oturum kapandığında). */
    suspend fun unregister(fcmToken: String) {
        http.delete("${host.baseUrl.trimEnd('/')}/dashboard/v1/devices") {
            contentType(ContentType.Application.Json)
            host.token?.let { header(HttpHeaders.Authorization, "Bearer $it") }
            setBody(Body(fcmToken, "fcm"))
        }
    }
}
