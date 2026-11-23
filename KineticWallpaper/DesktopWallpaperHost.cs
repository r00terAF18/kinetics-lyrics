using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace KineticWallpaper;

public static class DesktopWallpaperHost
{
    private const uint WmSpawnWorker = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const nint WsChild = 0x40000000;
    private const nint WsPopup = unchecked((int)0x80000000);
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifSendChange = 0x0002;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parentHandle, nint childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(nint hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var workerW = GetOrCreateWorkerW();
        if (workerW == 0) return;

        var style = GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsPopup) | WsChild);

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);

        SetParent(hwnd, workerW);
        FitToVirtualScreen(hwnd);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        window.Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SetParent(hwnd, 0);
            SystemParametersInfo(SpiSetDeskWallpaper, 0, null, SpifSendChange);
        };

        void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
            window.Dispatcher.InvokeAsync(() => FitToVirtualScreen(hwnd));
    }

    private static void FitToVirtualScreen(nint hwnd)
    {
        int width = (int)SystemParameters.VirtualScreenWidth;
        int height = (int)SystemParameters.VirtualScreenHeight;
        MoveWindow(hwnd, 0, 0, width, height, true);
    }

    private static nint GetOrCreateWorkerW()
    {
        var progman = FindWindow("Progman", null);
        if (progman == 0) return 0;

        SendMessageTimeout(progman, WmSpawnWorker, 0xD, 0x1, 0, 1000, out _);

        nint workerW = 0;
        bool MatchWorker(nint topHandle, nint _)
        {
            if (FindWindowEx(topHandle, 0, "SHELLDLL_DefView", null) == 0)
                return true;

            workerW = FindWindowEx(0, topHandle, "WorkerW", null);
            if (workerW == 0)
                workerW = FindWindowEx(progman, topHandle, "WorkerW", null);

            return workerW == 0;
        }

        EnumWindows(MatchWorker, 0);
        if (workerW == 0)
            EnumChildWindows(progman, MatchWorker, 0);

        return workerW;
    }
}
