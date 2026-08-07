package com.codexbridge.widget

import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver

/**
 * AndroidManifest'te tanımlı AppWidget alıcısı. Glance widget'ını sisteme bağlar.
 * Widget ilk kez eklendiğinde periyodik yenilemenin kurulu olduğundan emin olur.
 */
class UsageWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = UsageWidget()

    override fun onEnabled(context: android.content.Context) {
        super.onEnabled(context)
        RefreshScheduler.schedulePeriodic(context)
        RefreshScheduler.refreshNow(context)
    }
}
