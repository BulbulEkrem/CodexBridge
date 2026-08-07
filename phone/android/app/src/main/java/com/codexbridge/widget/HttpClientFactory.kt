package com.codexbridge.widget

import io.ktor.client.HttpClient
import io.ktor.client.engine.android.Android
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json

/**
 * Ortak Ktor istemcisi. ContentNegotiation(json) kurulu → PushRegistration'ın gönderdiği
 * @Serializable gövde otomatik JSON'a çevrilir. DashboardClient gövdeyi elle çözdüğü için
 * ondan etkilenmez.
 */
object HttpClientFactory {
    val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun create(): HttpClient = HttpClient(Android) {
        expectSuccess = true
        install(ContentNegotiation) {
            json(json)
        }
    }
}
