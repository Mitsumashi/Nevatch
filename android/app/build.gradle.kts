plugins {
    id("com.android.application")
}

// One version for the app; the APK is named after it (dist/Nevatch-<version>.apk).
val nevatchVersion = "1.5.3"
val nevatchVersionCode = 3

android {
    namespace = "com.nevatch.app"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.nevatch.app"
        minSdk = 24
        targetSdk = 37
        versionCode = nevatchVersionCode
        versionName = nevatchVersion
        // Phones (64- and 32-bit ARM) and the x86_64 emulator. Leaving out 32-bit x86 saves ~15 MB.
        ndk { abiFilters += listOf("arm64-v8a", "armeabi-v7a", "x86_64") }
    }

    buildTypes {
        debug {
            // Signed with the SDK's debug key, so it installs anywhere as a normal APK.
        }
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        // Xray's native library must be extracted to disk to be loaded on older Android versions.
        jniLibs { useLegacyPackaging = true }
    }
}

dependencies {
    // Xray-core for Android (gomobile), from https://github.com/2dust/AndroidLibXrayLite/releases
    implementation(files("libs/libv2ray.aar"))
    // QR code scanner (Apache-2.0)
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("androidx.core:core:1.13.1") // the scanner needs it (it only declares it compileOnly)
}

// assembleDebug also copies the APK to android/dist/Nevatch-<version>.apk
val copyApk = tasks.register<Copy>("copyApk") {
    from(layout.buildDirectory.file("outputs/apk/debug/app-debug.apk"))
    into(rootProject.layout.projectDirectory.dir("dist"))
    rename { "Nevatch-$nevatchVersion.apk" }
}
tasks.matching { it.name == "assembleDebug" }.configureEach { finalizedBy(copyApk) }
