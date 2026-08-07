package com.codexbridge.widget

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * dashboard/v1 snapshot şemasının kotlinx.serialization karşılığı.
 * CodexBridge host (Windows) ve codexbar serve (macOS/Linux) aynı şemayı üretir.
 */
@Serializable
data class DashboardSnapshot(
    val schemaVersion: Int = 1,
    val generatedAt: String,
    val staleAfterSeconds: Int = 180,
    val host: HostInfo,
    val providers: List<ProviderRow> = emptyList(),
)

@Serializable
data class HostInfo(
    val codexBarVersion: String? = null,
    val refreshIntervalSeconds: Int = 0,
)

@Serializable
data class ProviderRow(
    val id: String,
    val name: String,
    val enabled: Boolean = true,
    val source: String? = null,
    val status: ProviderStatus? = null,
    val identity: ProviderIdentity? = null,
    val windows: List<RateWindow> = emptyList(),
    val credits: CreditInfo? = null,
    val cost: CostInfo? = null,
    val display: DisplayHints? = null,
    val error: ProviderError? = null,
    val updatedAt: String? = null,
)

@Serializable
data class ProviderStatus(val level: String = "unknown", val label: String? = null, val updatedAt: String? = null)

@Serializable
data class ProviderIdentity(val accountEmail: String? = null, val plan: String? = null)

@Serializable
data class RateWindow(
    val kind: String,
    val label: String? = null,
    val usedPercent: Double? = null,
    val remainingPercent: Double? = null,
    val resetAt: String? = null,
)

@Serializable
data class CreditInfo(val remaining: Double? = null, val unit: String? = null)

@Serializable
data class CostInfo(
    @SerialName("todayUSD") val todayUsd: Double? = null,
    @SerialName("last30DaysUSD") val last30DaysUsd: Double? = null,
)

@Serializable
data class DisplayHints(val accentColor: String? = null, val sortKey: Int = 0, val priority: String? = null)

@Serializable
data class ProviderError(val code: String? = null, val message: String? = null)
