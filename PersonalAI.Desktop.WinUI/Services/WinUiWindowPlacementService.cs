using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Context;
using PersonalAI.Core.Ui;
using Windows.Graphics;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiWindowPlacementService
{
    public const int DefaultWindowWidth = 1080;
    public const int DefaultWindowHeight = 760;
    public const int MinimumWindowWidth = 1080;
    public const int MinimumWindowHeight = 760;

    private readonly string _settingsPath;
    private readonly int _defaultWidth;
    private readonly int _defaultHeight;
    private readonly int _minimumWidth;
    private readonly int _minimumHeight;
    private bool _hasRememberedPosition;
    private bool _isApplyingPosition;
    private bool _isApplyingSize;
    private WindowPosition _rememberedPosition;

    public bool RememberWindowPosition { get; set; } = true;

    public WinUiWindowPlacementService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI",
            "winui-window-position.json"),
            DefaultWindowWidth,
            DefaultWindowHeight,
            MinimumWindowWidth,
            MinimumWindowHeight)
    {
    }

    public WinUiWindowPlacementService(string settingsPath)
        : this(
            settingsPath,
            DefaultWindowWidth,
            DefaultWindowHeight,
            MinimumWindowWidth,
            MinimumWindowHeight)
    {
    }

    public WinUiWindowPlacementService(
        string settingsPath,
        int defaultWidth,
        int defaultHeight,
        int minimumWidth,
        int minimumHeight)
    {
        _settingsPath = settingsPath;
        _defaultWidth = defaultWidth;
        _defaultHeight = defaultHeight;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
    }

    public void ConfigureWindow(
        Window window,
        bool rememberPositionChanges = true)
    {
        window.AppWindow.Resize(new SizeInt32(_defaultWidth, _defaultHeight));
        window.AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange)
            {
                EnforceMinimumSize(window);
            }

            if (_isApplyingPosition ||
                !rememberPositionChanges ||
                !RememberWindowPosition ||
                !args.DidPositionChange)
            {
                return;
            }

            RememberPosition(window);
        };
    }

    private void EnforceMinimumSize(Window window)
    {
        if (_isApplyingSize)
        {
            return;
        }

        var size = window.AppWindow.Size;
        var width = Math.Max(size.Width, _minimumWidth);
        var height = Math.Max(size.Height, _minimumHeight);

        if (width == size.Width && height == size.Height)
        {
            return;
        }

        _isApplyingSize = true;

        try
        {
            window.AppWindow.Resize(new SizeInt32(width, height));
        }
        finally
        {
            _isApplyingSize = false;
        }
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

    public void PlaceCentered(
        Window window,
        ActiveWindowReference? externalWindow)
    {
        var size = window.AppWindow.Size;
        ApplyPosition(
            window,
            GetActivationPosition(externalWindow, size.Width, size.Height));
    }

    public void PlaceAssistPill(
        Window window,
        ActiveWindowReference? externalWindow)
    {
        var areas = GetWorkingAreas();
        var size = window.AppWindow.Size;
        var persisted = RememberWindowPosition
            ? GetPreferredPosition(size.Width, size.Height, areas)
            : null;
        var bounds = GetActivationBounds(externalWindow);
        var fallback = PalettePlacementCalculator.BottomRightInBounds(
            bounds,
            size.Width,
            size.Height,
            margin: 20);

        ApplyPosition(
            window,
            persisted ?? new WindowPosition(fallback.X, fallback.Y));
    }

    public void PlaceExpandedNearRemembered(
        Window window,
        ActiveWindowReference? externalWindow)
    {
        var size = window.AppWindow.Size;
        var areas = GetWorkingAreas();
        var remembered = GetRememberedPosition();
        var position = remembered is null
            ? null
            : WindowPositionValidator.ClampToVisibleWorkingArea(
                remembered.Value,
                size.Width,
                size.Height,
                areas);

        if (position is not null)
        {
            ApplyPosition(window, position.Value);
            return;
        }

        var bounds = GetActivationBounds(externalWindow);
        var fallback = PalettePlacementCalculator.BottomRightInBounds(
            bounds,
            size.Width,
            size.Height,
            margin: 20);
        ApplyPosition(window, new WindowPosition(fallback.X, fallback.Y));
    }

    private WindowPosition? GetPreferredPosition(
        double width,
        double height,
        IReadOnlyList<RectBounds> workingAreas)
    {
        if (_hasRememberedPosition &&
            WindowPositionValidator.ClampToVisibleWorkingArea(
                _rememberedPosition,
                width,
                height,
                workingAreas) is { } remembered)
        {
            _rememberedPosition = remembered;
            return remembered;
        }

        var persisted = ReadPersistedPosition();

        if (persisted is not null &&
            WindowPositionValidator.ClampToVisibleWorkingArea(
                persisted.Value,
                width,
                height,
                workingAreas) is { } corrected)
        {
            _rememberedPosition = corrected;
            _hasRememberedPosition = true;
            return corrected;
        }

        return null;
    }

    private WindowPosition? GetRememberedPosition()
    {
        if (_hasRememberedPosition)
        {
            return _rememberedPosition;
        }

        var persisted = ReadPersistedPosition();
        if (persisted is null)
        {
            return null;
        }

        _rememberedPosition = persisted.Value;
        _hasRememberedPosition = true;
        return persisted;
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

    public void RememberPosition(Window window)
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
        var areas = new List<RectBounds>();
        _ = EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo
            {
                cbSize = Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(new RectBounds(
                    info.rcWork.Left,
                    info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top));
            }

            return true;
        }, 0);

        return areas.Count > 0
            ? areas
            : [GetActivationBounds(externalWindow: null)];
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
            return WindowPositionJson.Deserialize(json);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
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
                WindowPositionJson.Serialize(position));
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
        }
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint deviceContext,
        nint monitorRect,
        nint data);

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

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(
        nint hMonitor,
        ref MonitorInfo lpmi);
}
