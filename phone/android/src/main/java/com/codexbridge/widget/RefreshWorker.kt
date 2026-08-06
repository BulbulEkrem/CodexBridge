package com.codexbridge.widget

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import androidx.glance.appwidget.updateAll
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import io.ktor.client.HttpClient

/** Widget verisinin ve son güncelleme zamanının saklandığı yer. */
val Context.dashboardStore by preferencesDataStore(name = "codexbridge")
val KEY_SNAPSHOT = stringPreferencesKey("snapshot_json")
val KEY_FETCHED_AT = longPreferencesKey("fetched_at_epoch_ms")

/**
 * WorkManager worker — periyodik (min 15 dk) snapshot çeker, DataStore'a yazar, widget'ı yeniler.
 * Ağ başarısız olursa SON BİLİNEN durum korunur; widget "veri yaşı"nı gösterdiği için kullanıcı
 * verinin eskidiğini görür (Data Saver / pil optimizasyonu sessiz eskimesine karşı savunma).
 */
class RefreshWorker(
    context: Context,
    params: WorkerParameters,
    private val http: HttpClient = HttpClient(),
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val hosts = HostConfig.load(applicationContext) // kullanıcı ayarlarından
        if (hosts.isEmpty()) return Result.success()

        val client = DashboardClient(http)
        val merged = runCatching { client.fetchMerged(hosts) }.getOrElse {
            // Başarısızlıkta eski veriyi koru; sadece yeniden dene.
            return Result.retry()
        }

        applicationContext.dashboardStore.edit { prefs ->
            prefs[KEY_SNAPSHOT] = DashboardCodec.encodeRows(merged)
            prefs[KEY_FETCHED_AT] = System.currentTimeMillis()
        }
        UsageWidget().updateAll(applicationContext)
        return Result.success()
    }
}
