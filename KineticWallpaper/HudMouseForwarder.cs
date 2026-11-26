using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace KineticWallpaper;

internal sealed class HudMouseForwarder : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private readonly record struct HudRect(double Left, double Top, double Right, double Bottom)
    {
        public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    private readonly CoreWebView2 _webView;
    private readonly Func<(int X, int Y, int W, int H)> _getScreenRect;
    private readonly HookProc _callback;
    private HudRect[] _buttons = [];
    private double _viewportWidth;
    private double _viewportHeight;
    private nint _hook;
    private bool _captured;
    private int _pressedButton = -1;
    private bool _disposed;

    public HudMouseForwarder(CoreWebView2 webView, Func<(int X, int Y, int W, int H)> getScreenRect)
    {
        _webView = webView;
        _getScreenRect = getScreenRect;
        _callback = OnMouse;
    }

    public void UpdateBounds(JsonElement data)
    {
        try
        {
            var width = data.GetProperty("width").GetDouble();
            var height = data.GetProperty("height").GetDouble();
            var buttons = data.GetProperty("buttons").EnumerateArray()
                .Select(button => new HudRect(
                    button.GetProperty("left").GetDouble(),
                    button.GetProperty("top").GetDouble(),
                    button.GetProperty("right").GetDouble(),
                    button.GetProperty("bottom").GetDouble()))
                .ToArray();

            if (width <= 0 || height <= 0) return;
            _viewportWidth = width;
            _viewportHeight = height;
            _buttons = buttons;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _buttons = [];
            _viewportWidth = _viewportHeight = 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        if (enabled && _hook == 0)
            _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
        else if (!enabled && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
            _captured = false;
            _pressedButton = -1;
        }
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code < 0 || _hook == 0)
            return CallNextHookEx(_hook, code, wParam, lParam);

        try
        {
            var message = unchecked((int)wParam);
            if (message != WmLButtonDown && message != WmLButtonUp)
                return CallNextHookEx(_hook, code, wParam, lParam);

            var point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).Point;
            if (message == WmLButtonDown && _buttons.Length > 0 &&
                DesktopWallpaperHost.IsDesktopPoint(point.X, point.Y) && TryMapToHud(point, out _, out _, out _pressedButton))
            {
                _captured = true;
                return 1; // The shell must not also act on this HUD click.
            }

            if (message == WmLButtonUp && _captured)
            {
                _captured = false;
                double x = 0, y = 0;
                int releasedButton = -1;
                bool sameButton = DesktopWallpaperHost.IsDesktopPoint(point.X, point.Y) &&
                    TryMapToHud(point, out x, out y, out releasedButton) && releasedButton == _pressedButton;
                _pressedButton = -1;
                if (sameButton)
                    DispatchClick(x, y);
                return 1;
            }
        }
        catch
        {
            // Never allow a forwarding failure to break the system mouse hook chain.
            _captured = false;
            _pressedButton = -1;
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool TryMapToHud(POINT point, out double x, out double y, out int buttonIndex)
    {
        x = y = 0;
        buttonIndex = -1;
        if (_viewportWidth <= 0 || _viewportHeight <= 0) return false;

        var screen = _getScreenRect();
        if (screen.W <= 0 || screen.H <= 0) return false;
        x = (point.X - screen.X) * _viewportWidth / screen.W;
        y = (point.Y - screen.Y) * _viewportHeight / screen.H;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (!_buttons[i].Contains(x, y)) continue;
            buttonIndex = i;
            return true;
        }
        return false;
    }

    private void DispatchClick(double x, double y)
    {
        string cx = x.ToString("0.###", CultureInfo.InvariantCulture);
        string cy = y.ToString("0.###", CultureInfo.InvariantCulture);
        string script = $"(()=>{{const e=document.elementFromPoint({cx},{cy})?.closest('#style-bar button,#sync button');if(e)e.click();}})()";
        _ = _webView.ExecuteScriptAsync(script);
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetEnabled(false);
        _disposed = true;
        GC.KeepAlive(_callback);
    }
}
