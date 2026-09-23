using Castorice.Core.Chat;

namespace Castorice.Core.Tests;

public class ReconnectPlanTests
{
    private static ChatTarget T(string name) => new(name, ChatTarget.KindFor(name));

    [Fact]
    public void A_first_connect_joins_the_configured_channels()
    {
        Assert.Equal(["#osu"], ReconnectPlan.ChannelsToJoin(["#osu"], [], null));
    }

    [Fact]
    public void A_reconnect_rejoins_the_attached_lobby()
    {
        var joined = ReconnectPlan.ChannelsToJoin(["#osu"], [T("#osu"), T("#mp_12345")], "#mp_12345");

        Assert.Equal(["#osu", "#mp_12345"], joined);
    }

    [Fact]
    public void Rejoins_the_lobby_even_when_its_tab_was_closed()
    {
        // The referee buttons still aim at the attached lobby, tab or no tab.
        Assert.Contains("#mp_777", ReconnectPlan.ChannelsToJoin([], [], "#mp_777"));
    }

    [Fact]
    public void Rejoins_channels_joined_by_hand_not_just_the_configured_ones()
    {
        var joined = ReconnectPlan.ChannelsToJoin(["#osu"], [T("#osu"), T("#german")], null);

        Assert.Equal(["#osu", "#german"], joined);
    }

    [Fact]
    public void Private_conversations_and_the_server_log_are_not_joined()
    {
        var joined = ReconnectPlan.ChannelsToJoin(
            [],
            [T("BanchoBot"), T("SomePlayer"), new ChatTarget("Server", ChatTargetKind.Server)],
            null);

        Assert.Empty(joined);
    }

    [Fact]
    public void Joins_each_channel_once_whatever_its_case_or_source()
    {
        var joined = ReconnectPlan.ChannelsToJoin(["#osu", "osu"], [T("#OSU"), T("#mp_1")], "#mp_1");

        Assert.Equal(["#osu", "#mp_1"], joined);
    }
}
