package com.codexbridge.widget

import android.content.Context
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

/** Sağlayıcı satırlarını DataStore'da saklamak için JSON kodlama. */
object DashboardCodec {
    private val json = Json { ignoreUnknownKeys = true }
    private val rowsSerializer = ListSerializer(ProviderRow.serializer())
    fun encodeRows(rows: List<ProviderRow>): String = json.encodeToString(rowsSerializer, rows)
    fun decodeRows(text: String): List<ProviderRow> =
        runCatching { json.decodeFromString(rowsSerializer, text) }.getOrDefault(emptyList())
}

/**
 * Kullanıcının eklediği host(lar). MainActivity yazar, DashboardClient/RefreshWorker okur.
 *
 * Not: token hassastır; üretimde EncryptedSharedPreferences'a taşınmalı. Şimdilik düz
 * SharedPreferences (senkron okuma WorkManager worker'ında pratik). QR ile eşleştirme akışı
 * ileride bu değerleri doldurabilir.
 */
object HostConfig {
    private const val PREFS = "codexbridge_config"
    private const val KEY_HOST_URL = "host_url"
    private const val KEY_TOKEN = "token"

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Yapılandırılmış host(lar). Boşsa widget "veri yok" gösterir. */
    fun load(context: Context): List<DashboardClient.Host> {
        val p = prefs(context)
        val url = p.getString(KEY_HOST_URL, null)?.trim().orEmpty()
        if (url.isEmpty()) return emptyList()
        val token = p.getString(KEY_TOKEN, null)?.trim()?.ifEmpty { null }
        return listOf(DashboardClient.Host(baseUrl = url, token = token))
    }

    /** İlk (birincil) host — push kaydı ve tanılama için. */
    fun primary(context: Context): DashboardClient.Host? = load(context).firstOrNull()

    fun save(context: Context, hostUrl: String, token: String?) {
        prefs(context).edit()
            .putString(KEY_HOST_URL, hostUrl.trim())
            .putString(KEY_TOKEN, token?.trim().orEmpty())
            .apply()
    }

    fun currentUrl(context: Context): String = prefs(context).getString(KEY_HOST_URL, "").orEmpty()
    fun currentToken(context: Context): String = prefs(context).getString(KEY_TOKEN, "").orEmpty()
}
