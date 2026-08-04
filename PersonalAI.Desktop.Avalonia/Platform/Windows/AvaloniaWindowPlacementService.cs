using Avalonia;
using Avalonia.Controls;
using PersonalAI.Core.Ui;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public sealed class AvaloniaWindowPlacementService
{
    public void Place(Window window, WindowPosition requested)
    {
        ArgumentNullException.ThrowIfNull(window);
        var scale = Math.Max(0.5, window.RenderScaling);
        var width = Pixels(window.ClientSize.Width, window.Width, scale);
        var height = Pixels(window.ClientSize.Height, window.Height, scale);
        var screens = window.Screens.All;
        var workingAreas = screens.Select(screen => ToBounds(screen.WorkingArea)).ToArray();
        var fallback = window.Screens.Primary is { } primary
            ? ToBounds(primary.WorkingArea)
            : workingAreas.FirstOrDefault(new RectBounds(0, 0, 1280, 720));
        var position = ResolvePosition(requested, width, height, workingAreas, fallback);
        window.Position = new PixelPoint(
            (int)Math.Round(position.Left),
            (int)Math.Round(position.Top));
    }

    public static WindowPosition ResolvePosition(
        WindowPosition requested,
        double width,
        double height,
        IReadOnlyList<RectBounds> workingAreas,
        RectBounds fallback)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        return WindowPositionValidator.ClampToVisibleWorkingArea(
                requested,
                width,
                height,
                workingAreas)
            ?? ToWindowPosition(PalettePlacementCalculator.CenterInBounds(
                fallback,
                width,
                height));
    }

    private static RectBounds ToBounds(PixelRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static WindowPosition ToWindowPosition(PointPosition position) =>
        new(position.X, position.Y);

    private static double Pixels(double current, double configured, double scale)
    {
        var logical = double.IsFinite(current) && current > 0 ? current : configured;
        return double.IsFinite(logical) && logical > 0 ? logical * scale : 1;
    }
}
