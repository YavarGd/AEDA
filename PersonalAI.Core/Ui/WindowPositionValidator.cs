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

    public static WindowPosition? ClampToVisibleWorkingArea(
        WindowPosition position,
        double width,
        double height,
        IEnumerable<RectBounds> workingAreas)
    {
        var windowBounds = new RectBounds(position.Left, position.Top, width, height);
        var area = workingAreas
            .Select(candidate => new
            {
                Bounds = candidate,
                Overlap = IntersectionArea(candidate, windowBounds)
            })
            .Where(candidate => candidate.Overlap > 0)
            .OrderByDescending(candidate => candidate.Overlap)
            .Select(candidate => (RectBounds?)candidate.Bounds)
            .FirstOrDefault();

        if (area is null)
        {
            return null;
        }

        var maxLeft = area.Value.Left + Math.Max(0, area.Value.Width - width);
        var maxTop = area.Value.Top + Math.Max(0, area.Value.Height - height);
        return new WindowPosition(
            Math.Clamp(position.Left, area.Value.Left, maxLeft),
            Math.Clamp(position.Top, area.Value.Top, maxTop));
    }

    private static bool Intersects(RectBounds first, RectBounds second)
    {
        return first.Left < second.Left + second.Width &&
            first.Left + first.Width > second.Left &&
            first.Top < second.Top + second.Height &&
            first.Top + first.Height > second.Top;
    }

    private static double IntersectionArea(RectBounds first, RectBounds second)
    {
        var width = Math.Min(first.Left + first.Width, second.Left + second.Width) -
            Math.Max(first.Left, second.Left);
        var height = Math.Min(first.Top + first.Height, second.Top + second.Height) -
            Math.Max(first.Top, second.Top);
        return Math.Max(0, width) * Math.Max(0, height);
    }
}
