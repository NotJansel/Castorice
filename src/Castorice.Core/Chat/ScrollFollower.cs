namespace Castorice.Core.Chat;

/// <summary>What a scroll view reported after one change, in its own units.</summary>
public readonly record struct ScrollChange(
    double OffsetDelta,
    double ExtentDelta,
    double ViewportDelta,
    double Offset,
    double Extent,
    double Viewport);

/// <summary>
/// Decides whether a chat view should follow new messages. It follows by default; scrolling up
/// pauses it, and scrolling back to the bottom resumes it. Kept free of any UI type so the rules
/// can be tested on their own.
/// </summary>
public sealed class ScrollFollower
{
    /// <summary>
    /// How close to the end still counts as "at the bottom". Without slack, sub-pixel layout
    /// rounding would read as the user having scrolled away.
    /// </summary>
    public const double BottomTolerance = 24;

    /// <summary>True while the user has scrolled up to read, i.e. new messages do not move the view.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Feeds one scroll event in. Returns true when the view should now be scrolled to the end.
    /// </summary>
    public bool OnScrollChanged(ScrollChange change)
    {
        // Content grew or shrank, or the view was resized. That is never the user moving, so it
        // must not change whether we are paused — otherwise a new message arriving while the view
        // sits at the old bottom would read as "scrolled up" and switch following off.
        if (change.ExtentDelta != 0 || change.ViewportDelta != 0)
        {
            return !IsPaused;
        }

        // Only the offset moved: the user scrolled, or we did. Either way, where it ended up is the
        // answer — our own jump to the end lands at the bottom and so leaves following on.
        if (change.OffsetDelta != 0)
        {
            IsPaused = !IsAtBottom(change);
        }

        return false;
    }

    /// <summary>Resumes following, e.g. on "jump to latest", on sending, or on switching conversation.</summary>
    public void Resume() => IsPaused = false;

    public static bool IsAtBottom(ScrollChange change) =>
        change.Offset >= change.Extent - change.Viewport - BottomTolerance;
}
