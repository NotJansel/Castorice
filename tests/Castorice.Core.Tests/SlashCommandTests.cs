using Castorice.Core.Chat;

namespace Castorice.Core.Tests;

public class SlashCommandTests
{
    [Fact]
    public void Plain_text_is_not_a_command()
    {
        Assert.Equal(SlashCommandKind.None, SlashCommand.Parse("hello").Kind);
    }

    [Fact]
    public void A_doubled_slash_escapes_into_literal_text()
    {
        Assert.Equal(SlashCommandKind.None, SlashCommand.Parse("//mp start").Kind);
        Assert.Equal("/mp start", SlashCommand.Unescape("//mp start"));
    }

    [Fact]
    public void Parses_join()
    {
        var command = SlashCommand.Parse("/join #osu");

        Assert.Equal(SlashCommandKind.Join, command.Kind);
        Assert.Equal("#osu", command.Argument);
    }

    [Fact]
    public void Parses_a_private_message_with_a_body()
    {
        var command = SlashCommand.Parse("/msg BanchoBot !mp make Cup: (A) vs (B)");

        Assert.Equal(SlashCommandKind.PrivateMessage, command.Kind);
        Assert.Equal("BanchoBot", command.Argument);
        Assert.Equal("!mp make Cup: (A) vs (B)", command.Remainder);
    }

    [Fact]
    public void Me_keeps_the_whole_rest_of_the_line()
    {
        var command = SlashCommand.Parse("/me waves at everyone");

        Assert.Equal(SlashCommandKind.Action, command.Kind);
        Assert.Equal("waves at everyone", command.Argument);
    }

    [Fact]
    public void Unknown_verbs_are_reported_rather_than_sent()
    {
        var command = SlashCommand.Parse("/frobnicate x");

        Assert.Equal(SlashCommandKind.Unknown, command.Kind);
        Assert.Equal("frobnicate", command.Argument);
    }

    [Theory]
    [InlineData("/j", SlashCommandKind.Join)]
    [InlineData("/part", SlashCommandKind.Part)]
    [InlineData("/query", SlashCommandKind.PrivateMessage)]
    [InlineData("/quit", SlashCommandKind.Quit)]
    [InlineData("/raw", SlashCommandKind.Raw)]
    public void Supports_the_usual_aliases(string input, SlashCommandKind expected)
    {
        Assert.Equal(expected, SlashCommand.Parse(input).Kind);
    }
}
