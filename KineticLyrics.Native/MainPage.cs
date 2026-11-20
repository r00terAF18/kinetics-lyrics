using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;
using Microsoft.Maui.Controls.Shapes;

namespace KineticLyrics.Native;

public sealed class MainPage : ContentPage
{
    private readonly MediaSessionService _media;
    private readonly ILocalLyricsProvider _local;
    private readonly IWallpaperHost _wallpaper;
    private readonly Label _trackLabel;
    private readonly Label _artistLabel;
    private readonly Label _statusLabel;
    private readonly Label _accessLabel;
    private bool _started;
#if WINDOWS
    private const string RendererTitle = "Live desktop wallpaper";
    private const string RendererDescription = "Lyrics appear directly behind your desktop icons.";
#else
    private const string RendererTitle = "Android live wallpaper";
    private const string RendererDescription = "Android WallpaperService hosting is still to be added.";
#endif

    public MainPage(MediaSessionService media, ILocalLyricsProvider local, IWallpaperHost wallpaper)
    {
        _media = media;
        _local = local;
        _wallpaper = wallpaper;
        BackgroundColor = Color.FromArgb("#0B0B10");
        NavigationPage.SetHasNavigationBar(this, false);

        _trackLabel = new Label { Text = "Waiting for music", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#F4F1EA") };
        _artistLabel = new Label { Text = "Play a track in any media app", FontSize = 13, TextColor = Color.FromArgb("#A8A5AF") };
        _statusLabel = new Label { Text = _wallpaper.Status, FontSize = 12, TextColor = Color.FromArgb("#FF9BAE") };
        _accessLabel = new Label { Text = _media.AccessStatus, FontSize = 12, TextColor = Color.FromArgb("#C6C4CB") };

        var titleCard = new Border
        {
            BackgroundColor = Color.FromArgb("#15151D"),
            Stroke = Color.FromArgb("#292832"),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = 16,
            Content = new VerticalStackLayout { Spacing = 4, Children = { _trackLabel, _artistLabel } }
        };

        var offset = Math.Clamp(Preferences.Default.Get("lyrics_offset_seconds", 0d), -2, 2);
        var offsetLabel = new Label { Text = $"Lyrics timing: {offset:+0.00;-0.00;0.00} s", FontSize = 13, TextColor = Color.FromArgb("#D7D5DC") };
        var offsetSlider = new Slider { Minimum = -2, Maximum = 2, Value = offset, MinimumTrackColor = Color.FromArgb("#FF4D6D"), ThumbColor = Color.FromArgb("#FF4D6D") };
        offsetSlider.ValueChanged += (_, args) =>
        {
            _wallpaper.SetLyricsOffset(args.NewValue);
            Preferences.Default.Set("lyrics_offset_seconds", args.NewValue);
            offsetLabel.Text = $"Lyrics timing: {args.NewValue:+0.00;-0.00;0.00} s";
        };

        var folderButton = new Button { Text = "Choose music folder", BackgroundColor = Color.FromArgb("#292832"), TextColor = Colors.White, CornerRadius = 12 };
        folderButton.Clicked += async (_, _) =>
        {
            try
            {
                var result = await _local.ChooseMusicFolderAsync();
                _accessLabel.Text = result is null ? _local.AccessStatus : $"Lyrics folder: {result}";
                await _wallpaper.RefreshLyricsAsync();
            }
            catch (Exception ex) { _accessLabel.Text = ex.Message; }
        };

        var accessButton = new Button { Text = "Refresh media access", BackgroundColor = Color.FromArgb("#292832"), TextColor = Colors.White, CornerRadius = 12 };
        accessButton.Clicked += async (_, _) =>
        {
            await _media.RequestAccessAsync();
            _accessLabel.Text = _media.AccessStatus;
        };

        var layout = new VerticalStackLayout
        {
            Padding = new Thickness(22, 26),
            Spacing = 14,
            Children =
            {
                new Label { Text = "KINETIC LYRICS", FontSize = 12, CharacterSpacing = 2.2, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FF4D6D") },
                new Label { Text = RendererTitle, FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label { Text = RendererDescription, FontSize = 13, TextColor = Color.FromArgb("#A8A5AF") },
                titleCard,
                _statusLabel,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#292832"), Margin = new Thickness(0, 2) },
                new Label { Text = "Configuration", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                offsetLabel,
                offsetSlider,
                folderButton,
                accessButton,
                _accessLabel
            }
        };
        Content = new ScrollView { Content = layout };
        _media.TrackChanged += OnTrackChanged;
        _wallpaper.StatusChanged += OnWallpaperStatusChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if WINDOWS
        ResizeConfigurationWindow();
#endif
        if (_started) return;
        _started = true;
        await _wallpaper.StartAsync();
        await _media.StartAsync();
        _accessLabel.Text = _media.AccessStatus;
        _statusLabel.Text = _wallpaper.Status;
    }

#if WINDOWS
    private static void ResizeConfigurationWindow()
    {
        var nativeWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        nativeWindow?.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, 620));
    }
#endif

    private void OnWallpaperStatusChanged(object? sender, string status) =>
        MainThread.BeginInvokeOnMainThread(() => _statusLabel.Text = status);

    private void OnTrackChanged(object? sender, MediaTrackSnapshot? track)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _trackLabel.Text = track?.Title ?? "Waiting for music";
            _artistLabel.Text = track?.Artist ?? "Play a track in any media app";
        });
    }
}
