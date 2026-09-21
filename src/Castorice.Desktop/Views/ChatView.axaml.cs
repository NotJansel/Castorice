using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Castorice.Desktop.ViewModels;

namespace Castorice.Desktop.Views;

public partial class ChatView : UserControl
{
    private ScrollViewer? _scroller;

    public ChatView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scroller = this.FindControl<ScrollViewer>("MessageScroller");
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (viewModel.SendCommand.CanExecute(null))
                {
                    viewModel.SendCommand.Execute(null);
                }

                ScrollToBottom();
                break;

            case Key.Up:
                e.Handled = true;
                viewModel.RecallHistory(-1);
                MoveCaretToEnd(sender);
                break;

            case Key.Down:
                e.Handled = true;
                viewModel.RecallHistory(1);
                MoveCaretToEnd(sender);
                break;
        }
    }

    private void OnJoinKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.JoinCommand.CanExecute(null))
        {
            viewModel.JoinCommand.Execute(null);
        }
    }

    /// <summary>
    /// Keeps the newest message in view. Posted at background priority so the new item is measured
    /// before the scroll offset is applied.
    /// </summary>
    private void ScrollToBottom() =>
        Dispatcher.UIThread.Post(() => _scroller?.ScrollToEnd(), DispatcherPriority.Background);

    private static void MoveCaretToEnd(object? sender)
    {
        if (sender is TextBox box)
        {
            box.CaretIndex = box.Text?.Length ?? 0;
        }
    }
}
