# #0005 Desktop runtime dependency resolution hits unavailable JetBrains Maven host before configured JogAmp repository for JOGL artifacts

- 2026-11-24T19:45:57Z `issue`: Desktop runtime dependency resolution hits unavailable JetBrains Maven host before configured JogAmp repository for JOGL artifacts [KineticsLyrics-KMP/settings.gradle.kts:19]
- 2026-11-24T19:46:23Z `attempt`: Prioritized JogAmp Maven repository ahead of unreachable JetBrains Compose repository for JOGL/GlueGen artifacts; build could not be re-run in this sandbox because Gradle wrapper lock access is denied [KineticsLyrics-KMP/settings.gradle.kts:18] (partial)
- 2026-11-24T19:48:32Z `attempt`: User run resolved JogAmp dependencies and reached desktop application startup after repository order change [KineticsLyrics-KMP/settings.gradle.kts:18] (worked)
- 2026-11-24T19:48:34Z `fix`: JogAmp dependencies resolved and desktop run reached application after placing JogAmp repository before unreachable JetBrains Maven host [KineticsLyrics-KMP/settings.gradle.kts:18]
