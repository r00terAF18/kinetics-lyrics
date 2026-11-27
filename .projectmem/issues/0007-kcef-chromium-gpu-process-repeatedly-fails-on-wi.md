# #0007 KCEF Chromium GPU process repeatedly fails on Windows and terminates with GPU process isn't usable

- 2026-11-24T19:51:14Z `issue`: KCEF Chromium GPU process repeatedly fails on Windows and terminates with GPU process isn't usable [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:124]
- 2026-11-24T19:54:33Z `attempt`: Pass --disable-gpu to KCEF only on Windows so Chromium selects software SwiftShader path; source confirms KCEF forwards args; diff check passes, runtime not rerun [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:135] (partial)
