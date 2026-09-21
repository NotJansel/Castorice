using Castorice.Core.Irc;

namespace Castorice.Core.Tests;

public class IrcMessageTests
{
    [Fact]
    public void Parses_a_channel_message()
    {
        var message = IrcMessage.Parse(":Peppy!cho@ppy.sh PRIVMSG #osu :hello there");

        Assert.NotNull(message);
        Assert.Equal("Peppy", message.Nick);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("#osu", message.ParameterAt(0));
        Assert.Equal("hello there", message.Trailing);
    }

    [Fact]
    public void Keeps_colons_inside_the_trailing_parameter()
    {
        var message = IrcMessage.Parse(":BanchoBot!cho@ppy.sh PRIVMSG #mp_1 :Room name: Test, History: https://osu.ppy.sh/mp/1");

        Assert.NotNull(message);
        Assert.Equal("Room name: Test, History: https://osu.ppy.sh/mp/1", message.Trailing);
    }

    [Fact]
    public void Parses_a_message_without_a_prefix()
    {
        var message = IrcMessage.Parse("PING :cho.ppy.sh");

        Assert.NotNull(message);
        Assert.Equal(string.Empty, message.Prefix);
        Assert.Equal("PING", message.Command);
        Assert.Equal("cho.ppy.sh", message.Trailing);
    }

    [Fact]
    public void Upper_cases_the_command()
    {
        Assert.Equal("PRIVMSG", IrcMessage.Parse(":a!b@c privmsg #x :y")!.Command);
    }

    [Fact]
    public void Parses_numerics_with_several_middle_parameters()
    {
        var message = IrcMessage.Parse(":cho.ppy.sh 353 me = #osu :alice bob @carol");

        Assert.NotNull(message);
        Assert.Equal("353", message.Command);
        Assert.Equal(["me", "=", "#osu", "alice bob @carol"], message.Parameters);
    }

    [Fact]
    public void Takes_the_nick_from_a_prefix_without_a_user_part()
    {
        Assert.Equal("cho.ppy.sh", IrcMessage.Parse(":cho.ppy.sh 001 me :Welcome")!.Nick);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_blank_lines(string? line) => Assert.Null(IrcMessage.Parse(line));

    [Fact]
    public void Strips_the_trailing_newline()
    {
        Assert.Equal("PING :x", IrcMessage.Parse("PING :x\r\n")!.Raw);
    }
}
