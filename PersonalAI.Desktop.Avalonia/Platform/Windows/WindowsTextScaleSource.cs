using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public interface IWindowsTextScaleSource : IDisposable
{
    double Factor { get; }

    event EventHandler? Changed;
}

public sealed class WindowsTextScaleSource : IWindowsTextScaleSource
{
    private readonly UISettings? _settings;

    public WindowsTextScaleSource()
    {
        try
        {
            _settings = new UISettings();
            _settings.TextScaleFactorChanged += OnTextScaleFactorChanged;
        }
        catch (Exception exception) when (
            exception is COMException or
            PlatformNotSupportedException or
            TypeInitializationException)
        {
            _settings = null;
        }
    }

    public double Factor
    {
        get
        {
            try
            {
                return _settings?.TextScaleFactor ?? 1;
            }
            catch (COMException)
            {
                return 1;
            }
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
        }
    }

    private void OnTextScaleFactorChanged(UISettings sender, object args) =>
        Changed?.Invoke(this, EventArgs.Empty);
}
