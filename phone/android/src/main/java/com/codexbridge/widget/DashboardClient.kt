package com.codexbridge.widget

import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.statement.bodyAsText
import io.ktor.http.HttpHeaders
import kotlinx.serialization.json.Json

/**
 * Bir veya birden fazla dashboard/v1 host'undan snapshot çeker.
 *
 * Çok-makineli birleşik görünüm: birden fazla host verilince snapshot'lar birleştirilir.
 * KRİTİK KURAL — maliyet toplanır, kota yüzdeleri toplanmaz (hesap bazlı; aynı id tekilleştirilir).
 */
class DashboardClient(
    private val http: HttpClient,
    private val json: Json = Json { ignoreUnknownKeys = true },
) {
    data class Host(val baseUrl: String, val token: String?)

    suspend fun fetch(host: Host): DashboardSnapshot {
        val text = http.get("${host.baseUrl.trimEnd('/')}/dashboard/v1/snapshot") {
            host.token?.let { header(HttpHeaders.Authorization, "Bearer $it") }
        }.bodyAsText()
        return json.decodeFromString(DashboardSnapshot.serializer(), text)
    }

    /** Birden fazla host'un satırlarını birleştirir: maliyet toplanır, kota (aynı id) tekil. */
    suspend fun fetchMerged(hosts: List<Host>): List<ProviderRow> {
        val byId = LinkedHashMap<String, ProviderRow>()
        for (host in hosts) {
            val snap = runCatching { fetch(host) }.getOrNull() ?: continue
            for (row in snap.providers) {
                val existing = byId[row.id]
                if (existing == null) {
                    byId[row.id] = row
                } else {
                    // Kota pencerelerini tekil tut (hesap bazlı), yalnızca maliyeti topla.
                    val mergedCost = CostInfo(
                        todayUsd = (existing.cost?.todayUsd ?: 0.0) + (row.cost?.todayUsd ?: 0.0),
                        last30DaysUsd = (existing.cost?.last30DaysUsd ?: 0.0) + (row.cost?.last30DaysUsd ?: 0.0),
                    )
                    byId[row.id] = existing.copy(cost = mergedCost)
                }
            }
        }
        return byId.values.sortedBy { it.display?.sortKey ?: 0 }
    }
}
