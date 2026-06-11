using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Context;
using PersonalAI.Core.Ui;
using Windows.Graphics;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiWindowPlacementService
{
    private readonly string _settingsPath;
    private bool _hasRememberedPosition;
    private bool _isApplyingPosition;
    private WindowPosition _rememberedPosition;

    public bool RememberWindowPosition { get; set; } = true;

    public WinUiWindowPlacementService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI",
            "winui-window-position.json"))
    {
    }

    public WinUiWindowPlacementService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public void ConfigureWindow(Window window)
    {
        window.AppWindow.Resize(new SizeInt32(1080, 760));
        window.AppWindow.Changed += (_, args) =>
        {
            if (_isApplyingPosition ||
                !RememberWindowPosition ||
                !args.DidPositionChange)
            {
                return;
            }

            RememberPosition(window);
        };
    }

    public void PlaceForActivation(
        Window window,
        ActiveWindowReference? externalWindow)
    {
        var areas = GetWorkingAreas();
        var size = window.AppWindow.Size;
        var persisted = RememberWindowPosition
            ? GetPreferredPosition(size.Width, size.Height, areas)
            : null;
        var position = persisted ?? GetActivationPosition(
            externalWindow,
            size.Width,
            size.Height);

        ApplyPosition(window, position);
    }

    private WindowPosition? GetPreferredPosition(
        double width,
        double height,
        IReadOnlyList<RectBounds> workingAreas)
    {
        if (_hasRememberedPosition &&
            WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
                _rememberedPosition,
                width,
                height,
                workingAreas))
        {
            return _rememberedPosition;
        }

        var persisted = ReadPersistedPosition();

        if (persisted is not null &&
            WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
                persisted.Value,
                width,
                height,
                workingAreas))
        {
            _rememberedPosition = persisted.Value;
            _hasRememberedPosition = true;
            return persisted;
        }

        return null;
    }

    private static WindowPosition GetActivationPosition(
        ActiveWindowReference? externalWindow,
        double width,
        double height)
    {
        var bounds = GetActivationBounds(externalWindow);
        var centered = PalettePlacementCalculator.CenterInBounds(
            bounds,
            width,
            height);

        return new WindowPosition(centered.X, centered.Y);
    }

    private void ApplyPosition(Window window, WindowPosition position)
    {
        _isApplyingPosition = true;

        try
        {
            window.AppWindow.Move(new PointInt32(
                (int)Math.Round(position.Left),
                (int)Math.Round(position.Top)));
        }
        finally
        {
            _isApplyingPosition = false;
        }
    }

    private void RememberPosition(Window window)
    {
        var position = new WindowPosition(
            window.AppWindow.Position.X,
            window.AppWindow.Position.Y);
        _rememberedPosition = position;
        _hasRememberedPosition = true;
        PersistPosition(position);
    }

    public void ResetRememberedPosition()
    {
        _hasRememberedPosition = false;

        try
        {
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
        }
    }

    private static IReadOnlyList<RectBounds> GetWorkingAreas()
    {
        return [GetActivationBounds(externalWindow: null)];
    }

    private static RectBounds GetActivationBounds(ActiveWindowReference? externalWindow)
    {
        nint monitor;

        if (externalWindow is not null)
        {
            monitor = MonitorFromWindow(externalWindow.WindowHandle, MonitorDefaultToNearest);
        }
        else
        {
            GetCursorPos(out var cursor);
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        }

        var info = new MonitorInfo
        {
            cbSize = Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor != 0 && GetMonitorInfo(monitor, ref info))
        {
            return new RectBounds(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top);
        }

        return new RectBounds(0, 0, 1280, 720);
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

    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromPoint(
        NativePoint pt,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromWindow(
        nint hwnd,
        uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(
        nint hMonitor,
        ref MonitorInfo lpmi);
}
