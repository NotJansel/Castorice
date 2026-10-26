using Castorice.Core.Bancho;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class TournamentControllerTests
{
    [Theory]
    [InlineData("TP: (Germany) vs (Poland)", "TP: (Germany) vs (Poland)", true)]
    [InlineData("tp: (germany) vs (poland)", "TP: (Germany) vs (Poland)", true)]
    [InlineData("TP: (A very long team name) vs (Ano", "TP: (A very long team name) vs (Another very long one)", true)]
    [InlineData("TP: (France) vs (Spain)", "TP: (Germany) vs (Poland)", false)]
    [InlineData("", "TP: (Germany) vs (Poland)", false)]
    public void Takes_only_the_reply_naming_its_own_lobby(string replied, string asked, bool expected)
    {
        // Two tabs making a lobby at the same moment must not both attach to the first reply.
        Assert.Equal(expected, TournamentController.IsReplyFor(new MatchCreated(1, replied), asked));
    }
}
