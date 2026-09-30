using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Castorice.Desktop.Views;

/// <summary>Replaces Avalonia's own About box in the macOS app menu.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AppName.Text = AppInfo.Name;
        Tagline.Text = AppInfo.Tagline;
        VersionText.Text = $"Version {AppInfo.DisplayVersion}";
    }

    private async void OnOpenRepository(object? sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(AppInfo.Repository));
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            // No browser to hand the link to; the window stays open and nothing else happens.
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
