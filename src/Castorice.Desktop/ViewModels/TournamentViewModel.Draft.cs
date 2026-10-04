using System.Runtime.CompilerServices;
using Castorice.Core.Tournament;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castorice.Desktop.ViewModels;

/// <summary>An option in a drop-down: the value it stands for and how it reads.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Protect, ban and pick order, and the lines posted into the lobby as the draft moves.</summary>
public sealed partial class TournamentViewModel
{
    // The winner of every scored map in this match, oldest first; null for a tie. Loser-picks and
    // winner-picks brackets read the last entry.
    private readonly List<TeamColour?> _mapWinners = [];

    // Hands out ever-increasing stamps so Undo can find the newest mark.
    private long _markCounter;

    // Protects and bans a team passed on or forfeited, and picks it lost, this match; stamped like
    // the marks so Undo can take them back. A forfeit adds one entry per ban, all sharing a stamp,
    // so it undoes whole.
    private readonly List<(DraftSkip Skip, long Stamp)> _skips = [];

    // Set while clearing or undoing, which should never post anything into the lobby.
    private bool _quietMarks;

    [ObservableProperty]
    private TeamColour _firstProtect = TeamColour.Red;

    [ObservableProperty]
    private TeamColour _firstBan = TeamColour.Red;

    [ObservableProperty]
    private TeamColour _firstPick = TeamColour.Red;

    /// <summary>
    /// While a protect or ban is due, clicking a map marks it for the team in turn instead of
    /// sending it to the lobby. Warmup picks always go to the lobby.
    /// </summary>
    [ObservableProperty]
    private bool _clickFollowsDraft = true;

    public LobbyMessagesViewModel Messages { get; }

    public IReadOnlyList<TeamColour> Teams { get; } = [TeamColour.Red, TeamColour.Blue];

    public IReadOnlyList<Choice<TurnOrder>> TurnOrderChoices { get; } =
    [
        new(TurnOrder.Alternating, "ABAB — alternating"),
        new(TurnOrder.Snake, "ABBA — snake"),
    ];

    public IReadOnlyList<Choice<PickOrder>> PickOrderChoices { get; } =
    [
        new(PickOrder.Alternating, "ABAB — alternating"),
        new(PickOrder.Snake, "ABBA — snake"),
        new(PickOrder.LoserPicks, "Loser of the last map picks"),
        new(PickOrder.WinnerPicks, "Winner of the last map picks"),
    ];

    // ---- pool rules --------------------------------------------------------

    /// <summary>Whether this bracket has protects at all. Turning it on starts at one per team.</summary>
    public bool UseProtects
    {
        get => Pool.Draft.HasProtects;
        set
        {
            if (value != Pool.Draft.HasProtects)
            {
                ProtectsPerTeam = value ? 1 : 0;
            }
        }
    }

    public int ProtectsPerTeam
    {
        get => Pool.Draft.ProtectsPerTeam;
        set => SetDraftRule(
            Pool.Draft.ProtectsPerTeam,
            Math.Max(0, value),
            v => Pool.Draft.ProtectsPerTeam = v,
            nameof(UseProtects));
    }

    public Choice<TurnOrder> ProtectOrderChoice
    {
        get => TurnOrderChoices.First(c => c.Value == Pool.Draft.ProtectOrder);
        set => SetDraftRule(Pool.Draft.ProtectOrder, value?.Value ?? TurnOrder.Alternating, v => Pool.Draft.ProtectOrder = v);
    }

    public int BansPerTeam
    {
        get => Pool.Draft.BansPerTeam;
        set => SetDraftRule(Pool.Draft.BansPerTeam, Math.Max(0, value), v => Pool.Draft.BansPerTeam = v);
    }

    public Choice<TurnOrder> BanOrderChoice
    {
        get => TurnOrderChoices.First(c => c.Value == Pool.Draft.BanOrder);
        set => SetDraftRule(Pool.Draft.BanOrder, value?.Value ?? TurnOrder.Alternating, v => Pool.Draft.BanOrder = v);
    }

    public Choice<PickOrder> PickOrderChoice
    {
        get => PickOrderChoices.First(c => c.Value == Pool.Draft.PickOrder);
        set => SetDraftRule(
            Pool.Draft.PickOrder,
            value?.Value ?? PickOrder.Alternating,
            v => Pool.Draft.PickOrder = v);
    }

    public int SecondBanRoundAfterPicks
    {
        get => Pool.Draft.SecondBanRoundAfterPicks;
        set => SetDraftRule(
            Pool.Draft.SecondBanRoundAfterPicks,
            Math.Max(0, value),
            v => Pool.Draft.SecondBanRoundAfterPicks = v,
            nameof(HasSecondBanRound));
    }

    public int SecondRoundBansPerTeam
    {
        get => Pool.Draft.SecondRoundBansPerTeam;
        set => SetDraftRule(
            Pool.Draft.SecondRoundBansPerTeam,
            Math.Max(0, value),
            v => Pool.Draft.SecondRoundBansPerTeam = v,
            nameof(HasSecondBanRound));
    }

    public bool SecondRoundOtherTeamFirst
    {
        get => Pool.Draft.SecondRoundOtherTeamFirst;
        set => SetDraftRule(Pool.Draft.SecondRoundOtherTeamFirst, value, v => Pool.Draft.SecondRoundOtherTeamFirst = v);
    }

    public bool HasSecondBanRound => Pool.Draft.SecondBanRoundAfterPicks > 0;

    // ---- where the match stands --------------------------------------------

    private TeamNames Names => TeamNames.From(RedTeam, BlueTeam);

    private DraftState CurrentDraft => DraftOrder.Evaluate(
        Pool.Draft,
        new DraftStart(FirstProtect, FirstBan, FirstPick),
        BuildDraftProgress());

    /// <summary>For the lobby panel, e.g. <c>Blue bans · 2 of 4</c>.</summary>
    public string NextTurnDisplay => CurrentDraft.Next.Describe(Names, Pool.Draft.PickOrder);

    /// <summary>The team whose turn it is, for the dot beside <see cref="NextTurnDisplay"/>.</summary>
    public TeamColour? NextTurnTeam => CurrentDraft.Next.Team;

    public string DraftSummary => BuildDraftSummary().Replace(" | ", "\n", StringComparison.Ordinal);

    private DraftProgress BuildDraftProgress()
    {
        var slots = AllSlots.ToList();

        return new DraftProgress
        {
            RedProtects = slots.Count(s => s.Availability is SlotAvailability.ProtectedByRed)
                + SkipCount(DraftPhase.Protect, TeamColour.Red),
            BlueProtects = slots.Count(s => s.Availability is SlotAvailability.ProtectedByBlue)
                + SkipCount(DraftPhase.Protect, TeamColour.Blue),
            RedBans = slots.Count(s => s.Availability is SlotAvailability.BannedByRed)
                + SkipCount(DraftPhase.Ban, TeamColour.Red),
            BlueBans = slots.Count(s => s.Availability is SlotAvailability.BannedByBlue)
                + SkipCount(DraftPhase.Ban, TeamColour.Blue),
            Picks = slots.Where(s => s.IsPicked).OrderBy(s => s.PickStamp).Select(s => s.PickedBy).ToList(),
            SkippedPicks = _skips.Select(s => s.Skip).Where(s => s.Phase is DraftPhase.Pick).ToList(),
            MapWinners = _mapWinners.ToList(),
            RedScore = RedScore,
            BlueScore = BlueScore,
            PointsToWin = Pool.PointsToWin,
        };
    }

    private string BuildDraftSummary()
    {
        var slots = AllSlots.ToList();

        return MatchAnnouncer.DraftSummaryLine(
            slots.Where(s => s.HasAvailabilityMark).OrderBy(s => s.AvailabilityStamp).Select(s => (s.Label, s.Availability)),
            slots.Where(s => s.IsPicked).OrderBy(s => s.PickStamp).Select(s => (s.Label, s.PickedBy)),
            Names,
            _skips.Select(s => s.Skip));
    }

    private int SkipCount(DraftPhase phase, TeamColour team) =>
        _skips.Count(s => s.Skip.Phase == phase && s.Skip.Team == team);

    /// <summary>
    /// True while a protect, ban or pick is due to a known team: it can pass on the protect or
    /// ban, or lose the pick as a penalty.
    /// </summary>
    public bool CanSkipTurn => CurrentDraft.Next is
        { Phase: DraftPhase.Protect or DraftPhase.Ban or DraftPhase.Pick, Team: not null };

    public string SkipTurnLabel => CurrentDraft.Next switch
    {
        { Phase: DraftPhase.Protect, Team: { } team } => $"{Names.For(team)} skips protect",
        { Phase: DraftPhase.Ban, Team: { } team } => $"{Names.For(team)} skips ban",
        { Phase: DraftPhase.Pick, Team: { } team } => $"Skip {Names.For(team)}'s pick",
        _ => "Skip",
    };

    public string SkipTurnTip => CurrentDraft.Next.Phase is DraftPhase.Pick
        ? "The team in turn loses this pick, e.g. after running over the pick timer again, and the other team picks instead. Undo gives it back."
        : "The team in turn passes on this protect or ban; the draft moves on.";

    /// <summary>Whether the pool has bans at all, which is when a late team can lose them.</summary>
    public bool HasBans => Pool.Draft.BansOwedPerTeam > 0;

    public bool CanForfeitRedBans => RemainingBans(TeamColour.Red) > 0;

    public bool CanForfeitBlueBans => RemainingBans(TeamColour.Blue) > 0;

    public string ForfeitRedBansLabel => $"{Names.Red} loses bans";

    public string ForfeitBlueBansLabel => $"{Names.Blue} loses bans";

    /// <summary>Bans the team still has to make over the match, second round included.</summary>
    private int RemainingBans(TeamColour team)
    {
        var mark = team is TeamColour.Red ? SlotAvailability.BannedByRed : SlotAvailability.BannedByBlue;
        var used = AllSlots.Count(s => s.Availability == mark) + SkipCount(DraftPhase.Ban, team);
        return Math.Max(0, Pool.Draft.BansOwedPerTeam - used);
    }

    private void RefreshDraft()
    {
        OnPropertyChanged(nameof(NextTurnDisplay));
        OnPropertyChanged(nameof(NextTurnTeam));
        OnPropertyChanged(nameof(DraftSummary));
        OnPropertyChanged(nameof(CanSkipTurn));
        OnPropertyChanged(nameof(SkipTurnLabel));
        OnPropertyChanged(nameof(SkipTurnTip));
        OnPropertyChanged(nameof(HasBans));
        OnPropertyChanged(nameof(CanForfeitRedBans));
        OnPropertyChanged(nameof(CanForfeitBlueBans));
        OnPropertyChanged(nameof(ForfeitRedBansLabel));
        OnPropertyChanged(nameof(ForfeitBlueBansLabel));
    }

    /// <summary>Re-raises every rule after a different pool was loaded.</summary>
    private void RefreshDraftRules()
    {
        OnPropertyChanged(nameof(UseProtects));
        OnPropertyChanged(nameof(ProtectsPerTeam));
        OnPropertyChanged(nameof(ProtectOrderChoice));
        OnPropertyChanged(nameof(BansPerTeam));
        OnPropertyChanged(nameof(BanOrderChoice));
        OnPropertyChanged(nameof(PickOrderChoice));
        OnPropertyChanged(nameof(SecondBanRoundAfterPicks));
        OnPropertyChanged(nameof(SecondRoundBansPerTeam));
        OnPropertyChanged(nameof(SecondRoundOtherTeamFirst));
        OnPropertyChanged(nameof(HasSecondBanRound));
        RefreshDraft();
    }

    partial void OnFirstProtectChanged(TeamColour value) => RefreshDraft();

    partial void OnFirstBanChanged(TeamColour value) => RefreshDraft();

    partial void OnFirstPickChanged(TeamColour value) => RefreshDraft();

    partial void OnRedTeamChanged(string value) => RefreshDraft();

    partial void OnBlueTeamChanged(string value) => RefreshDraft();

    private void SetDraftRule<T>(
        T current,
        T value,
        Action<T> assign,
        string? alsoChanged = null,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        assign(value);
        OnPropertyChanged(name);
        if (alsoChanged is not null)
        {
            OnPropertyChanged(alsoChanged);
        }

        RefreshDraft();
    }

    // ---- marks ---------------------------------------------------------------

    private void OnSlotDraftMarkChanged(object? sender, DraftMarkChangedEventArgs e)
    {
        if (sender is not MappoolSlotViewModel slot)
        {
            return;
        }

        if (e.Added)
        {
            if (e.Kind is DraftMarkKind.Pick)
            {
                slot.PickStamp = ++_markCounter;
            }
            else
            {
                slot.AvailabilityStamp = ++_markCounter;
            }
        }

        RefreshDraft();

        if (_quietMarks)
        {
            return;
        }

        if (!e.Added)
        {
            Status = e.Kind is DraftMarkKind.Pick ? $"Cleared the pick on {slot.Label}." : $"Cleared {slot.Label}.";
            return;
        }

        var (phase, team) = e.Kind is DraftMarkKind.Pick
            ? (slot.PickedBy is null ? DraftPhase.Tiebreaker : DraftPhase.Pick, slot.PickedBy)
            : (slot.IsBanned ? DraftPhase.Ban : DraftPhase.Protect, slot.AvailabilityTeam);

        var action = MatchAnnouncer.DraftActionLine(phase, team, slot.Label, MapNameOf(slot), Names);

        // A pick is followed by the map itself; whose turn comes next is only news once it is played.
        var next = e.Kind is DraftMarkKind.Availability
            ? MatchAnnouncer.NextTurnLine(CurrentDraft.Next, Names)
            : null;

        Status = next is null ? $"{action}." : $"{action}. {next}.";

        var parts = new List<string>(2);
        if (Messages.DraftActions)
        {
            parts.Add(action);
        }

        if (Messages.NextTurn && next is not null)
        {
            parts.Add(next);
        }

        if (parts.Count > 0)
        {
            _ = PostQuietlyAsync([string.Join(" | ", parts)]);
        }
    }

    private static string MapNameOf(MappoolSlotViewModel slot) =>
        slot.Model.Title.Length > 0 ? slot.DisplayName : string.Empty;

    /// <summary>
    /// Handles a click on a map while a protect or ban is due. Returns false when the click should
    /// go on to pick the map as usual.
    /// </summary>
    private bool TryMarkFromClick(MappoolSlotViewModel slot)
    {
        if (IsWarmup || !ClickFollowsDraft)
        {
            return false;
        }

        var next = CurrentDraft.Next;
        if (!next.IsMark || next.Team is not { } team)
        {
            return false;
        }

        var verb = next.Phase is DraftPhase.Ban ? "banned" : "protected";

        if (slot.HasAvailabilityMark)
        {
            Status = $"{slot.Label} is already {slot.AvailabilityLabel}. Pick another map, or right-click to change it.";
            return true;
        }

        if (IsTiebreaker(slot))
        {
            Status = $"The tiebreaker cannot be {verb}. Right-click it if your bracket allows that.";
            return true;
        }

        slot.Availability = (next.Phase, team) switch
        {
            (DraftPhase.Ban, TeamColour.Red) => SlotAvailability.BannedByRed,
            (DraftPhase.Ban, _) => SlotAvailability.BannedByBlue,
            (_, TeamColour.Red) => SlotAvailability.ProtectedByRed,
            _ => SlotAvailability.ProtectedByBlue,
        };

        return true;
    }

    /// <summary>Records who picked a map that was just sent to the lobby.</summary>
    private void RecordPick(MappoolSlotViewModel slot)
    {
        if (IsWarmup || slot.IsPicked)
        {
            return;
        }

        var draft = CurrentDraft;
        if (draft.Next.Phase is DraftPhase.Tiebreaker || IsTiebreaker(slot))
        {
            slot.MarkPicked(null);
        }
        else if (draft.NextPicker is { } picker)
        {
            slot.MarkPicked(picker);
        }
        else
        {
            Status = $"Picked {slot.Label}. Right-click it to note which team picked it.";
        }
    }

    private static bool IsTiebreaker(MappoolSlotViewModel slot) =>
        slot.Category.Equals("TB", StringComparison.OrdinalIgnoreCase)
        || slot.Category.Equals("Tiebreaker", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The team in turn passes on its protect or ban, or loses its pick; the draft moves on as if
    /// it had made it, so a lost pick goes to the other team.
    /// </summary>
    [RelayCommand]
    private void SkipTurn()
    {
        if (!CanSkipTurn || CurrentDraft.Next is not { Team: { } team } next)
        {
            Status = "No protect, ban or pick is due.";
            return;
        }

        var picksMade = AllSlots.Count(s => s.IsPicked && s.PickedBy is not null);
        _skips.Add((new DraftSkip(next.Phase, team, AfterPicks: picksMade), ++_markCounter));
        RefreshDraft();
        AnnounceDraftStep(MatchAnnouncer.SkipLine(next.Phase, team, Names));
    }

    [RelayCommand]
    private void ForfeitRedBans() => ForfeitBans(TeamColour.Red);

    [RelayCommand]
    private void ForfeitBlueBans() => ForfeitBans(TeamColour.Blue);

    /// <summary>
    /// Takes away every ban the team has left, second round included, as brackets do for a late
    /// show. Undo gives them all back in one go.
    /// </summary>
    private void ForfeitBans(TeamColour team)
    {
        var remaining = RemainingBans(team);
        if (remaining == 0)
        {
            Status = $"{Names.For(team)} has no bans left to lose.";
            return;
        }

        var stamp = ++_markCounter;
        for (var i = 0; i < remaining; i++)
        {
            _skips.Add((new DraftSkip(DraftPhase.Ban, team, Forfeited: true), stamp));
        }

        RefreshDraft();
        AnnounceDraftStep(MatchAnnouncer.BansForfeitedLine(team, remaining, Names));
    }

    /// <summary>Reports a skip or forfeit in the status bar and, per the switches, in the lobby.</summary>
    private void AnnounceDraftStep(string action)
    {
        var next = MatchAnnouncer.NextTurnLine(CurrentDraft.Next, Names);
        Status = next is null ? $"{action}." : $"{action}. {next}.";

        var parts = new List<string>(2);
        if (Messages.DraftActions)
        {
            parts.Add(action);
        }

        if (Messages.NextTurn && next is not null)
        {
            parts.Add(next);
        }

        if (parts.Count > 0)
        {
            _ = PostQuietlyAsync([string.Join(" | ", parts)]);
        }
    }

    [RelayCommand]
    private void ClearDraft()
    {
        _quietMarks = true;
        try
        {
            foreach (var slot in AllSlots)
            {
                slot.Availability = SlotAvailability.Available;
                slot.ClearPick();
            }
        }
        finally
        {
            _quietMarks = false;
        }

        _skips.Clear();
        RefreshDraft();
        Status = "Cleared every protect, ban and pick.";
    }

    /// <summary>Takes back the newest protect, ban or pick. Nothing is posted.</summary>
    [RelayCommand]
    private void UndoDraftMark()
    {
        var newestMark = AllSlots
            .Where(s => s.HasAvailabilityMark)
            .MaxBy(s => s.AvailabilityStamp);
        var newestPick = AllSlots
            .Where(s => s.IsPicked)
            .MaxBy(s => s.PickStamp);

        var markStamp = newestMark?.AvailabilityStamp ?? -1;
        var pickStamp = newestPick?.PickStamp ?? -1;
        var skipStamp = _skips.Count > 0 ? _skips[^1].Stamp : -1;

        if (newestMark is null && newestPick is null && skipStamp < 0)
        {
            Status = "Nothing to undo.";
            return;
        }

        if (skipStamp > markStamp && skipStamp > pickStamp)
        {
            var newest = _skips[^1];
            var undone = _skips.RemoveAll(s => s.Stamp == skipStamp);
            RefreshDraft();
            Status = newest.Skip switch
            {
                { Forfeited: true } => $"Gave {Names.For(newest.Skip.Team)} back {undone} forfeited {(undone == 1 ? "ban" : "bans")}.",
                { Phase: DraftPhase.Protect } => $"Took back {Names.For(newest.Skip.Team)}'s skipped protect.",
                { Phase: DraftPhase.Pick } => $"Gave {Names.For(newest.Skip.Team)} back the lost pick.",
                _ => $"Took back {Names.For(newest.Skip.Team)}'s skipped ban.",
            };
            return;
        }

        _quietMarks = true;
        try
        {
            if (markStamp > pickStamp)
            {
                Status = $"Took back {newestMark!.Label} {newestMark.AvailabilityLabel}.";
                newestMark.Availability = SlotAvailability.Available;
            }
            else
            {
                Status = $"Took back the pick of {newestPick!.Label}.";
                newestPick.ClearPick();
            }
        }
        finally
        {
            _quietMarks = false;
        }

        RefreshDraft();
    }

    // ---- lobby messages ------------------------------------------------------

    private MatchStanding CurrentStanding() => new(
        RedScore,
        BlueScore,
        Pool.PointsToWin,
        Names.Red,
        Names.Blue);

    /// <summary>The lines that follow a point, whichever way it was awarded.</summary>
    private void AppendNextTurn(List<string> messages)
    {
        if (Messages.NextTurn && MatchAnnouncer.NextTurnLine(CurrentDraft.Next, Names) is { } next)
        {
            messages.Add(next);
        }
    }

    private async Task AwardManualPointAsync(TeamColour team)
    {
        // The score lives on the lobby, so without one there is nothing to add the point to.
        if (Room is null)
        {
            Status = "No lobby is attached.";
            return;
        }

        if (team is TeamColour.Red)
        {
            RedScore++;
        }
        else
        {
            BlueScore++;
        }

        _mapWinners.Add(team);
        RefreshDraft();
        Status = $"Point to {Names.For(team)}. Match: {RedScore} - {BlueScore}.";

        var messages = new List<string>(2);
        if (Messages.ScoreOnManualPoint)
        {
            messages.Add(MatchAnnouncer.MatchScoreLine(CurrentStanding()));
        }

        AppendNextTurn(messages);
        await PostQuietlyAsync(messages);
    }

    [RelayCommand]
    private Task PostScoreAsync() => RunAsync(
        () => _services.Tournament.SendAsync(MatchAnnouncer.MatchScoreLine(CurrentStanding())),
        "Posted the match score.");

    [RelayCommand]
    private Task PostDraftAsync() => RunAsync(
        async () =>
        {
            await _services.Tournament.SendAsync(BuildDraftSummary());
            if (MatchAnnouncer.NextTurnLine(CurrentDraft.Next, Names) is { } next)
            {
                await _services.Tournament.SendAsync(next);
            }
        },
        "Posted the draft.");

    /// <summary>
    /// Posts automatic lines when a lobby is attached, and says so in the status bar if that
    /// fails rather than throwing into a fire-and-forget caller.
    /// </summary>
    private async Task PostQuietlyAsync(IReadOnlyList<string> messages)
    {
        if (messages.Count == 0 || !_services.Tournament.IsAttached)
        {
            return;
        }

        try
        {
            foreach (var message in messages)
            {
                await _services.Tournament.SendAsync(message);
            }
        }
        catch (Exception ex)
        {
            Status = $"Could not post to the lobby: {ex.Message}";
        }
    }
}
