using Castorice.Core.Irc;
using Microsoft.Extensions.Time.Testing;

namespace Castorice.Core.Tests;

public class OutboundRateLimiterTests
{
    [Fact]
    public async Task Lets_the_first_burst_through_immediately()
    {
        var time = new FakeTimeProvider();
        var limiter = new OutboundRateLimiter(3, TimeSpan.FromSeconds(5), time);

        for (var i = 0; i < 3; i++)
        {
            await limiter.WaitForSlotAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Blocks_once_the_window_is_full()
    {
        var time = new FakeTimeProvider();
        var limiter = new OutboundRateLimiter(2, TimeSpan.FromSeconds(5), time);

        await limiter.WaitForSlotAsync();
        await limiter.WaitForSlotAsync();

        var third = limiter.WaitForSlotAsync();
        Assert.False(third.IsCompleted);

        // Still inside the window.
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(third.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(2));
        await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Slides_the_window_rather_than_resetting_it()
    {
        var time = new FakeTimeProvider();
        var limiter = new OutboundRateLimiter(2, TimeSpan.FromSeconds(10), time);

        await limiter.WaitForSlotAsync();
        time.Advance(TimeSpan.FromSeconds(6));
        await limiter.WaitForSlotAsync();

        var third = limiter.WaitForSlotAsync();
        Assert.False(third.IsCompleted);

        // The first slot expires at t=10, which is 4 seconds after the second send.
        time.Advance(TimeSpan.FromSeconds(5));
        await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Uses_the_documented_osu_quotas()
    {
        var player = OutboundRateLimiter.ForPlayerAccount();
        Assert.Equal(10, player.MaxMessages);
        Assert.Equal(TimeSpan.FromSeconds(5), player.Window);

        var bot = OutboundRateLimiter.ForBotAccount();
        Assert.Equal(300, bot.MaxMessages);
        Assert.Equal(TimeSpan.FromSeconds(60), bot.Window);
    }

    [Fact]
    public void Rejects_a_nonsensical_configuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundRateLimiter(0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundRateLimiter(1, TimeSpan.Zero));
    }
}
