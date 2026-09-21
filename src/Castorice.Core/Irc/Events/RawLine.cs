namespace Castorice.Core.Irc.Events;

/// <summary>One line of wire traffic, for the raw IRC console.</summary>
public sealed record RawLine(string Line, bool Outbound)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
