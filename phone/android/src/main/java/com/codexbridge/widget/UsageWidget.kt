package com.codexbridge.widget

import android.content.Context
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import androidx.datastore.preferences.core.emptyPreferences
import androidx.glance.GlanceModifier
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.provideContent
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.layout.padding
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import kotlinx.coroutines.flow.first
import java.time.Duration
import java.time.Instant

/**
 * Glance widget. Felsefe: "canlı gösterge değil, son bilinen durum + yaşı".
 * Her sağlayıcı satırı yüzdeyi VE verinin ne kadar eski olduğunu gösterir.
 */
class UsageWidget : GlanceAppWidget() {
    override suspend fun provideGlance(context: Context, id: androidx.glance.GlanceId) {
        val prefs = context.dashboardStore.data.first()
        val rows = prefs[KEY_SNAPSHOT]?.let { DashboardCodec.decodeRows(it) } ?: emptyList()
        val fetchedAt = prefs[KEY_FETCHED_AT] ?: 0L
        provideContent { WidgetBody(rows, fetchedAt) }
    }

    @Composable
    private fun WidgetBody(rows: List<ProviderRow>, fetchedAtMs: Long) {
        Column(modifier = GlanceModifier.padding(8.dp)) {
            val age = ageLabel(fetchedAtMs)
            Text("CodexBridge · $age", style = TextStyle())
            for (row in rows) {
                val used = row.windows.firstOrNull()?.usedPercent ?: 0.0
                Row(modifier = GlanceModifier.padding(top = 2.dp)) {
                    Text("${shortName(row.name)}  ${used.toInt()}%", style = TextStyle())
                }
            }
        }
    }

    /** "12 dk önce" gibi veri yaşı. Sessiz eskimeye karşı kullanıcıya net sinyal. */
    private fun ageLabel(fetchedAtMs: Long): String {
        if (fetchedAtMs <= 0L) return "veri yok"
        val mins = Duration.between(Instant.ofEpochMilli(fetchedAtMs), Instant.now()).toMinutes()
        return when {
            mins < 1 -> "az önce"
            mins < 60 -> "$mins dk önce"
            else -> "${mins / 60} sa önce"
        }
    }

    private fun shortName(name: String) = if (name.length <= 6) name else name.substring(0, 6)
}
