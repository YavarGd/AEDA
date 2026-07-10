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
