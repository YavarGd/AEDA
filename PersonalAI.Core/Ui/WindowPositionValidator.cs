namespace PersonalAI.Core.Ui;

public static class WindowPositionValidator
{
    public static bool IsVisibleWithinAnyWorkingArea(
        WindowPosition position,
        double width,
        double height,
        IEnumerable<RectBounds> workingAreas)
    {
        var windowBounds = new RectBounds(position.Left, position.Top, width, height);

        return workingAreas.Any(area => Intersects(area, windowBounds));
    }

    private static bool Intersects(RectBounds first, RectBounds second)
    {
        return first.Left < second.Left + second.Width &&
            first.Left + first.Width > second.Left &&
            first.Top < second.Top + second.Height &&
            first.Top + first.Height > second.Top;
    }
}
