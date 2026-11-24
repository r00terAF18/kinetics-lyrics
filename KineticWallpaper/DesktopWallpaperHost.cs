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

    private static string _lastOrderCheck = "Not sampled";
    private static int _orderRepairs;
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
            // Do not reset the user's static wallpaper when the live window closes.
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

        if (GetParent(hwnd) != progman) return;
        var icons = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        var wallpaper = FindWindowEx(progman, 0, "WorkerW", null);
        if (icons == 0 || !UsableWorker(wallpaper, progman)) return;

        // EnumChildWindows includes descendants; compare only the three direct siblings.
        var order = new List<nint>(3);
        EnumChildWindows(progman, (h, _) =>
        {
            if (h == icons || h == hwnd || h == wallpaper) order.Add(h);
            return order.Count < 3;
        }, 0);
        if (order.Count != 3 || (order[0] == icons && order[1] == hwnd)) return;
        var before = string.Join(" > ", order.Select(h => h == icons ? "icons" : h == hwnd ? "app" : "static"));
        uint flags = SwpNoMove | SwpNoSize | SwpNoActivate;
        // If Explorer raised its static plane above even the icons, moving our window alone
        // cannot put it both below the icons and above that plane.
        bool ok = order.IndexOf(wallpaper) > order.IndexOf(icons) || SetWindowPos(wallpaper, HwndBottom, 0, 0, 0, 0, flags);
        ok = ok && SetWindowPos(hwnd, icons, 0, 0, 0, 0, flags);
        _lastOrderCheck = $"{before} at {DateTime.Now:T}: {(ok ? "repositioned" : $"failed ({Marshal.GetLastWin32Error()})")}";
        if (ok) _orderRepairs++;
    }

    private static void ApplyMode(Window window, nint hwnd)
    {
        // AllowsTransparency asks WPF itself to create and present a layered HWND.
        // Do not mix its per-pixel updates with SetLayeredWindowAttributes.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget target)
            target.RenderMode = RenderMode.Default;

        if (CurrentMode == DesktopAttachMode.FloatingWindow)
        {
            SetParent(hwnd, 0);
            var style = GetWindowLongPtr(hwnd, GwlStyle);
            SetWindowLongPtr(hwnd, GwlStyle, (style & ~WsChild) | WsPopup | WsVisible);

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

            // On a raised desktop these planes usually already exist. Requesting them
            // again on every mode switch can disturb the very order we are establishing.
            if (FindWindowEx(progman, 0, "WorkerW", null) == 0 ||
                FindWindowEx(progman, 0, "SHELLDLL_DefView", null) == 0)
                SendMessageTimeout(progman, WmSpawnWorker, 0xD, 0x1, 2, 1000, out _);

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
            SetWindowLongPtr(hwnd, GwlExStyle, (exStyle & ~WsExAppWindow) | WsExNoActivate | WsExToolWindow);

            var shellDefView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
            bool raised = shellDefView != 0 && (GetWindowLongPtr(progman, GwlExStyle) & WsExNoRedirectionBitmap) != 0;
            if (raised)
            {
                if ((GetWindowLongPtr(hwnd, GwlExStyle) & WsExLayered) == 0)
                    throw new InvalidOperationException("WPF did not create a layered window.");
                SetParent(hwnd, progman);
                int error = Marshal.GetLastWin32Error();
                if (GetParent(hwnd) != progman)
                    throw new System.ComponentModel.Win32Exception(error, $"Raised desktop SetParent failed ({error}).");
                if (!SetWindowPos(hwnd, shellDefView, 0, 0, physW, physH, SwpNoActivate | SwpShowWindow | SwpFrameChanged))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ModeDescription = $"Raised desktop / layered WPF ({physW}x{physH})";
                return;
            }
            nint worker = 0;
            if (shellDefView != 0)
            {
                var candidate = FindWindowEx(progman, shellDefView, "WorkerW", null);
                if (UsableWorker(candidate, progman)) worker = candidate;
            }

            // Classic desktop: find the background sibling below the icon host.
            if (worker == 0)
                EnumWindows((topHandle, _) =>
                {
                    if (FindWindowEx(topHandle, 0, "SHELLDLL_DefView", null) == 0) return true;
                    var candidate = FindWindowEx(0, topHandle, "WorkerW", null);
                    if (UsableWorker(candidate, progman)) worker = candidate;
                    return worker == 0;
                }, 0);

            if (worker == 0 || !GetClientRect(worker, out var bounds))
                throw new InvalidOperationException("No visible desktop-sized background WorkerW found.");

            physW = bounds.Right - bounds.Left;
            physH = bounds.Bottom - bounds.Top;
            SetParent(hwnd, worker);
            int parentError = Marshal.GetLastWin32Error();
            if (GetParent(hwnd) != worker)
                throw new System.ComponentModel.Win32Exception(parentError, $"SetParent failed ({parentError}).");
            SyncWpfLogicalSize(window, physW, physH);
            if (!SetWindowPos(hwnd, 0, 0, 0, physW, physH, SwpNoActivate | SwpShowWindow | SwpFrameChanged))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ModeDescription = $"Desktop WorkerW 0x{worker:X} ({physW}x{physH})";
        }
        catch (Exception ex)
        {
            App.LogCrash("ApplyMode", ex);
            PinToBottom(window, hwnd);
            ModeDescription += $" (Auto attach failed: {ex.Message})";
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    private static bool UsableWorker(nint candidate, nint progman)
    {
        if (candidate == 0 || !IsWindowVisible(candidate)) return false;
        GetWindowThreadProcessId(candidate, out var pid);
        GetWindowThreadProcessId(progman, out var shellPid);
        return pid != 0 && pid == shellPid && FindWindowEx(candidate, 0, "SHELLDLL_DefView", null) == 0
            && GetClientRect(candidate, out var r) && r.Right > 100 && r.Bottom > 100;
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
        SetWindowLongPtr(hwnd, GwlExStyle, (exStyle & ~WsExAppWindow) | WsExNoActivate | WsExToolWindow);

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
        sb.AppendLine($"Z-order repairs: {_orderRepairs} | Last drift: {_lastOrderCheck}");
        sb.AppendLine($"Layered: {(GetWindowLongPtr(hwnd, GwlExStyle) & WsExLayered) != 0} | Visible: {IsWindowVisible(hwnd)}");
        if (window is MainWindow main) sb.AppendLine($"WPF render callbacks: {main.RenderFrameCount}");
        sb.AppendLine($"Our HWND: 0x{hwnd:X} | Parent: 0x{GetParent(hwnd):X}");
        sb.AppendLine($"Our Rect: ({rc.Left}, {rc.Top}) - ({rc.Right}, {rc.Bottom}) [{rc.Right - rc.Left}x{rc.Bottom - rc.Top}]");
        sb.AppendLine($"Progman: 0x{progman:X} | ExStyle: 0x{GetWindowLongPtr(progman, GwlExStyle):X}");
        sb.AppendLine($"Progman->SHELLDLL_DefView: 0x{defView:X} | Progman->WorkerW: 0x{workerW:X}");
        if (progman != 0)
        {
            sb.AppendLine("Progman direct children (top-to-bottom Z-order):");
            EnumChildWindows(progman, (child, _) =>
            {
                if (GetParent(child) != progman) return true;
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
