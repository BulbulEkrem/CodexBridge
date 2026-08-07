package com.codexbridge.widget

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Button
import android.widget.EditText
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

/**
 * Yapılandırma + tanılama ekranı.
 *
 * - Host URL + Bearer token'ı HostConfig'e (SharedPreferences) yazar; RefreshWorker/DashboardClient okur.
 * - Kaydedince periyodik yenilemeyi planlar ve bir kez anında çeker.
 * - "Widget güncellenmiyor mu?" tanılaması + ilgili sistem ayarlarına intent'ler
 *   (Data Saver / pil optimizasyonu / arka plan verisi — sessiz eskimenin baş nedenleri).
 */
class MainActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        val hostUrl = findViewById<EditText>(R.id.hostUrl)
        val token = findViewById<EditText>(R.id.token)

        hostUrl.setText(HostConfig.currentUrl(this))
        token.setText(HostConfig.currentToken(this))

        findViewById<Button>(R.id.saveButton).setOnClickListener {
            val url = hostUrl.text.toString().trim()
            if (url.isEmpty()) {
                Toast.makeText(this, R.string.missing_host_toast, Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            HostConfig.save(this, url, token.text.toString())
            RefreshScheduler.schedulePeriodic(this)
            RefreshScheduler.refreshNow(this)
            Toast.makeText(this, R.string.saved_toast, Toast.LENGTH_SHORT).show()
        }

        findViewById<Button>(R.id.batteryButton).setOnClickListener {
            openBatteryOptimizationSettings()
        }
        findViewById<Button>(R.id.dataButton).setOnClickListener {
            openDataUsageSettings()
        }
        findViewById<Button>(R.id.appSettingsButton).setOnClickListener {
            openAppDetailsSettings()
        }
    }

    /** Pil optimizasyonu listesi — periyodik işin öldürülmesini engellemek için muafiyet. */
    private fun openBatteryOptimizationSettings() {
        val intent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
        startSettings(intent, fallbackToAppDetails = true)
    }

    /** Data Saver / veri kullanımı ekranı — arka plan ağ erişimi engelini görmek için. */
    private fun openDataUsageSettings() {
        val intent = Intent(Settings.ACTION_DATA_USAGE_SETTINGS)
        startSettings(intent, fallbackToAppDetails = true)
    }

    /** Uygulama detayları — "arka plan verisi / pilsiz kısıtlama" ayarlarının olduğu yer. */
    private fun openAppDetailsSettings() {
        val intent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
            data = Uri.fromParts("package", packageName, null)
        }
        startSettings(intent, fallbackToAppDetails = false)
    }

    private fun startSettings(intent: Intent, fallbackToAppDetails: Boolean) {
        try {
            startActivity(intent)
        } catch (e: Exception) {
            if (fallbackToAppDetails) {
                runCatching {
                    startActivity(
                        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                            data = Uri.fromParts("package", packageName, null)
                        },
                    )
                }.onFailure { openGeneralSettings() }
            } else {
                openGeneralSettings()
            }
        }
    }

    private fun openGeneralSettings() {
        runCatching { startActivity(Intent(Settings.ACTION_SETTINGS)) }
    }

    @Suppress("unused")
    private fun sdkAtLeast(level: Int) = Build.VERSION.SDK_INT >= level
}
