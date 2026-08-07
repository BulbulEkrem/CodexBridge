plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.serialization)
}

// Push'u yalnızca google-services.json bırakılınca aktifleştir.
// Dosya yoksa google-services plugin'i uygulanmaz → build kırılmaz (widget + WorkManager derlenir).
val googleServicesJson = file("google-services.json")
if (googleServicesJson.exists()) {
    apply(plugin = "com.google.gms.google-services")
}

android {
    namespace = "com.codexbridge.widget"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.codexbridge.widget"
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    composeOptions {
        kotlinCompilerExtensionVersion = libs.versions.composeCompiler.get()
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
            excludes += "/META-INF/INDEX.LIST"
            excludes += "/META-INF/io.netty.versions.properties"
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)

    // Glance AppWidget (Compose tabanlı) + veri yaşı gösterimi.
    implementation(libs.androidx.glance.appwidget)

    // Periyodik yenileme (min 15 dk).
    implementation(libs.androidx.work.runtime.ktx)

    // Son bilinen snapshot + fetched-at deposu.
    implementation(libs.androidx.datastore.preferences)

    // dashboard/v1 çekme + kodlama.
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.ktor.client.android)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.serialization.kotlinx.json)

    // Push (opsiyonel): bağımlılık her zaman derlenir; FirebaseApp yalnızca
    // google-services.json ile başlatılır, aksi halde sessizce devre dışı kalır.
    implementation(platform(libs.firebase.bom))
    implementation(libs.firebase.messaging)
}
