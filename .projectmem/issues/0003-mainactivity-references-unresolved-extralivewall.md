# #0003 MainActivity references unresolved ExtraLiveWallpaperComponent

- 2026-11-24T19:17:29Z `issue`: MainActivity references unresolved ExtraLiveWallpaperComponent [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70]
- 2026-11-24T19:18:42Z `attempt`: Changed WallpaperManager.ExtraLiveWallpaperComponent to EXTRA_LIVE_WALLPAPER_COMPONENT; confirmed field exists in Android 35 android.jar with javap [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70] (worked)
- 2026-11-24T19:18:42Z `fix`: Use Android's uppercase EXTRA_LIVE_WALLPAPER_COMPONENT field; verified in Android 35 android.jar [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70]
