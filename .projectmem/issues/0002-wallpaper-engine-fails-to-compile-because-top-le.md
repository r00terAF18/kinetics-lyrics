# #0002 Wallpaper engine fails to compile because top-level class calls inner WallpaperService.Engine without service receiver

- 2026-11-24T19:14:55Z `issue`: Wallpaper engine fails to compile because top-level class calls inner WallpaperService.Engine without service receiver [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt:28]
- 2026-11-24T19:17:12Z `attempt`: Moved engine subclass inside WallpaperService so inherited Engine constructor has enclosing service receiver; Gradle compile blocked by access denied under C:\Users\Amir\.gradle [KineticsLyrics-KMP/androidApp/src/main/kotlin/com/kineticlyrics/wallpaper/KineticWallpaperService.kt] (partial)
