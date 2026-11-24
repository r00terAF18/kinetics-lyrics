# Kinetic Lyrics — Kotlin Multiplatform (Android, Windows, macOS)

## Project Modules
- `:shared` — Kotlin Multiplatform library targeting **Android**, **JVM Desktop (Windows & macOS)**, and **macOS Native (`macosArm64` / `macosX64`)**. Contains shared media/settings models, local `.lrc` matching, WebView JS bridge protocol, Windows SMTC/JNA desktop embedding, macOS AppleScript/CoreAudio media polling, and JTransforms FFT audio analysis.
- `:androidApp` — Native Kotlin **Android Live Wallpaper** (`WallpaperService` + `VirtualDisplay` + `Presentation` + `WebView` + `NotificationListenerService` + Android `Visualizer` FFT).
- `:desktopApp` — **Compose Multiplatform Desktop** app for **Windows** and **macOS** with system tray / menu bar controls, desktop wallpaper embedding, and an embedded local HTTP server hosting the PixiJS v8 + GSAP GPU stage.

## Build & Run Commands
- **Android Live Wallpaper (APK / Device Run):**
  ```bash
  ./gradlew :androidApp:installDebug
  ```
- **Windows / macOS Desktop App:**
  ```bash
  ./gradlew :desktopApp:run
  ```
- **Package Native Installers (`.msi`/`.exe` on Windows, `.dmg` on macOS):**
  ```bash
  ./gradlew :desktopApp:packageDistributionForCurrentOS
  ```
