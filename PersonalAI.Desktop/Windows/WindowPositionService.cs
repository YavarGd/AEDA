using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using PersonalAI.Core.Ui;

namespace PersonalAI.Desktop.Windows;

public sealed class WindowPositionService
{
    private readonly string _settingsPath;
    private bool _hasSessionPosition;
    private WindowPosition _sessionPosition;

    public WindowPositionService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI",
            "window-position.json"))
    {
    }

    public WindowPositionService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public void PlaceWindow(Window window)
    {
        var areas = GetWorkingAreas();
        var position = GetPreferredPosition(window.Width, window.Height, areas);

        if (position is null)
        {
            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor);
            var bounds = new RectBounds(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height);
            var centered = PalettePlacementCalculator.CenterInBounds(
                bounds,
                window.Width,
                window.Height);
            position = new WindowPosition(centered.X, centered.Y);
        }

        _sessionPosition = position.Value;
        _hasSessionPosition = true;
        window.Left = position.Value.Left;
        window.Top = position.Value.Top;
    }

    public void RememberPosition(Window window)
    {
        var position = new WindowPosition(window.Left, window.Top);
        _sessionPosition = position;
        _hasSessionPosition = true;
        PersistPosition(position);
    }

    private WindowPosition? GetPreferredPosition(
        double width,
        double height,
        IReadOnlyList<RectBounds> workingAreas)
    {
        if (_hasSessionPosition &&
            WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
                _sessionPosition,
                width,
                height,
                workingAreas))
        {
            return _sessionPosition;
        }

        var persisted = ReadPersistedPosition();

        if (persisted is not null &&
            WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
                persisted.Value,
                width,
                height,
                workingAreas))
        {
            _sessionPosition = persisted.Value;
            _hasSessionPosition = true;
            return persisted;
        }

        return null;
    }

    private static IReadOnlyList<RectBounds> GetWorkingAreas()
    {
        return Screen.AllScreens
            .Select(screen => new RectBounds(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height))
            .ToArray();
    }

    private WindowPosition? ReadPersistedPosition()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<WindowPosition>(json);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is JsonException)
        {
            return null;
        }
    }

    private void PersistPosition(WindowPosition position)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(position));
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
        }
    }
}
