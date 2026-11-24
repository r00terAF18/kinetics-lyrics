WebView2 desktop-layer test (no lyrics, just an animated page with an fps counter)

Build:  dotnet build -c Release     (needs the WebView2 runtime; it is preinstalled on Windows 11)
Run:    WebViewTest.exe auto      -> parents into the wallpaper WorkerW (patched 24H2 path)
        WebViewTest.exe bottom    -> bottom-most window mode
        WebViewTest.exe float     -> normal window (sanity check, should always show)
Quit via Task Manager (it has no window chrome).

Results are written to  %TEMP%\kinetic-webview-test.txt  every 5 seconds
(mode line, parent HWND, layered flag, Progman children). Send me that file plus
whether you could see the animation on the desktop and for how long.
