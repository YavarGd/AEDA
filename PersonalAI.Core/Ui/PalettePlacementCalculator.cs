namespace PersonalAI.Core.Ui;

public static class PalettePlacementCalculator
{
    public static PointPosition CenterInBounds(
        RectBounds bounds,
        double width,
        double height)
    {
        var left = bounds.Left + Math.Max(0, (bounds.Width - width) / 2);
        var top = bounds.Top + Math.Max(0, (bounds.Height - height) / 2);

        return new PointPosition(left, top);
    }

    public static PointPosition NearCursor(
        RectBounds bounds,
        PointPosition cursor,
        double width,
        double height)
    {
        var left = cursor.X - (width / 2);
        var top = cursor.Y - 80;

        return Clamp(bounds, new PointPosition(left, top), width, height);
    }

    public static PointPosition BottomRightInBounds(
        RectBounds bounds,
        double width,
        double height,
        double margin)
    {
        var left = bounds.Left + bounds.Width - width - margin;
        var top = bounds.Top + bounds.Height - height - margin;

        return Clamp(bounds, new PointPosition(left, top), width, height);
    }

    private static PointPosition Clamp(
        RectBounds bounds,
        PointPosition position,
        double width,
        double height)
    {
        var maxLeft = bounds.Left + Math.Max(0, bounds.Width - width);
        var maxTop = bounds.Top + Math.Max(0, bounds.Height - height);
        var left = Math.Clamp(position.X, bounds.Left, maxLeft);
        var top = Math.Clamp(position.Y, bounds.Top, maxTop);

        return new PointPosition(left, top);
    }
}

public sealed record AssistResponseLayout(
    int Width,
    int Height,
    bool RequiresScrolling);

public static class AssistResponseSizingPolicy
{
    public static int ScalePixels(int logicalPixels, double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale)
            ? Math.Clamp(rasterizationScale, 0.5, 4)
            : 1;
        return Math.Max(1, (int)Math.Round(logicalPixels * scale));
    }

    public static AssistResponseLayout CalculateMeasured(
        double desiredHeight,
        RectBounds workingArea,
        double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale)
            ? Math.Clamp(rasterizationScale, 0.5, 4)
            : 1;
        var width = Math.Max(1, Math.Min(
            (int)Math.Round(560 * scale),
            (int)Math.Round(workingArea.Width - (40 * scale))));
        var minimumHeight = (int)Math.Round(180 * scale);
        var maximumHeight = Math.Max(1, Math.Min(
            (int)Math.Round(480 * scale),
            (int)Math.Round(workingArea.Height - (40 * scale))));
        var requestedHeight = double.IsFinite(desiredHeight)
            ? (int)Math.Ceiling(Math.Max(0, desiredHeight) * scale)
            : minimumHeight;

        return new AssistResponseLayout(
            width,
            Math.Clamp(requestedHeight, Math.Min(minimumHeight, maximumHeight), maximumHeight),
            requestedHeight > maximumHeight);
    }

    public static AssistResponseLayout Calculate(
        int visibleCharacters,
        RectBounds workingArea,
        double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale)
            ? Math.Clamp(rasterizationScale, 0.5, 4)
            : 1;
        var width = Math.Max(
            1,
            Math.Min(
                (int)Math.Round(560 * scale),
                (int)Math.Round(workingArea.Width - (40 * scale))));
        var minimumHeight = (int)Math.Round(180 * scale);
        var uncappedHeight = minimumHeight +
            (int)Math.Ceiling(Math.Max(0, visibleCharacters) / 72d) *
            (int)Math.Round(18 * scale);
        var maximumHeight = Math.Max(
            1,
            Math.Min(
                (int)Math.Round(480 * scale),
                (int)Math.Round(workingArea.Height - (40 * scale))));
        var height = Math.Clamp(
            uncappedHeight,
            Math.Min(minimumHeight, maximumHeight),
            maximumHeight);

        return new AssistResponseLayout(
            width,
            height,
            uncappedHeight > maximumHeight);
    }
}

public static class AssistScrollFollowPolicy
{
    public const double NearBottomThreshold = 32;

    public static bool IsNearBottom(double scrollableHeight, double verticalOffset) =>
        double.IsFinite(scrollableHeight) &&
        double.IsFinite(verticalOffset) &&
        scrollableHeight - verticalOffset <= NearBottomThreshold;
}

public enum AssistInvocationKind
{
    Pointer,
    Keyboard
}

public sealed record AssistEntranceMotion(bool IsSpatial, double InitialScale, int DurationMilliseconds);

public static class AssistMotionPolicy
{
    public const int ResponseEntranceMilliseconds = 160;
    public const int SpotlightEntranceMilliseconds = 140;
    public const int FirstContentFadeMilliseconds = 120;

    public static AssistEntranceMotion Entrance(
        AssistInvocationKind invocationKind,
        bool spotlight,
        bool animationsEnabled) =>
        invocationKind == AssistInvocationKind.Pointer && animationsEnabled
            ? new(true, spotlight ? 0.98 : 0.97,
                spotlight ? SpotlightEntranceMilliseconds : ResponseEntranceMilliseconds)
            : new(false, 1, 0);
}

public sealed class AssistHeightInterpolator
{
    private int _startHeight;
    private TimeSpan _startTime;
    private TimeSpan _duration;

    public int CurrentHeight { get; private set; }
    public int TargetHeight { get; private set; }
    public long InvocationId { get; private set; }
    public bool IsActive { get; private set; }

    public int Retarget(
        int currentHeight,
        int requestedHeight,
        int maximumHeight,
        bool streaming,
        bool animationsEnabled,
        long invocationId,
        TimeSpan now)
    {
        var maximum = Math.Max(1, maximumHeight);
        CurrentHeight = Math.Clamp(currentHeight, 1, maximum);
        TargetHeight = Math.Clamp(requestedHeight, 1, maximum);
        if (streaming)
        {
            TargetHeight = Math.Max(CurrentHeight, TargetHeight);
        }

        InvocationId = invocationId;
        _startHeight = CurrentHeight;
        _startTime = now;
        _duration = TimeSpan.FromMilliseconds(Math.Abs(TargetHeight - CurrentHeight) <= 48 ? 120 : 180);
        IsActive = animationsEnabled && CurrentHeight != TargetHeight;
        if (!IsActive)
        {
            CurrentHeight = TargetHeight;
        }

        return CurrentHeight;
    }

    public bool TrySample(TimeSpan now, long invocationId, out int height)
    {
        height = CurrentHeight;
        if (!IsActive || invocationId != InvocationId)
        {
            return false;
        }

        var progress = Math.Clamp((now - _startTime).TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        height = (int)Math.Round(_startHeight + ((TargetHeight - _startHeight) * eased));
        height = Math.Clamp(height, Math.Min(_startHeight, TargetHeight), Math.Max(_startHeight, TargetHeight));
        CurrentHeight = height;
        if (progress >= 1)
        {
            IsActive = false;
            CurrentHeight = TargetHeight;
            height = TargetHeight;
        }

        return true;
    }

    public void Cancel() => IsActive = false;
}
