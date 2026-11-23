using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace KineticWallpaper;

// Explorer's desktop windows are undocumented and can change between Windows builds.
public static class DesktopWallpaperHost
{
    public static void Attach(Window window)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("Explorer's Progman window was not found.");
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);
        IntPtr worker = IntPtr.Zero;
        bool aboveIcons = Environment.GetCommandLineArgs().Contains("--above-icons", StringComparer.OrdinalIgnoreCase);
        var iconView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        // On modern Explorer the wallpaper WorkerW is a direct child of Progman.
        if (iconView != IntPtr.Zero)
            worker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (worker == IntPtr.Zero)
        {
            EnumWindows((top, _) =>
            {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return worker == IntPtr.Zero;
            }, IntPtr.Zero);
        }
        if (worker == IntPtr.Zero) throw new InvalidOperationException("Could not locate Explorer's wallpaper WorkerW.");
        if (aboveIcons)
        {
            if (iconView == IntPtr.Zero) throw new InvalidOperationException("Above-icons diagnostic requires the modern Progman desktop layout.");
            worker = progman;
        }
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int style = GetWindowLong(hwnd, -16);
        SetStyle(hwnd, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
        int exstyle = GetWindowLong(hwnd, -20);
        SetStyle(hwnd, -20, exstyle | 0x08000080);
        Marshal.SetLastPInvokeError(0);
        var previous = SetParent(hwnd, worker);
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0) Fail("SetParent");
        Fit();
        void OnDisplayChanged(object? sender, EventArgs args)
        {
            if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke((Action)Fit);
        }
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        window.Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            // Invalidate Explorer's desktop rather than changing the user's wallpaper settings.
            RedrawWindow(worker, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100);
        };
        void Fit()
        {
            var origin = new PointNative { X = GetSystemMetrics(76), Y = GetSystemMetrics(77) };
            if (!ScreenToClient(worker, ref origin)) Fail("ScreenToClient");
            uint flags = 0x0010 | 0x0020 | 0x0040; // no activation, frame changed, show
            if (!aboveIcons) flags |= 0x0004; // preserve z-order normally; HWND_TOP for the diagnostic
            if (!SetWindowPos(hwnd, IntPtr.Zero, origin.X, origin.Y, GetSystemMetrics(78), GetSystemMetrics(79), flags)) Fail("SetWindowPos");
        }
    }
    private static void SetStyle(IntPtr h, int index, int value)
    {
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLong(h, index, value) == 0 && Marshal.GetLastPInvokeError() != 0) Fail("SetWindowLong");
    }
    private static void Fail(string action) => throw new Win32Exception(Marshal.GetLastPInvokeError(), action + " failed");
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? name, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
}
