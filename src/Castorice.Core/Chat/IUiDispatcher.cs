namespace Castorice.Core.Chat;

/// <summary>
/// Lets core services mutate observable collections without referencing a UI framework.
/// The desktop app supplies an Avalonia-backed implementation; tests use <see cref="Immediate"/>.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);

    static IUiDispatcher Immediate { get; } = new ImmediateDispatcher();

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
