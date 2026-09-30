using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Castorice.Core.Configuration;
using Castorice.Desktop.Services;
using Castorice.Desktop.ViewModels;
using Castorice.Desktop.Views;

namespace Castorice.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // macOS titles the app menu with this, and Avalonia's default is "Avalonia Application".
        Name = AppInfo.Name;
    }

    private void OnAboutClicked(object? sender, EventArgs e)
    {
        var about = new AboutWindow();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } owner })
        {
            _ = about.ShowDialog(owner);
        }
        else
        {
            about.Show();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureCreated();

            // The Dock ignores the window icon, so macOS gets its own.
            if (OperatingSystem.IsMacOS())
            {
                MacDockIcon.Apply();
            }

            var viewModel = new MainWindowViewModel();
            RequestedThemeVariant = viewModel.Settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            viewModel.ThemeChanged += (_, variant) => RequestedThemeVariant = variant;

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) => await viewModel.ShutdownAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
