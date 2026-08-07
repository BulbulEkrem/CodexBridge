# kotlinx.serialization — @Serializable sınıflarının serializer'larını koru.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**
-keepclassmembers class kotlinx.serialization.json.** { *; }
-keep,includedescriptorclasses class com.codexbridge.widget.**$$serializer { *; }
-keepclassmembers class com.codexbridge.widget.** {
    *** Companion;
}
-keepclasseswithmembers class com.codexbridge.widget.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# Ktor
-keep class io.ktor.** { *; }
-dontwarn io.ktor.**
-dontwarn org.slf4j.**
