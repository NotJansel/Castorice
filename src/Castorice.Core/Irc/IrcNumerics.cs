namespace Castorice.Core.Irc;

/// <summary>The handful of numerics Bancho actually sends.</summary>
public static class IrcNumerics
{
    public const string Welcome = "001";
    public const string MotdStart = "375";
    public const string Motd = "372";
    public const string MotdEnd = "376";
    public const string NamesReply = "353";
    public const string EndOfNames = "366";
    public const string Topic = "332";
    public const string NoSuchNick = "401";
    public const string NoNicknameGiven = "431";
    public const string ErroneousNickname = "432";
    public const string NicknameInUse = "433";
    public const string PasswordMismatch = "464";
}
