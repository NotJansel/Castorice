using Castorice.Core.Chat;

namespace Castorice.Core.Tests;

public class ScrollFollowerTests
{
    // A view 500 tall. "Content grew" events keep the offset; "scrolled" events keep the extent.
    private static ScrollChange Grew(double offset, double extent, double by) =>
        new(OffsetDelta: 0, ExtentDelta: by, ViewportDelta: 0, offset, extent, Viewport: 500);

    private static ScrollChange Scrolled(double offset, double extent, double by) =>
        new(OffsetDelta: by, ExtentDelta: 0, ViewportDelta: 0, offset, extent, Viewport: 500);

    [Fact]
    public void Follows_new_messages_by_default()
    {
        var follower = new ScrollFollower();

        Assert.True(follower.OnScrollChanged(Grew(offset: 500, extent: 1040, by: 40)));
        Assert.False(follower.IsPaused);
    }

    [Fact]
    public void Scrolling_up_pauses_following()
    {
        var follower = new ScrollFollower();

        follower.OnScrollChanged(Scrolled(offset: 200, extent: 1000, by: -300));

        Assert.True(follower.IsPaused);
        Assert.False(follower.OnScrollChanged(Grew(offset: 200, extent: 1040, by: 40)));
    }

    [Fact]
    public void Scrolling_back_to_the_bottom_resumes_following()
    {
        var follower = new ScrollFollower();
        follower.OnScrollChanged(Scrolled(offset: 200, extent: 1000, by: -300));

        follower.OnScrollChanged(Scrolled(offset: 500, extent: 1000, by: 300));

        Assert.False(follower.IsPaused);
        Assert.True(follower.OnScrollChanged(Grew(offset: 500, extent: 1040, by: 40)));
    }

    [Fact]
    public void Landing_near_the_bottom_counts_as_the_bottom()
    {
        var follower = new ScrollFollower();
        follower.OnScrollChanged(Scrolled(offset: 200, extent: 1000, by: -300));

        // 10 px short of the end, well inside the tolerance.
        follower.OnScrollChanged(Scrolled(offset: 490, extent: 1000, by: 290));

        Assert.False(follower.IsPaused);
    }

    [Fact]
    public void A_new_message_does_not_pause_it_even_though_the_view_is_no_longer_at_the_end()
    {
        // The trap: after content grows, offset 500 of extent 1040 is not the bottom any more.
        // Reading that as "the user scrolled up" would switch following off on every message.
        var follower = new ScrollFollower();

        follower.OnScrollChanged(Grew(offset: 500, extent: 1040, by: 40));

        Assert.False(follower.IsPaused);
    }

    [Fact]
    public void Its_own_jump_to_the_end_leaves_following_on()
    {
        var follower = new ScrollFollower();

        follower.OnScrollChanged(Grew(offset: 500, extent: 1040, by: 40));
        follower.OnScrollChanged(Scrolled(offset: 540, extent: 1040, by: 40));

        Assert.False(follower.IsPaused);
    }

    [Fact]
    public void Resizing_the_window_keeps_the_newest_message_in_view_while_following()
    {
        var follower = new ScrollFollower();

        var resized = new ScrollChange(0, 0, ViewportDelta: -200, Offset: 500, Extent: 1000, Viewport: 300);

        Assert.True(follower.OnScrollChanged(resized));
    }

    [Fact]
    public void Resizing_while_paused_leaves_the_reader_where_they_are()
    {
        var follower = new ScrollFollower();
        follower.OnScrollChanged(Scrolled(offset: 100, extent: 1000, by: -400));

        var resized = new ScrollChange(0, 0, ViewportDelta: -200, Offset: 100, Extent: 1000, Viewport: 300);

        Assert.False(follower.OnScrollChanged(resized));
        Assert.True(follower.IsPaused);
    }

    [Fact]
    public void Resume_turns_following_back_on()
    {
        var follower = new ScrollFollower();
        follower.OnScrollChanged(Scrolled(offset: 100, extent: 1000, by: -400));

        follower.Resume();

        Assert.False(follower.IsPaused);
        Assert.True(follower.OnScrollChanged(Grew(offset: 100, extent: 1040, by: 40)));
    }

    [Fact]
    public void A_short_backlog_that_does_not_fill_the_view_is_at_the_bottom()
    {
        Assert.True(ScrollFollower.IsAtBottom(new ScrollChange(0, 0, 0, Offset: 0, Extent: 300, Viewport: 500)));
    }
}
