namespace Castorice.Core.Chat;

/// <summary>
/// Which channels to be in once a connection is (re-)established. A dropped connection leaves
/// every channel on the server's side while the tabs stay open here, so without rejoining, text
/// typed into them — and every referee button aimed at the lobby — goes nowhere.
/// </summary>
public static class ReconnectPlan
{
    /// <summary>
    /// The configured auto-joins, every channel tab still open, and the attached lobby even if its
    /// tab was closed. Each appears once, in that order; private conversations need no join.
    /// </summary>
    public static IReadOnlyList<string> ChannelsToJoin(
        IEnumerable<string> autoJoinChannels,
        IEnumerable<ChatTarget> openTargets,
        string? attachedLobbyChannel)
    {
        ArgumentNullException.ThrowIfNull(autoJoinChannels);
        ArgumentNullException.ThrowIfNull(openTargets);

        var open = openTargets
            .Where(t => t.Kind is ChatTargetKind.Channel or ChatTargetKind.MultiplayerRoom)
            .Select(t => t.Name);

        var lobby = string.IsNullOrWhiteSpace(attachedLobbyChannel) ? [] : new[] { attachedLobbyChannel };

        return autoJoinChannels
            .Concat(open)
            .Concat(lobby)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.StartsWith('#') ? name : '#' + name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
