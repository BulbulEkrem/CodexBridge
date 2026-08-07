// Kök build betiği — alt modüller kendi plugin'lerini uygular.
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    // Push için (opsiyonel): classpath'te durur ama :app yalnızca google-services.json
    // varsa uygular; yoksa build kırılmaz.
    alias(libs.plugins.google.services) apply false
}
