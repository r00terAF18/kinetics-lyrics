using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KineticWallpaper;

public enum DesktopAttachMode
{
    AutoShell,
    BottomMostWindow,
    FloatingWindow
}

public static class DesktopWallpaperHost
{
    private const uint WmSpawnWorker = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsChild = 0x40000000;
    private const nint WsPopup = unchecked((int)0x80000000);
    private const nint WsVisible = 0x10000000;
    private const nint WsClipSiblings = 0x04000000;

    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;
    private const nint WsExLayered = 0x00080000;
    private const nint WsExNoRedirectionBitmap = 0x00200000;

    private static readonly nint HwndBottom = 1;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;

    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifSendChange = 0x0002;

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

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
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private static DispatcherTimer? _watchdog;
    public static DesktopAttachMode CurrentMode { get; private set; } = DesktopAttachMode.AutoShell;
    public static string ModeDescription { get; private set; } = "Initializing";

    public static void Attach(Window window, DesktopAttachMode mode = DesktopAttachMode.AutoShell)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        CurrentMode = mode;
        ApplyMode(window, hwnd);

        _watchdog?.Stop();
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watchdog.Tick += (_, _) => MaintainZOrder(window, hwnd);
        _watchdog.Start();

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        window.Closed += (_, _) =>
        {
            _watchdog?.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SetParent(hwnd, 0);
            SystemParametersInfo(SpiSetDeskWallpaper, 0, null, SpifSendChange);
        };

        void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
            window.Dispatcher.InvokeAsync(() => ApplyMode(window, hwnd));

        void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
                window.Dispatcher.InvokeAsync(() => ApplyMode(window, hwnd));
        }
    }

    public static void SetMode(Window window, DesktopAttachMode mode)
    {
        CurrentMode = mode;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ApplyMode(window, hwnd);
    }

    private static void MaintainZOrder(Window window, nint hwnd)
    {
        if (CurrentMode == DesktopAttachMode.FloatingWindow) return;

        if (CurrentMode == DesktopAttachMode.BottomMostWindow)
        {
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            return;
        }

        var progman = FindWindow("Progman", null);
        if (progman == 0) return;

        if (GetParent(hwnd) == 0)
        {
            ApplyMode(window, hwnd);
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

    private static void ApplyMode(Window window, nint hwnd)
    {
        // Always keep WPF hardware-accelerated DirectX pipeline enabled (RenderMode.Default).
        // Never set WS_EX_LAYERED on a child inside WS_EX_NOREDIRECTIONBITMAP Progman, because
        // DWM does not composite GDI/layered surfaces inside a non-redirected parent.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget target)
            target.RenderMode = RenderMode.Default;

        if (CurrentMode == DesktopAttachMode.FloatingWindow)
        {
            SetParent(hwnd, 0);
            var style = GetWindowLongPtr(hwnd, GwlStyle);
            SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsChild) | WsPopup | WsVisible);

            var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
            SetWindowLongPtr(hwnd, GwlExStyle, exStyle & ~(WsExNoActivate | WsExLayered));

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
            return;
        }

        if (CurrentMode == DesktopAttachMode.BottomMostWindow)
        {
            PinToBottom(window, hwnd);
            return;
        }

        try
        {
            var progman = FindWindow("Progman", null);
            if (progman == 0)
            {
                PinToBottom(window, hwnd);
                return;
            }

            // Spawn WorkerW behind SHELLDLL_DefView
            SendMessageTimeout(progman, WmSpawnWorker, 0xD, 0x1, 0, 1000, out _);
            SendMessageTimeout(progman, WmSpawnWorker, 0, 0, 0, 1000, out _);

            int physW = Math.Max(640, GetSystemMetrics(SmCxVirtualScreen));
            int physH = Math.Max(480, GetSystemMetrics(SmCyVirtualScreen));
            if (GetClientRect(progman, out var rc) && rc.Right - rc.Left > 100 && rc.Bottom - rc.Top > 100)
            {
                physW = rc.Right - rc.Left;
                physH = rc.Bottom - rc.Top;
            }

            SyncWpfLogicalSize(window, physW, physH);

            var style = GetWindowLongPtr(hwnd, GwlStyle);
            SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsPopup) | WsChild | WsClipSiblings | WsVisible);

            var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
            // Strip WS_EX_LAYERED so DWM composites WPF's DirectX SwapChain directly inside Progman!
            SetWindowLongPtr(hwnd, GwlExStyle, (exStyle & ~(WsExAppWindow | WsExLayered)) | WsExNoActivate | WsExToolWindow);

            // Windows 11 24H2+: SHELLDLL_DefView is a direct child of Progman
            var shellDefView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
            if (shellDefView != 0)
            {
                SetParent(hwnd, progman);
                // Insert immediately AFTER SHELLDLL_DefView (behind desktop icons) and BEFORE WorkerW (static wallpaper)
                SetWindowPos(hwnd, shellDefView, 0, 0, physW, physH, SwpNoActivate | SwpShowWindow | SwpFrameChanged);

                var workerW = FindWindowEx(progman, 0, "WorkerW", null);
                if (workerW != 0 && workerW != hwnd)
                    SetWindowPos(workerW, hwnd, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

                bool is24H2 = (GetWindowLongPtr(progman, GwlExStyle) & WsExNoRedirectionBitmap) != 0;
                ModeDescription = $"{(is24H2 ? "Win11 24H2+ Progman" : "Win11 Progman")} ({physW}x{physH})";
                return;
            }

            // Windows 10 / Windows 11 23H2 and earlier: WorkerW is a top-level sibling after SHELLDLL_DefView's WorkerW
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
                PinToBottom(window, hwnd);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ApplyMode", ex);
            PinToBottom(window, hwnd);
        }
    }

    private static void PinToBottom(Window window, nint hwnd)
    {
        int physX = GetSystemMetrics(SmXVirtualScreen);
        int physY = GetSystemMetrics(SmYVirtualScreen);
        int physW = Math.Max(640, GetSystemMetrics(SmCxVirtualScreen));
        int physH = Math.Max(480, GetSystemMetrics(SmCyVirtualScreen));

        SyncWpfLogicalSize(window, physW, physH);

        SetParent(hwnd, 0);
        var style = GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsChild) | WsPopup | WsVisible);

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, (exStyle & ~(WsExAppWindow | WsExLayered)) | WsExNoActivate | WsExToolWindow);

        SetWindowPos(hwnd, HwndBottom, physX, physY, physW, physH, SwpNoActivate | SwpShowWindow | SwpFrameChanged);
        ModeDescription = $"Bottom-Most Desktop Layer ({physW}x{physH})";
    }

    private static void SyncWpfLogicalSize(Window window, int physW, int physH)
    {
        var source = PresentationSource.FromVisual(window);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        window.Width = physW / dpiX;
        window.Height = physH / dpiY;
    }

    public static string GetDiagnostics(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var progman = FindWindow("Progman", null);
        var defView = progman != 0 ? FindWindowEx(progman, 0, "SHELLDLL_DefView", null) : 0;
        var workerW = progman != 0 ? FindWindowEx(progman, 0, "WorkerW", null) : 0;
        GetWindowRect(hwnd, out var rc);

        var sb = new StringBuilder();
        sb.AppendLine($"Mode: {ModeDescription}");
        sb.AppendLine($"Our HWND: 0x{hwnd:X} | Parent: 0x{GetParent(hwnd):X}");
        sb.AppendLine($"Our Rect: ({rc.Left}, {rc.Top}) - ({rc.Right}, {rc.Bottom}) [{rc.Right - rc.Left}x{rc.Bottom - rc.Top}]");
        sb.AppendLine($"Progman: 0x{progman:X} | ExStyle: 0x{GetWindowLongPtr(progman, GwlExStyle):X}");
        sb.AppendLine($"Progman->SHELLDLL_DefView: 0x{defView:X} | Progman->WorkerW: 0x{workerW:X}");
        if (progman != 0)
        {
            sb.AppendLine("Progman Children (top-to-bottom Z-order):");
            EnumChildWindows(progman, (child, _) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(child, cls, cls.Capacity);
                GetWindowRect(child, out var crc);
                sb.AppendLine($"  0x{child:X} [{cls}] ({crc.Right - crc.Left}x{crc.Bottom - crc.Top})");
                return true;
            }, 0);
        }
        return sb.ToString();
    }
}
