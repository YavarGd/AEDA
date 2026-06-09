using System.Windows;
using System.Windows.Forms;
using PersonalAI.Core.Ui;

namespace PersonalAI.Desktop.Windows;

public static class PaletteWindowPlacementService
{
    public static void PlaceNearCursor(Window window)
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var bounds = new RectBounds(
            screen.WorkingArea.Left,
            screen.WorkingArea.Top,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height);
        var position = PalettePlacementCalculator.NearCursor(
            bounds,
            new PointPosition(cursor.X, cursor.Y),
            window.Width,
            window.Height);

        window.Left = position.X;
        window.Top = position.Y;
    }
}
