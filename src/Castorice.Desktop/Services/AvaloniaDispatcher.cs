using Avalonia.Threading;
using Castorice.Core.Chat;

namespace Castorice.Desktop.Services;

/// <summary>
/// Marshals core-service callbacks onto the UI thread. IRC arrives on a background read loop, and
/// every observable collection the views bind to is mutated from those callbacks.
/// </summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public static AvaloniaDispatcher Instance { get; } = new();

    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}
