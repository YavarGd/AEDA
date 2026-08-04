using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.Avalonia.Themes;

public sealed class AvaloniaThemeManager : IDisposable
{
    private IPlatformSettings? _platformSettings;
    private ThemePreference _theme;

    public AvaloniaThemeManager(ThemePreference theme)
    {
        Apply(theme);
    }

    public void AttachPlatformSettings(IPlatformSettings? platformSettings)
    {
        if (ReferenceEquals(_platformSettings, platformSettings))
        {
            return;
        }

        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }

        _platformSettings = platformSettings;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += OnColorValuesChanged;
        }

        ApplyCurrent();
    }

    public void Apply(ThemePreference theme)
    {
        _theme = AedaThemeCatalog.Normalize(theme);
        ApplyCurrent();
    }

    public static AedaPalette GetPalette(
        ThemePreference theme,
        bool highContrast = false) =>
        highContrast
            ? AedaPalette.HighContrast
            : AedaThemeCatalog.Normalize(theme) switch
            {
                ThemePreference.Graphite => AedaPalette.Graphite,
                ThemePreference.MineralStone => AedaPalette.MineralStone,
                ThemePreference.SharpAlmond => AedaPalette.SharpAlmond,
                _ => AedaPalette.SystemMica
            };

    public void Dispose()
    {
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyCurrent();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyCurrent);
        }
    }

    private void ApplyCurrent()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var highContrast =
            _platformSettings?.GetColorValues().ContrastPreference ==
                ColorContrastPreference.High ||
            global::System.Windows.SystemParameters.HighContrast;
        var palette = GetPalette(_theme, highContrast);
        application.RequestedThemeVariant = palette.IsDark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        SetBrush(application, "AedaNavBackgroundBrush", palette.Navigation);
        SetBrush(application, "AedaBorderBrush", palette.Border);
        SetBrush(application, "AedaUserBubbleBrush", palette.UserBubble);
        SetBrush(application, "AedaAssistantBubbleBrush", palette.AssistantBubble);
        SetBrush(application, "AedaCodeBackgroundBrush", palette.CodeBackground);
        SetBrush(application, "AedaMetadataBrush", palette.Metadata);
        SetBrush(application, "AedaFocusBrush", palette.Focus);
    }

    private static void SetBrush(Application application, string key, string color) =>
        application.Resources[key] = new SolidColorBrush(Color.Parse(color));
}

public sealed record AedaPalette(
    bool IsDark,
    string Navigation,
    string Border,
    string UserBubble,
    string AssistantBubble,
    string CodeBackground,
    string Metadata,
    string Focus)
{
    public static AedaPalette SystemMica { get; } = new(
        false, "#F2F2F2", "#D0D0D0", "#E8E8E8", "#FFFFFF", "#F8F8F8", "#5D5D5D", "#3A3A3A");

    public static AedaPalette Graphite { get; } = new(
        true, "#232323", "#484848", "#302E3B", "#2E2E2E", "#191919", "#C5C5C5", "#B0A5E8");

    public static AedaPalette MineralStone { get; } = new(
        false, "#E1E8E3", "#BFCBC5", "#DCE6DF", "#FBFCFA", "#F5F8F5", "#56655F", "#3D665B");

    public static AedaPalette SharpAlmond { get; } = new(
        false, "#F4F0FA", "#D5CDF0", "#E1DBF7", "#FFFFFF", "#F9F7FC", "#5B5670", "#6758C5");

    public static AedaPalette HighContrast { get; } = new(
        true, "#000000", "#FFFFFF", "#000000", "#000000", "#000000", "#FFFFFF", "#FFFF00");
}
