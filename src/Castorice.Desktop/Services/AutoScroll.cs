using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Castorice.Core.Chat;

namespace Castorice.Desktop.Services;

/// <summary>
/// Keeps a <see cref="ScrollViewer"/> pinned to its newest content, via
/// <c>services:AutoScroll.IsEnabled="True"</c>. Scrolling up to read pauses it; scrolling back to
/// the bottom resumes it. <see cref="IsPausedProperty"/> is bindable, for a "jump to latest" button.
/// The rules themselves live in <see cref="ScrollFollower"/>.
/// </summary>
public static class AutoScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(AutoScroll));

    /// <summary>True while the user has scrolled up, i.e. new content is arriving out of sight.</summary>
    public static readonly AttachedProperty<bool> IsPausedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsPaused", typeof(AutoScroll));

    private static readonly AttachedProperty<ScrollFollower?> FollowerProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ScrollFollower?>("Follower", typeof(AutoScroll));

    static AutoScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

    public static bool GetIsPaused(ScrollViewer viewer) => viewer.GetValue(IsPausedProperty);

    /// <summary>Resumes following and jumps to the newest content.</summary>
    public static void ScrollToLatest(ScrollViewer viewer)
    {
        viewer.GetValue(FollowerProperty)?.Resume();
        viewer.SetValue(IsPausedProperty, false);
        ScrollToEndSoon(viewer);
    }

    private static void OnIsEnabledChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            viewer.SetValue(FollowerProperty, new ScrollFollower());
            viewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            viewer.ScrollChanged -= OnScrollChanged;
            viewer.SetValue(FollowerProperty, null);
            viewer.SetValue(IsPausedProperty, false);
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.GetValue(FollowerProperty) is not { } follower)
        {
            return;
        }

        var shouldFollow = follower.OnScrollChanged(new ScrollChange(
            e.OffsetDelta.Y,
            e.ExtentDelta.Y,
            e.ViewportDelta.Y,
            viewer.Offset.Y,
            viewer.Extent.Height,
            viewer.Viewport.Height));

        viewer.SetValue(IsPausedProperty, follower.IsPaused);

        if (shouldFollow)
        {
            ScrollToEndSoon(viewer);
        }
    }

    /// <summary>
    /// Deferred so the new item is measured first; scrolling inside the change notification would
    /// aim at the old extent and stop one message short.
    /// </summary>
    private static void ScrollToEndSoon(ScrollViewer viewer) =>
        Dispatcher.UIThread.Post(viewer.ScrollToEnd, DispatcherPriority.Background);
}
