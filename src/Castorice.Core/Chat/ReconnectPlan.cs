namespace Castorice.Core.Chat;

/// <summary>
/// Which channels to be in once a connection is (re-)established. A dropped connection leaves
/// every channel on the server's side while the tabs stay open here, so without rejoining, text
/// typed into them — and every referee button aimed at the lobby — goes nowhere.
/// </summary>
public static class ReconnectPlan
{
    /// <summary>
    /// The configured auto-joins, every channel tab still open, and every attached lobby even if its
    /// chat tab was closed. Each appears once, in that order; private conversations need no join.
    /// </summary>
    public static IReadOnlyList<string> ChannelsToJoin(
        IEnumerable<string> autoJoinChannels,
        IEnumerable<ChatTarget> openTargets,
        IEnumerable<string?> attachedLobbyChannels)
    {
        ArgumentNullException.ThrowIfNull(autoJoinChannels);
        ArgumentNullException.ThrowIfNull(openTargets);
        ArgumentNullException.ThrowIfNull(attachedLobbyChannels);

        var open = openTargets
            .Where(t => t.Kind is ChatTargetKind.Channel or ChatTargetKind.MultiplayerRoom)
            .Select(t => t.Name);

        return autoJoinChannels
            .Concat(open)
            .Concat(attachedLobbyChannels.OfType<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.StartsWith('#') ? name : '#' + name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
