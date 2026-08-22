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
        SetBrush(application, "WindowBackgroundBrush", palette.WindowBackground);
        SetBrush(application, "ShellSurfaceBrush", palette.ShellSurface);
        SetBrush(application, "ContentSurfaceBrush", palette.ContentSurface);
        SetBrush(application, "ElevatedSurfaceBrush", palette.ElevatedSurface);
        SetBrush(application, "CardSurfaceBrush", palette.CardSurface);
        SetBrush(application, "SubtleSurfaceBrush", palette.SubtleSurface);
        SetBrush(application, "SurfaceAltBrush", palette.SurfaceAlt);
        SetBrush(application, "AccentSoftBrush", palette.AccentSoft);
        SetBrush(application, "AccentTextBrush", palette.AccentText);
        SetBrush(application, "PrimaryTextBrush", palette.PrimaryText);
        SetBrush(application, "SecondaryTextBrush", palette.SecondaryText);
        SetBrush(application, "TertiaryTextBrush", palette.MutedText);
        SetBrush(application, "MutedTextBrush", palette.MutedText);
        SetBrush(application, "BorderBrush", palette.Border);
        SetBrush(application, "StrongBorderBrush", palette.StrongBorder);
        SetBrush(application, "AccentBrush", palette.Accent);
        SetBrush(application, "AccentHoverBrush", palette.AccentHover);
        SetBrush(application, "AccentPressedBrush", palette.AccentPressed);
        SetBrush(application, "SuccessBrush", palette.Success);
        SetBrush(application, "WarningBrush", palette.Warning);
        SetBrush(application, "ErrorBrush", palette.Error);
        SetBrush(application, "ListeningBrush", palette.Listening);
        SetBrush(application, "FocusRingBrush", palette.FocusRing);
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
    string WindowBackground,
    string ShellSurface,
    string ContentSurface,
    string ElevatedSurface,
    string CardSurface,
    string SubtleSurface,
    string SurfaceAlt,
    string AccentSoft,
    string AccentText,
    string PrimaryText,
    string SecondaryText,
    string MutedText,
    string Border,
    string StrongBorder,
    string Accent,
    string AccentHover,
    string AccentPressed,
    string Success,
    string Warning,
    string Error,
    string Listening,
    string FocusRing)
{
    public string Navigation => ShellSurface;

    public string UserBubble => AccentSoft;

    public string AssistantBubble => ElevatedSurface;

    public string CodeBackground => SurfaceAlt;

    public string Metadata => MutedText;

    public string Focus => FocusRing;

    public static AedaPalette SystemMica { get; } = new(
        false,
        "#EEF0F2", "#FFFFFF", "#EEF0F2", "#FFFFFF", "#FFFFFF", "#F6F7F9",
        "#F6F7F9", "#E4EBF3", "#FFFFFF", "#1B222A", "#5B6572", "#8A93A0",
        "#DCE1E6", "#DCE1E6", "#4C6FA0", "#4C6FA0", "#4C6FA0", "#3E8F5B",
        "#B4842A", "#B4483E", "#2E86A8", "#4C6FA0");

    public static AedaPalette Graphite { get; } = new(
        true,
        "#16181C", "#202329", "#1B1E23", "#292D34", "#24272D", "#30343C",
        "#24272D", "#302E3B", "#F1F2F4", "#F1F2F4", "#C4C7CD", "#9CA1AA", "#3B4049", "#555C68",
        "#8D84BC", "#A198D0", "#766DA8", "#74A36B", "#C6924E", "#D66565",
        "#4E9DB6", "#B0A5E8");

    public static AedaPalette MineralStone { get; } = new(
        false,
        "#EFEAE1", "#FBF7F0", "#EFEAE1", "#FBF7F0", "#FBF7F0", "#F4EEE3",
        "#F4EEE3", "#E7ECDF", "#FFFFFF", "#2B2620", "#6B6255", "#9A9082",
        "#E0D6C4", "#E0D6C4", "#6B7F5C", "#6B7F5C", "#6B7F5C", "#5C7A3E",
        "#B4842A", "#A85C42", "#3B7F86", "#6B7F5C");

    public static AedaPalette SharpAlmond { get; } = new(
        true,
        "#15131A", "#1D1B23", "#15131A", "#1D1B23", "#1D1B23", "#24222C",
        "#24222C", "#2C2740", "#17151C", "#F1EEF6", "#A9A5B4", "#77738A",
        "#34313D", "#34313D", "#9C8CE0", "#9C8CE0", "#9C8CE0", "#6FCB8F",
        "#E0A94B", "#E08079", "#5FC2D9", "#B3A6EA");

    public static AedaPalette HighContrast { get; } = new(
        true,
        "#000000", "#000000", "#000000", "#000000", "#000000", "#1A1A1A",
        "#1A1A1A", "#1A1A1A", "#000000", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF",
        "#00FFFF", "#FFFFFF", "#00FFFF", "#00FF00", "#FFFF00", "#FF4040",
        "#00FFFF", "#FFFF00");
}
