# #0006 Desktop startup crashes because AWT tray Menu does not support Compose RadioButtonItem

- 2026-11-24T19:47:27Z `issue`: Desktop startup crashes because AWT tray Menu does not support Compose RadioButtonItem [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:172]
- 2026-11-24T19:48:56Z `attempt`: Replaced tray RadioButtonItem calls with supported Item entries that prefix current selection with a checkmark; diff check passes, runtime not rerun [KineticsLyrics-KMP/desktopApp/src/jvmMain/kotlin/com/kineticlyrics/desktop/Main.kt:171] (partial)
