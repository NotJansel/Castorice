using System.Collections.ObjectModel;
using Castorice.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>
/// The tournament page's tabs: one per lobby being refereed, so a referee can run several matches
/// at once. Every tab has its own lobby, pool, teams and draft; they share the connection, its
/// rate limit and the lobby message switches. There is always at least one.
/// </summary>
public sealed partial class TournamentTabsViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private int _nextNumber = 1;

    [ObservableProperty]
    private TournamentViewModel? _selected;

    public TournamentTabsViewModel(AppServices services)
    {
        _services = services;
        Messages = new LobbyMessagesViewModel(_services.Settings.Announcements, _services.SaveSettings);
        Selected = Add();
    }

    public ObservableCollection<TournamentViewModel> Tabs { get; } = [];

    /// <summary>What every tab posts into its lobby by itself; set in Settings.</summary>
    public LobbyMessagesViewModel Messages { get; }

    public bool HasSeveralTabs => Tabs.Count > 1;

    partial void OnSelectedChanged(TournamentViewModel? oldValue, TournamentViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            // Another tab may have saved or imported a pool since this one was last on screen.
            newValue.RefreshPoolListQuietly();
            newValue.IsSelected = true;
        }
    }

    [RelayCommand]
    private void NewTab() => Selected = Add();

    /// <summary>
    /// Closes a tab. Its lobby, if it had one, stays open on Bancho and can be attached again in
    /// another tab by its id; only the tab's own draft and score are gone.
    /// </summary>
    [RelayCommand]
    private void CloseTab(TournamentViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }

        if (tab.Room is { } room)
        {
            _services.Chat.AppendClientNotice(
                _services.Chat.Open(room.ChannelName),
                $"Closed the tournament tab for {room.ChannelName}. The lobby is still open; attach to {room.MatchId} to referee it again.");
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (Tabs.Count == 0)
        {
            _nextNumber = 1;
            Add();
        }

        if (ReferenceEquals(Selected, tab) || Selected is null)
        {
            Selected = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(HasSeveralTabs));
    }

    /// <summary>The lobbies attached in any tab, for rejoining them after a reconnect.</summary>
    public IEnumerable<TournamentViewModel> Attached => Tabs.Where(t => t.IsAttached);

    public void Dispose()
    {
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
    }

    private TournamentViewModel Add()
    {
        var tab = new TournamentViewModel(_services, Messages, _nextNumber++);
        Tabs.Add(tab);
        OnPropertyChanged(nameof(HasSeveralTabs));
        return tab;
    }
}
