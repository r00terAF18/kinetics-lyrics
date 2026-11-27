# #0004 Desktop JVM compile fails at KCEF startup: suspend client creation and obsolete KCEF browser API names

- 2026-11-24T19:40:53Z `issue`: Desktop JVM compile fails at KCEF startup: suspend client creation and obsolete KCEF browser API names [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:285]
- 2026-11-24T19:43:47Z `attempt`: Moved suspend KCEF client creation into LaunchedEffect and switched to current createBrowser(url) default API; Gradle compilation blocked by access denied on Gradle 9.6 wrapper lock in C:\Users\Amir\.gradle [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt] (partial)
- 2026-11-24T19:48:39Z `attempt`: User's desktop run compiled Main.kt and launched app after moving suspend KCEF client creation into LaunchedEffect [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:285] (worked)
- 2026-11-24T19:48:42Z `fix`: KCEF setup changes compile and desktop application starts [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:285]
