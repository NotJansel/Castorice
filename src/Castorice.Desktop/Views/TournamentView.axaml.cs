using Avalonia.Controls;
using Avalonia.Interactivity;
using Castorice.Desktop.Services;

namespace Castorice.Desktop.Views;

public partial class TournamentView : UserControl
{
    public TournamentView() => InitializeComponent();

    private void OnJumpToLatestLog(object? sender, RoutedEventArgs e) => AutoScroll.ScrollToLatest(LobbyLogScroller);
}
