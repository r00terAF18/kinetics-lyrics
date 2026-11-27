# projectmem - kinetics-lyrics

_Last updated: 2026-11-26_

## Project purpose
Replace this placeholder with a concise description of what this project does, who it serves, and the main technologies or runtime assumptions.

## Recent issues
- [OPEN] #0007 KCEF Chromium GPU process repeatedly fails on Windows and terminates with GPU process isn't usable [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:124] (open)
  - Partial attempt: Pass --disable-gpu to KCEF only on Windows so Chromium selects software SwiftShader path; source confirms KCEF forwards args; diff check passes, runtime not rerun [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:135]
- [OPEN] #0006 Desktop startup crashes because AWT tray Menu does not support Compose RadioButtonItem [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:172] (open)
  - Partial attempt: Replaced tray RadioButtonItem calls with supported Item entries that prefix current selection with a checkmark; diff check passes, runtime not rerun [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:171]
- [OPEN] #0002 Wallpaper engine fails to compile because top-level class calls inner WallpaperService.Engine without service receiver [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt:28] (open)
  - Partial attempt: Moved engine subclass inside WallpaperService so inherited Engine constructor has enclosing service receiver; Gradle compile blocked by access denied under C:\Users\Amir\.gradle [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt]
- [OPEN] #0001 GPU lyrics stay blank when missing camOn crashes buildGpuLine; ticker Text exceeds WebGPU texture limit [KineticWallpaper/WebContent/index.html:838] (open)
  - Partial attempt: Replaced undefined camera helper with camera mode check; bounded repeated ticker texture and kept lyrics lookup status visible. Release build and inline JS syntax pass; runtime display not yet confirmed. [KineticWallpaper/WebContent/index.html:838]
  - Partial attempt: Bound Pixi ticker texture to 4000 logical pixels, reset base font on each track and viewport layout, and removed ignored powerPreference. Release build and inline JavaScript syntax pass; live WebView2 rendering remains unverified. [KineticWallpaper/WebContent/index.html:127]
- [DONE] #0005 Desktop runtime dependency resolution hits unavailable JetBrains Maven host before configured JogAmp repository for JOGL artifacts [KineticsLyrics-KMP/settings.gradle.kts:19] -> JogAmp dependencies resolved and desktop run reached application after placing JogAmp repository before unreachable JetBrains Maven host [KineticsLyrics-KMP/settings.gradle.kts:18] (fixed)
  - Partial attempt: Prioritized JogAmp Maven repository ahead of unreachable JetBrains Compose repository for JOGL/GlueGen artifacts; build could not be re-run in this sandbox because Gradle wrapper lock access is denied [KineticsLyrics-KMP/settings.gradle.kts:18]
- [DONE] #0004 Desktop JVM compile fails at KCEF startup: suspend client creation and obsolete KCEF browser API names [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:285] -> KCEF setup changes compile and desktop application starts [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:285] (fixed)
  - Partial attempt: Moved suspend KCEF client creation into LaunchedEffect and switched to current createBrowser(url) default API; Gradle compilation blocked by access denied on Gradle 9.6 wrapper lock in C:\Users\Amir\.gradle [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt]
- [DONE] #0003 MainActivity references unresolved ExtraLiveWallpaperComponent [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70] -> Use Android's uppercase EXTRA_LIVE_WALLPAPER_COMPONENT field; verified in Android 35 android.jar [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70] (fixed)

## Decisions
- No decisions logged yet.

## Notes
- Merge: removed desktop from kotlin project
- Merge: Merge branch 'main' of https://github.com/r00terAF18/kinetics-lyrics

## Key files
- `WallpaperService.Engine`
- `WallpaperManager.ExtraLiveWallpaperComponent`
- `android.jar`
- `9.6`
- `Main.kt`
- `//github.com`

## Open questions
- None logged yet.
