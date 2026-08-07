package com.codexbridge.widget

import android.content.Context
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

/**
 * WorkManager planlaması. Periyodik iş min 15 dk (Android alt sınırı); bu yüzden widget
 * canlı değil, "son bilinen + veri yaşı" gösterir. Ayarlar kaydedilince bir kez anında da çeker.
 */
object RefreshScheduler {
    private const val PERIODIC = "codexbridge-refresh-periodic"
    private const val ONE_SHOT = "codexbridge-refresh-now"

    private val constraints = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED)
        .build()

    fun schedulePeriodic(context: Context) {
        val request = PeriodicWorkRequestBuilder<RefreshWorker>(15, TimeUnit.MINUTES)
            .setConstraints(constraints)
            .build()
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(
            PERIODIC,
            ExistingPeriodicWorkPolicy.UPDATE,
            request,
        )
    }

    fun refreshNow(context: Context) {
        val request = OneTimeWorkRequestBuilder<RefreshWorker>()
            .setConstraints(constraints)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            ONE_SHOT,
            ExistingWorkPolicy.REPLACE,
            request,
        )
    }
}
