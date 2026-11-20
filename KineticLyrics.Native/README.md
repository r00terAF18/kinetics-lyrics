# Kinetic Lyrics native app

A native lyrics wallpaper project for Windows and Android, based on the extracted Wallpaper Engine project. The original `og_wallpaper_engine` folder remains the visual and behavior reference. The app does not use a browser control.

## Platform status

- **Windows desktop:** reads the active Windows system media session through Global System Media Transport Controls. The Windows host creates a native Win32 surface intended for Explorer's wallpaper `WorkerW` layer so lyrics sit behind desktop icons; a separate compact WinUI window provides configuration.
- **Windows rendering:** currently uses transparent, native GDI drawing. This is the initial native host path and uses the CPU; the GPU renderer is not connected to the desktop surface yet. Explorer's `WorkerW` integration is an undocumented shell technique, so it still needs a desktop smoke check, including shell restarts and supported Windows builds.
- **Android:** a native `WallpaperService` renders on Android's wallpaper surface. Android 8.0+ uses `SurfaceHolder.LockHardwareCanvas`; Android 7.x uses the software canvas fallback. The app is a compact configuration screen that opens Android's live-wallpaper preview and applies the wallpaper from there.
- **Lyrics:** search embedded synchronized lyrics first, then a neighboring `.lrc`, then indexed files from the selected music folder, followed by LRCLIB and NetEase.

## Build and run on Windows

The project targets .NET 10. Build the Windows target with:

```powershell
dotnet build KineticLyrics.Native\KineticLyrics.Native.csproj -f net10.0-windows10.0.19041.0 -c Debug
```

The Windows App SDK is published alongside the app, so it does not depend on a separately installed Windows App Runtime. Publish a self-contained folder with:

```powershell
dotnet publish KineticLyrics.Native\KineticLyrics.Native.csproj -f net10.0-windows10.0.19041.0 -c Debug -r win-x64 --self-contained true
```

Launch `KineticLyrics.Native.exe` from the resulting `bin\Debug\net10.0-windows10.0.19041.0\win-x64\publish` folder. The prior unpackaged build crashed at startup when Windows could not find a registered Windows App Runtime; the self-contained publish setting fixes that dependency. The WorkerW desktop surface itself still needs a visual smoke check on the target Windows desktop.

## Android toolchain

The Android SDK is installed at `%LOCALAPPDATA%\Android\Sdk`, and Microsoft OpenJDK 21 is installed at `%LOCALAPPDATA%\Programs\Microsoft\jdk-21.0.13.1+2`. User-level `ANDROID_HOME`, `ANDROID_SDK_ROOT`, `JAVA_HOME`, and `DOTNET_JAVA_HOME` variables point to those locations. API 36, Build Tools 36.0.0, platform-tools, command-line tools, and the emulator are installed.

Build the Android APK from PowerShell with:

```powershell
. .\.tools\android-env.ps1
dotnet build KineticLyrics.Native\KineticLyrics.Native.csproj -f net10.0-android -c Debug
```

Install `KineticLyrics.Native\bin\Debug\net10.0-android\com.kineticslyrics.app-Signed.apk` on a device. In the app, grant notification access, select the music folder so Android can read embedded and neighboring `.lrc` files, then tap **Preview and set live wallpaper** and choose **Set wallpaper** in Android's preview.

## Attribution

The extracted `og_wallpaper_engine/index.html` and `project.json` are retained unchanged as source references. The native renderer is a new implementation of the visualizer behavior.
