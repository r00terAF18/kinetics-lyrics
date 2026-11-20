using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace KineticLyrics.Native;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int MusicFolderRequestCode = 4102;
    private static TaskCompletionSource<Intent?>? _folderPicker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        EnterImmersiveMode();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            EnterImmersiveMode();
    }

    private void EnterImmersiveMode()
    {
        if (Window is null)
            return;
        Window.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.Fullscreen |
            SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky |
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation);
    }

    internal static Task<Intent?> ChooseMusicFolderAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            throw new InvalidOperationException("The Android activity is not available.");

        _folderPicker = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), MusicFolderRequestCode);
        return _folderPicker.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != MusicFolderRequestCode)
            return;
        _folderPicker?.TrySetResult(resultCode == Result.Ok ? data : null);
        _folderPicker = null;
    }
}
