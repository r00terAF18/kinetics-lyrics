using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KineticWallpaper;

// Host code (DesktopWallpaperHost.cs) expects these two names.
internal static class App
{
    public static void LogCrash(string source, Exception ex) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "kinetic-webview-test.txt"), $"[{DateTime.Now:T}] {source}: {ex}\r\n");
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) => { App.LogCrash("Dispatcher", e.Exception); e.Handled = true; };
        app.Run(new MainWindow(args.FirstOrDefault()?.ToLowerInvariant() ?? "auto"));
    }
}

public sealed class MainWindow : Window
{
    private const string Html = """
        <!doctype html><meta charset="utf-8"><style>html,body{margin:0;height:100%;overflow:hidden;background:#101018;color:#fff;font:700 6vw Segoe UI}
        canvas{position:fixed;inset:0;width:100%;height:100%}#t{position:fixed;left:4vw;top:4vh}#f{font:600 2.2vw Consolas;opacity:.8}</style>
        <canvas id=c></canvas><div id=t>WEBVIEW2 TEST<div id=f></div></div>
        <script>const c=document.getElementById('c'),x=c.getContext('2d');let n=0,last=performance.now(),fps=0;
        function r(){c.width=innerWidth;c.height=innerHeight;}r();addEventListener('resize',r);
        (function f(t){n++;const w=c.width,h=c.height,a=t/1000;
          const g=x.createLinearGradient(0,0,w,h);g.addColorStop(0,`hsl(${a*40%360},80%,35%)`);g.addColorStop(1,`hsl(${(a*40+120)%360},80%,25%)`);
          x.fillStyle=g;x.fillRect(0,0,w,h);
          for(let i=0;i<8;i++){x.beginPath();x.arc(w/2+Math.cos(a+i)*w*.3,h/2+Math.sin(a*1.3+i)*h*.3,h*.1,0,7);x.fillStyle='rgba(255,255,255,.18)';x.fill();}
          if(t-last>500){fps=Math.round(n*1000/(t-last));n=0;last=t;document.getElementById('f').textContent=fps+' fps  '+new Date().toLocaleTimeString();}
          requestAnimationFrame(f)})(performance.now());</script>
        """;

    private readonly WebView2 _web = new();
    private readonly string _mode;
    public long RenderFrameCount { get; private set; }

    public MainWindow(string mode)
    {
        _mode = mode;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Width = 1280; Height = 720;
        Content = _web;
        CompositionTarget.Rendering += (_, _) => RenderFrameCount++;
        Loaded += async (_, _) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "kinetic-webview-test-data"));
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.NavigateToString(Html);
                DesktopWallpaperHost.Attach(this, _mode switch
                {
                    "bottom" => DesktopAttachMode.BottomMostWindow,
                    "float" => DesktopAttachMode.FloatingWindow,
                    _ => DesktopAttachMode.AutoShell
                });
                var log = Path.Combine(Path.GetTempPath(), "kinetic-webview-test.txt");
                File.WriteAllText(log, $"mode arg: {_mode}\r\n");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (_, _) => File.AppendAllText(log, $"--- {DateTime.Now:T}\r\n{DesktopWallpaperHost.GetDiagnostics(this)}\r\n");
                timer.Start();
            }
            catch (Exception ex) { App.LogCrash("Startup", ex); }
        };
    }
}
