using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace KineticWallpaper;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"Kinetic Wallpaper encountered an error:\n\n{e.Exception.Message}\n\nDetails saved to %LocalAppData%\\KineticLyrics\\crash.log",
            "Kinetic Wallpaper Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash("DomainUnhandledException", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    public static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}:\n{ex}\n----------------------------------------\n");
        }
        catch { }
    }
}
