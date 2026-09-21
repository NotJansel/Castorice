using System.Globalization;
using Castorice.Core.Tournament;

namespace Castorice.Core.Bancho;

/// <summary>
/// Builds the <c>!mp</c> command strings understood by BanchoBot. Kept free of I/O so the exact
/// wire text is easy to assert in tests — a malformed command in a live match is expensive.
/// </summary>
public static class MpCommands
{
    /// <summary>Creates a room. Sent to BanchoBot in a private message, not to a channel.</summary>
    public static string Make(string roomName) => $"!mp make {Sanitise(roomName)}";

    /// <summary>Creates a room that closes itself when everyone leaves.</summary>
    public static string MakePrivate(string roomName) => $"!mp makeprivate {Sanitise(roomName)}";

    public static string Close() => "!mp close";

    public static string Name(string roomName) => $"!mp name {Sanitise(roomName)}";

    public static string Password(string? password) =>
        string.IsNullOrWhiteSpace(password) ? "!mp password" : $"!mp password {Sanitise(password)}";

    public static string Invite(string username) => $"!mp invite {AsIrcName(username)}";

    public static string Kick(string username) => $"!mp kick {AsIrcName(username)}";

    public static string Ban(string username) => $"!mp ban {AsIrcName(username)}";

    public static string Host(string username) => $"!mp host {AsIrcName(username)}";

    public static string ClearHost() => "!mp clearhost";

    public static string AddRef(params IEnumerable<string> usernames) =>
        $"!mp addref {string.Join(' ', usernames.Select(AsIrcName))}";

    public static string RemoveRef(params IEnumerable<string> usernames) =>
        $"!mp removeref {string.Join(' ', usernames.Select(AsIrcName))}";

    public static string Lock() => "!mp lock";

    public static string Unlock() => "!mp unlock";

    public static string Size(int slots) => $"!mp size {Clamp(slots, 1, 16)}";

    public static string Settings() => "!mp settings";

    /// <summary>
    /// <c>!mp set &lt;teammode&gt; [&lt;scoremode&gt;] [&lt;size&gt;]</c>. A size of 0 leaves the slot count alone.
    /// </summary>
    public static string Set(TeamMode teamMode, ScoreMode scoreMode, int size = 0)
    {
        var command = $"!mp set {(int)teamMode} {(int)scoreMode}";
        return size > 0 ? $"{command} {Clamp(size, 1, 16)}" : command;
    }

    /// <summary><c>!mp map &lt;beatmapid&gt; [&lt;playmode&gt;]</c> — the beatmap id, not the beatmapset id.</summary>
    public static string Map(long beatmapId, PlayMode playMode = PlayMode.Osu)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beatmapId);
        return $"!mp map {beatmapId.ToString(CultureInfo.InvariantCulture)} {(int)playMode}";
    }

    /// <summary><c>!mp mods &lt;mods&gt;</c>, or <c>!mp mods None</c> to clear them.</summary>
    public static string SetMods(Mods mods) => $"!mp mods {mods.ToAcronyms()}";

    /// <summary>Starts the match, optionally after a countdown.</summary>
    public static string Start(int seconds = 0) =>
        seconds > 0 ? $"!mp start {Clamp(seconds, 1, 300)}" : "!mp start";

    public static string AbortStartTimer() => "!mp aborttimer";

    public static string Abort() => "!mp abort";

    /// <summary>A plain countdown that does not start the match.</summary>
    public static string Timer(int seconds) => $"!mp timer {Clamp(seconds, 1, 3600)}";

    public static string Team(string username, TeamColour colour) =>
        $"!mp team {AsIrcName(username)} {colour.ToString().ToLowerInvariant()}";

    /// <summary><c>!mp move &lt;user&gt; &lt;slot&gt;</c>, slots being 1-based.</summary>
    public static string Move(string username, int slot) =>
        $"!mp move {AsIrcName(username)} {Clamp(slot, 1, 16)}";

    /// <summary>Everything needed to bring a fresh room in line with the pool's configuration.</summary>
    public static IEnumerable<string> ConfigureRoom(Mappool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        yield return Set(pool.TeamMode, pool.ScoreMode, pool.RoomSize);

        foreach (var referee in pool.Referees.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            yield return AddRef(referee);
        }
    }

    /// <summary>The two commands that put a pick on the board, in the order BanchoBot expects.</summary>
    public static IEnumerable<string> PickSlot(MappoolSlot slot, PlayMode playMode)
    {
        ArgumentNullException.ThrowIfNull(slot);

        yield return Map(slot.BeatmapId, playMode);
        yield return SetMods(slot.Mods);
    }

    /// <summary>
    /// osu! usernames carry spaces; IRC names use underscores. Bancho accepts either in <c>!mp</c>
    /// arguments, but only the underscore form survives a space-delimited command.
    /// </summary>
    public static string AsIrcName(string username) => Sanitise(username).Replace(' ', '_');

    private static string Sanitise(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
