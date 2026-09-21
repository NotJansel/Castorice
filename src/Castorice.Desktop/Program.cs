using Avalonia;

namespace Castorice.Desktop;

internal static class Program
{
    // Avalonia needs to be initialised before anything touches its types, so keep this method free
    // of SynchronizationContext-dependent code.
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
