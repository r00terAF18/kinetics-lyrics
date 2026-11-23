using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KineticWallpaper;

public static class DesktopWallpaperHost
{
    private const uint WmSpawnWorker = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsChild = 0x40000000;
    private const nint WsPopup = unchecked((int)0x80000000);
    private const nint WsClipSiblings = 0x04000000;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private static bool _isAttached;
    private static DispatcherTimer? _watchdog;

    public static string ModeDescription { get; private set; } = "Initializing";

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _isAttached = true;
        EmbedIntoDesktop(window, hwnd);

        _watchdog?.Stop();
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _watchdog.Tick += (_, _) =>
        {
            if (_isAttached) EnsureEmbedded(window, hwnd);
        };
        _watchdog.Start();

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        window.Closed += (_, _) =>
        {
            _isAttached = false;
            _watchdog?.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SetParent(hwnd, 0);
            SystemParametersInfo(SpiSetDeskWallpaper, 0, null, SpifSendChange);
        };

        void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isAttached)
                window.Dispatcher.InvokeAsync(() => EmbedIntoDesktop(window, hwnd));
        }

        void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (_isAttached && e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
                window.Dispatcher.InvokeAsync(() => EmbedIntoDesktop(window, hwnd));
        }
    }

    public static void DetachToWindow(Window window)
    {
        _isAttached = false;
        _watchdog?.Stop();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        SetParent(hwnd, 0);
        var style = GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsChild) | WsPopup);

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, exStyle & ~WsExNoActivate);

        window.WindowState = WindowState.Normal;
        window.Left = 120;
        window.Top = 120;
        window.Width = 1280;
        window.Height = 720;
        window.Topmost = false;
        window.Show();
        window.Activate();

        ModeDescription = "Floating Window";
        SystemParametersInfo(SpiSetDeskWallpaper, 0, null, SpifSendChange);
    }

    public static void Reattach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _isAttached = true;
        EmbedIntoDesktop(window, hwnd);
    }

    private static void EnsureEmbedded(Window window, nint hwnd)
    {
        var progman = FindWindow("Progman", null);
        if (progman == 0) return;

        if (GetParent(hwnd) == 0)
        {
            EmbedIntoDesktop(window, hwnd);
            return;
        }

        var shellDefView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (shellDefView != 0 && GetParent(hwnd) == progman)
        {
            SetWindowPos(hwnd, shellDefView, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            var workerW = FindWindowEx(progman, 0, "WorkerW", null);
            if (workerW != 0 && workerW != hwnd)
                SetWindowPos(workerW, hwnd, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private static void EmbedIntoDesktop(Window window, nint hwnd)
    {
        var progman = FindWindow("Progman", null);
        if (progman == 0)
        {
            ModeDescription = "Progman not found";
            return;
        }

        // Ask Progman to spawn the WorkerW wallpaper host layer
        SendMessageTimeout(progman, WmSpawnWorker, 0xD, 0x1, 0, 1000, out _);
        SendMessageTimeout(progman, WmSpawnWorker, 0, 0, 0, 1000, out _);

        int physW = Math.Max(640, GetSystemMetrics(SmCxVirtualScreen));
        int physH = Math.Max(480, GetSystemMetrics(SmCyVirtualScreen));

        // Ensure WPF's own layout matches the virtual screen size
        var source = PresentationSource.FromVisual(window);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        window.Width = physW / dpiX;
        window.Height = physH / dpiY;

        var style = GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsPopup) | WsChild | WsClipSiblings);

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, (exStyle & ~WsExAppWindow) | WsExNoActivate | WsExToolWindow);

        // Path A: Windows 11 24H2+ (SHELLDLL_DefView is a direct child of Progman)
        var shellDefView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (shellDefView != 0)
        {
            // Try parenting into the child WorkerW inside Progman first; if none exists, parent directly to Progman behind SHELLDLL_DefView
            var childWorkerW = FindWindowEx(progman, 0, "WorkerW", null);
            if (childWorkerW != 0 && childWorkerW != hwnd)
            {
                SetParent(hwnd, childWorkerW);
                SetWindowPos(hwnd, 0, 0, 0, physW, physH, SwpNoZOrder | SwpNoActivate | SwpShowWindow | SwpFrameChanged);
                SetWindowPos(childWorkerW, shellDefView, 0, 0, physW, physH, SwpNoActivate | SwpShowWindow);
                ModeDescription = $"Win11 Child WorkerW ({physW}x{physH})";
                return;
            }

            SetParent(hwnd, progman);
            SetWindowPos(hwnd, shellDefView, 0, 0, physW, physH, SwpNoActivate | SwpShowWindow | SwpFrameChanged);
            ModeDescription = $"Win11 Progman Layer ({physW}x{physH})";
            return;
        }

        // Path B: Windows 10 / Windows 11 23H2 and earlier (WorkerW is a top-level sibling after SHELLDLL_DefView's WorkerW)
        nint legacyWorkerW = 0;
        EnumWindows((topHandle, _) =>
        {
            if (FindWindowEx(topHandle, 0, "SHELLDLL_DefView", null) == 0)
                return true;

            legacyWorkerW = FindWindowEx(0, topHandle, "WorkerW", null);
            return legacyWorkerW == 0;
        }, 0);

        if (legacyWorkerW != 0)
        {
            SetParent(hwnd, legacyWorkerW);
            SetWindowPos(hwnd, 0, 0, 0, physW, physH, SwpNoZOrder | SwpNoActivate | SwpShowWindow | SwpFrameChanged);
            ModeDescription = $"Win10 WorkerW ({physW}x{physH})";
        }
        else
        {
            ModeDescription = "Fallback Overlay";
        }
    }
}
