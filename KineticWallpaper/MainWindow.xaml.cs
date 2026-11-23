using System.Windows;

namespace KineticWallpaper;

public partial class MainWindow : Window
{
    private WallpaperWindow? wallpaper;
    public MainWindow()
    {
        InitializeComponent();
        Loaded += Start;
        Closed += (_, _) => wallpaper?.Close();
    }
    private void Start(object sender, RoutedEventArgs e)
    {
        try
        {
            wallpaper = new WallpaperWindow(message => Status.Text = message);
            wallpaper.Show();
        }
        catch (Exception ex)
        {
            Status.Text = "Could not start wallpaper: " + ex.Message;
            wallpaper?.Close(); wallpaper = null;
        }
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
