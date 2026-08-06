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
 * Kullanıcının eklediği host(lar). Gerçek uygulamada güvenli depoda (EncryptedSharedPreferences)
 * tutulmalı; token hassas. QR ile eşleştirme akışı bu listeyi doldurur.
 */
object HostConfig {
    fun load(context: Context): List<DashboardClient.Host> {
        // TODO: EncryptedSharedPreferences'tan oku. İskele: boş → widget "veri yok" gösterir.
        return emptyList()
    }
}
