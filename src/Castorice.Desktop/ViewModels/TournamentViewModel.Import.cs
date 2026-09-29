using System.Collections.ObjectModel;
using System.Globalization;
using Castorice.Core.MappoolBuilder;
using Castorice.Core.Tournament;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>A pool offered for import, as listed by the Mappool Builder.</summary>
public sealed record RemotePoolItem(string Id, string Name, string Subtitle, bool IsPrivate, bool IsImported);

/// <summary>Importing pools from the Mappool Builder and keeping them up to date.</summary>
public sealed partial class TournamentViewModel
{
    private bool _remotePoolsLoaded;

    [ObservableProperty]
    private bool _isImportOpen;

    [ObservableProperty]
    private string _importInput = string.Empty;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    private bool _isImportBusy;

    /// <summary>The token owner's pools; empty without a token.</summary>
    public ObservableCollection<RemotePoolItem> MyRemotePools { get; } = [];

    /// <summary>Recently changed public pools of other users.</summary>
    public ObservableCollection<RemotePoolItem> DiscoverRemotePools { get; } = [];

    public bool HasMyRemotePools => MyRemotePools.Count > 0;

    public bool HasDiscoverRemotePools => DiscoverRemotePools.Count > 0;

    public string PoolBuilderHost
    {
        get
        {
            try
            {
                return _services.PoolBuilderConnection.Host;
            }
            catch (MappoolBuilderException)
            {
                return "the Mappool Builder";
            }
        }
    }

    public bool HasPoolSource => Pool.Source is not null;

    /// <summary>E.g. <c>From pools.jansel.dev · by peppy · updated 28 Sep 2026, 18:02</c>.</summary>
    public string PoolSourceDisplay => Pool.Source is { } source
        ? $"From {HostOf(source.BaseUrl)}" +
          (source.OwnerName.Length > 0 ? $" · by {source.OwnerName}" : string.Empty) +
          $" · updated {FormatTime(source.UpdatedAtTime)}"
        : string.Empty;

    partial void OnIsImportOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(PoolBuilderHost));

        if (value && !_remotePoolsLoaded)
        {
            _ = LoadRemotePoolsAsync();
        }
    }

    [RelayCommand]
    private async Task LoadRemotePoolsAsync()
    {
        if (IsImportBusy)
        {
            return;
        }

        var connection = _services.PoolBuilderConnection;
        IsImportBusy = true;
        ImportStatus = $"Loading pools from {SafeHost(connection)}…";

        try
        {
            var list = await _services.PoolBuilder.ListPoolsAsync(connection);
            var imported = ImportedPoolIds(connection);

            Fill(MyRemotePools, list.Mine, imported);
            Fill(DiscoverRemotePools, list.Discover, imported);
            _remotePoolsLoaded = true;

            ImportStatus = connection.HasToken
                ? $"{list.Mine.Count} of your pools and {list.Discover.Count} recent public ones."
                : $"{list.Discover.Count} recent public pools. Add an API token in Settings to see your own, private ones included.";
        }
        catch (Exception ex)
        {
            ImportStatus = ex.Message;
        }
        finally
        {
            IsImportBusy = false;
            OnPropertyChanged(nameof(HasMyRemotePools));
            OnPropertyChanged(nameof(HasDiscoverRemotePools));
        }
    }

    [RelayCommand]
    private Task ImportFromInputAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportInput))
        {
            ImportStatus = "Paste a pool link or its id, e.g. Xb3kQ9aZ.";
            return Task.CompletedTask;
        }

        return ImportPoolAsync(ImportInput);
    }

    [RelayCommand]
    private Task ImportRemotePoolAsync(RemotePoolItem? item) =>
        item is null ? Task.CompletedTask : ImportPoolAsync(item.Id);

    /// <summary>
    /// Fetches the pool and opens it. A pool imported before is updated in its existing file, so
    /// its local settings stay; a new one gets a file of its own and never overwrites another.
    /// </summary>
    private async Task ImportPoolAsync(string idOrLink)
    {
        if (IsImportBusy)
        {
            return;
        }

        var connection = _services.PoolBuilderConnection;
        IsImportBusy = true;
        ImportStatus = "Importing…";

        try
        {
            var remote = await _services.PoolBuilder.GetPoolAsync(connection, idOrLink);
            var store = _services.Mappools;
            var existing = store.FindBySource(connection.Origin.ToString(), remote.Id);

            string fileName;
            string message;

            if (existing is not null && LoadForUpdate(existing) is { } local)
            {
                var result = MappoolBuilderImport.Refresh(local, remote, connection);
                fileName = store.Save(local, existing);
                message = $"Updated \"{local.Name}\" from {connection.Host}: {result.Describe()}.";
            }
            else
            {
                var pool = MappoolBuilderImport.ToMappool(remote, connection);
                fileName = store.Save(pool, store.SuggestUnusedFileName(pool.Name));
                message = $"Imported \"{pool.Name}\" with {pool.Slots.Count} maps from {connection.Host}.";
            }

            // The Builder keeps team names on the pool; they only fill boxes still left empty.
            if (string.IsNullOrWhiteSpace(RedTeam) && !string.IsNullOrWhiteSpace(remote.TeamRed))
            {
                RedTeam = remote.TeamRed.Trim();
            }

            if (string.IsNullOrWhiteSpace(BlueTeam) && !string.IsNullOrWhiteSpace(remote.TeamBlue))
            {
                BlueTeam = remote.TeamBlue.Trim();
            }

            OpenPoolFile(fileName);
            MarkImported(remote.Id);

            ImportInput = string.Empty;
            ImportStatus = message;
            Status = message;
        }
        catch (Exception ex)
        {
            ImportStatus = ex.Message;
        }
        finally
        {
            IsImportBusy = false;
        }
    }

    /// <summary>
    /// Pulls the maps of the open pool from where it was imported. Local settings stay; the maps
    /// follow the Builder.
    /// </summary>
    [RelayCommand]
    private async Task UpdateFromSourceAsync()
    {
        if (Pool.Source is not { } source || IsImportBusy)
        {
            return;
        }

        var connection = ConnectionFor(source);
        IsImportBusy = true;
        Status = $"Checking {connection.Host} for changes…";

        try
        {
            var remote = await _services.PoolBuilder.GetPoolAsync(connection, source.PoolId);

            if (remote.UpdatedAt == source.UpdatedAt)
            {
                Status = $"\"{Pool.Name}\" is up to date with {connection.Host}.";
                return;
            }

            var hadMarks = AllSlots.Any(s => s.HasAvailabilityMark || s.IsPicked);
            var result = MappoolBuilderImport.Refresh(Pool, remote, connection);

            if (SelectedPoolFile is { } file)
            {
                _services.Mappools.Save(Pool, file.FileName);
            }

            ApplyPool(Pool);

            Status = $"Updated \"{Pool.Name}\" from {connection.Host}: {result.Describe()}." +
                (hadMarks ? " Bans, protects and picks were cleared with the old maps." : string.Empty);
        }
        catch (Exception ex)
        {
            Status = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsImportBusy = false;
        }
    }

    /// <summary>
    /// The connection to reach a pool's source. The token is only ever sent to the Builder it was
    /// made for — a pool imported from another address is read without it.
    /// </summary>
    private MappoolBuilderConnection ConnectionFor(MappoolSource source)
    {
        var configured = _services.PoolBuilderConnection;
        var sameHost = string.Equals(HostOf(source.BaseUrl), SafeHost(configured), StringComparison.OrdinalIgnoreCase);

        return new MappoolBuilderConnection(source.BaseUrl, sameHost ? configured.Token : null);
    }

    /// <summary>The open pool, unsaved edits included, when it is the one being updated.</summary>
    private Mappool? LoadForUpdate(string fileName) =>
        SelectedPoolFile?.FileName == fileName ? Pool : _services.Mappools.Load(fileName);

    /// <summary>Selects the file and loads it, even when it was already the selected one.</summary>
    private void OpenPoolFile(string fileName)
    {
        SelectedPoolFile = null;
        RefreshPoolList();
        SelectedPoolFile = PoolFiles.FirstOrDefault(f => f.FileName == fileName);
    }

    private HashSet<string> ImportedPoolIds(MappoolBuilderConnection connection)
    {
        var host = SafeHost(connection);

        return _services.Mappools.List()
            .Select(file => _services.Mappools.Load(file.FileName)?.Source)
            .OfType<MappoolSource>()
            .Where(source => string.Equals(HostOf(source.BaseUrl), host, StringComparison.OrdinalIgnoreCase))
            .Select(source => source.PoolId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void MarkImported(string poolId)
    {
        foreach (var list in new[] { MyRemotePools, DiscoverRemotePools })
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Id == poolId && !list[i].IsImported)
                {
                    list[i] = list[i] with { IsImported = true };
                }
            }
        }
    }

    private static void Fill(ObservableCollection<RemotePoolItem> target, IEnumerable<RemotePool> pools, HashSet<string> imported)
    {
        target.Clear();
        foreach (var pool in pools)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(pool.Stage))
            {
                parts.Add(pool.Stage.Trim());
            }

            if (pool.Owner is { Username.Length: > 0 } owner)
            {
                parts.Add($"by {owner.Username}");
            }

            parts.Add($"{pool.Slots.Count} {(pool.Slots.Count == 1 ? "map" : "maps")}");
            parts.Add($"updated {FormatTime(pool.UpdatedAtTime)}");

            target.Add(new RemotePoolItem(
                pool.Id,
                string.IsNullOrWhiteSpace(pool.Name) ? pool.Id : pool.Name.Trim(),
                string.Join(" · ", parts),
                !pool.IsPublic,
                imported.Contains(pool.Id)));
        }
    }

    private static string SafeHost(MappoolBuilderConnection connection)
    {
        try
        {
            return connection.Host;
        }
        catch (MappoolBuilderException)
        {
            return connection.BaseUrl;
        }
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;

    private static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Re-raises what depends on the pool's source after a different pool was loaded.</summary>
    private void RefreshPoolSource()
    {
        OnPropertyChanged(nameof(HasPoolSource));
        OnPropertyChanged(nameof(PoolSourceDisplay));
    }
}
