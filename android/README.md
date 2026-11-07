# Nevatch for Android

The friend's app for phones. It does the same job as the Windows Nevatch, with less on screen: add your key
(paste the `vless://` link, tap it in a chat, or scan its QR code), then press **Connect**. The shield turns
green once the server answers. Everything on the phone then goes through Nevatch, except Russian sites,
which go direct, the same as on Windows.

It works like the Windows app underneath. Xray-core (from [AndroidLibXrayLite](https://github.com/2dust/AndroidLibXrayLite))
reads the phone's traffic from Android's VPN (`VpnService`) through Xray's own `tun` inbound, with the same
VLESS + Reality settings, DNS handling and routing as `client/Nevatch.cs`. While connected it pings
`http://update.nevatch/ping?id=…` once a second through the VPN, so the server's console shows the phone as
online and the app knows the connection really works.

Plain Java and Android framework views, with no Kotlin and no AndroidX UI. That keeps the build small and simple.

## Build

You need:

- a JDK 17 or newer (built with JDK 21, `C:\Program Files\Java\jdk-21`)
- the Android SDK with platform `android-37` and build-tools (`%LOCALAPPDATA%\Android\Sdk`)
- no Gradle install: the wrapper (`gradlew`) downloads Gradle 9.8.2 the first time

```bat
cd android
set JAVA_HOME=C:\Program Files\Java\jdk-21
gradlew assembleDebug
```

The APK is copied to `android\dist\Nevatch-<version>.apk`. It is signed with the SDK's debug key, so it installs
on any phone as a normal APK: allow "install unknown apps" for whatever opens it (Files, Telegram, Chrome).
`local.properties` (not in git) holds the SDK path; Android Studio writes it, or create it yourself:

```
sdk.dir=C:/Users/<you>/AppData/Local/Android/Sdk
```

The version is `nevatchVersion` at the top of `app/build.gradle.kts`. Raise `nevatchVersionCode` with it,
or phones refuse to install the new APK over the old one.

**Debug key and updates:** Android only installs an update over an existing app when both are signed with the same key.
The debug key lives in `%USERPROFILE%\.android\debug.keystore` on the PC that builds. Build every version on
that PC (or copy that file), or friends must uninstall the app before installing the new APK.

### Updating Xray

`app/libs/libv2ray.aar` is the `libv2ray.aar` asset from an AndroidLibXrayLite release (v26.10.31, Xray 26.10.31).
To update, download a newer one over it and rebuild.

## Test in the emulator

1. Start a throwaway server on test ports (it doesn't touch the real one's data):
   ```bat
   set CONSOLE_PORT=18088& set CONSOLE_HOST=127.0.0.1& set FVPN_DATA=%TEMP%\nevatch-test& set VPN_PORT=24443& set FVPN_API_PORT=20085& set FVPN_UPDATE_PORT=20086& set FVPN_RELAY_PORT=20443& set PUBLIC_HOST=10.0.2.2& set FVPN_CONSOLE_OPEN=1
   node server\server.js
   ```
   `10.0.2.2` is how the emulator reaches the PC.
2. Add a person at http://localhost:18088 and copy their key.
3. Install and open the key in the app:
   ```bat
   adb install -r android\dist\Nevatch-1.5.3.apk
   adb shell "am start -a android.intent.action.VIEW -d 'vless://…paste the key…' com.nevatch.app"
   ```
4. Tap **Connect** and accept Android's VPN prompt. The server's console shows the person as connected.

## Files

- `app/src/main/java/com/nevatch/app/NevatchKey.java` parses the key and builds the Xray config (mirrors `VlessKey` and `XrayConfig` in `client/Nevatch.cs`).
- `NevatchVpnService.java`: the VPN, Xray, the presence pings and the notification with its Disconnect button.
- `MainActivity.java`: the one screen.
- `Keys.java`: stores the one key in the app's private storage.

## Licenses

The app's own code is under the repository's MIT license. The APK bundles:

- AndroidLibXrayLite / Xray-core (`libv2ray.aar`): LGPL-3.0 (Xray-core itself is MPL-2.0). It is an unmodified
  library that is loaded at runtime, so anyone can swap in their own build. Link to its source when you share the APK.
- zxing-android-embedded and ZXing: Apache-2.0.
- AndroidX Core: Apache-2.0.
