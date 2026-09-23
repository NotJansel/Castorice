using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Castorice.Desktop.Services;
using Castorice.Desktop.ViewModels;

namespace Castorice.Desktop.Views;

public partial class ChatView : UserControl
{
    private ScrollViewer? _scroller;
    private ChatViewModel? _viewModel;

    public ChatView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scroller = this.FindControl<ScrollViewer>("MessageScroller");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ChatViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// A conversation opens at its newest message. Being scrolled up in one channel says nothing
    /// about the next, so switching resumes following.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.SelectedTarget))
        {
            ScrollToLatest();
        }
    }

    private void OnJumpToLatest(object? sender, RoutedEventArgs e) => ScrollToLatest();

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

                ScrollToLatest();
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

    private void ScrollToLatest()
    {
        if (_scroller is not null)
        {
            AutoScroll.ScrollToLatest(_scroller);
        }
    }

    private static void MoveCaretToEnd(object? sender)
    {
        if (sender is TextBox box)
        {
            box.CaretIndex = box.Text?.Length ?? 0;
        }
    }
}
