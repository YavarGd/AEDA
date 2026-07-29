namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Decides whether the message timeline should keep following newly streamed
/// content. Following stops as soon as the reader scrolls away from the end so
/// their position is never stolen.
/// </summary>
public static class ChatScrollFollowPolicy
{
    public const double DefaultTolerance = 24;

    public static bool IsAtEnd(
        double offsetY,
        double extentHeight,
        double viewportHeight,
        double tolerance = DefaultTolerance)
    {
        if (extentHeight <= viewportHeight)
        {
            return true;
        }

        return extentHeight - (offsetY + viewportHeight) <= tolerance;
    }

    /// <summary>
    /// Resolves follow mode after a scroll change. Any offset movement re-evaluates from
    /// the real position, including when content grew in the same change, so streaming
    /// output cannot pull the view back while the reader is scrolling away.
    /// </summary>
    public static bool ShouldFollowAfterScrollChange(
        bool wasFollowing,
        bool offsetMoved,
        bool contentGrew,
        double offsetY,
        double extentHeight,
        double viewportHeight,
        double tolerance = DefaultTolerance)
    {
        if (offsetMoved || !contentGrew)
        {
            return IsAtEnd(offsetY, extentHeight, viewportHeight, tolerance);
        }

        return wasFollowing;
    }
}
