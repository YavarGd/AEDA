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
