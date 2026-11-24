# projectmem - kinetics-lyrics

_Last updated: 2026-11-24_

## Project purpose
Replace this placeholder with a concise description of what this project does, who it serves, and the main technologies or runtime assumptions.

## Recent issues
- [OPEN] #0002 Wallpaper engine fails to compile because top-level class calls inner WallpaperService.Engine without service receiver [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt:28] (open)
  - Partial attempt: Moved engine subclass inside WallpaperService so inherited Engine constructor has enclosing service receiver; Gradle compile blocked by access denied under C:\Users\Amir\.gradle [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt]
- [OPEN] #0001 GPU lyrics stay blank when missing camOn crashes buildGpuLine; ticker Text exceeds WebGPU texture limit [KineticWallpaper/WebContent/index.html:838] (open)
  - Partial attempt: Replaced undefined camera helper with camera mode check; bounded repeated ticker texture and kept lyrics lookup status visible. Release build and inline JS syntax pass; runtime display not yet confirmed. [KineticWallpaper/WebContent/index.html:838]
  - Partial attempt: Bound Pixi ticker texture to 4000 logical pixels, reset base font on each track and viewport layout, and removed ignored powerPreference. Release build and inline JavaScript syntax pass; live WebView2 rendering remains unverified. [KineticWallpaper/WebContent/index.html:127]
- [DONE] #0003 MainActivity references unresolved ExtraLiveWallpaperComponent [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70] -> Use Android's uppercase EXTRA_LIVE_WALLPAPER_COMPONENT field; verified in Android 35 android.jar [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/MainActivity.kt:70] (fixed)

## Decisions
- No decisions logged yet.

## Notes
- No notes logged yet.

## Key files
- `WallpaperService.Engine`
- `WallpaperManager.ExtraLiveWallpaperComponent`
- `android.jar`

## Open questions
- None logged yet.
