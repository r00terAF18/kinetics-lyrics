# Kinetic Lyrics — Android Live Wallpaper

## Project Modules
- `:shared` — Kotlin Multiplatform library targeting Android, with shared media/settings models, local `.lrc` matching, and the WebView JavaScript bridge.
- `:androidApp` — Native Kotlin Android Live Wallpaper using `WallpaperService`, `WebView`, `NotificationListenerService`, and Android `Visualizer` FFT.

## Build & Run Commands
- **Android Live Wallpaper (APK / Device Run):**
  ```bash
  ./gradlew :androidApp:installDebug
  ```
