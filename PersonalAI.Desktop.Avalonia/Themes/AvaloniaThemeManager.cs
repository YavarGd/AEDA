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
        SetBrush(application, "PrimaryTextBrush", palette.PrimaryText);
        SetBrush(application, "SecondaryTextBrush", palette.SecondaryText);
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

    public string UserBubble => SubtleSurface;

    public string AssistantBubble => ElevatedSurface;

    public string CodeBackground => CardSurface;

    public string Metadata => MutedText;

    public string Focus => FocusRing;

    public static AedaPalette SystemMica { get; } = new(
        false,
        "#EEF1F5", "#F7F8FA", "#FBFCFE", "#FFFFFF", "#F4F6F8", "#E9EDF2",
        "#1D2025", "#505862", "#737B85", "#D7DBE2", "#B9C0CA",
        "#4B76A8", "#3E6898", "#31577F", "#5C8A57", "#B77A2F", "#B44949",
        "#2F91B8", "#2E73C8");

    public static AedaPalette Graphite { get; } = new(
        true,
        "#16181C", "#202329", "#1B1E23", "#292D34", "#24272D", "#30343C",
        "#F1F2F4", "#C4C7CD", "#9CA1AA", "#3B4049", "#555C68",
        "#8D84BC", "#A198D0", "#766DA8", "#74A36B", "#C6924E", "#D66565",
        "#4E9DB6", "#B0A5E8");

    public static AedaPalette MineralStone { get; } = new(
        false,
        "#DED5C5", "#E9E0D0", "#F3EEE4", "#FAF7F0", "#F6F1E8", "#E7E1D5",
        "#282A24", "#55584B", "#767665", "#D2C7B5", "#B4A88F",
        "#6D7746", "#5E683A", "#50592F", "#718653", "#B47A36", "#B65E4C",
        "#5E8F84", "#637248");

    public static AedaPalette SharpAlmond { get; } = new(
        true,
        "#11151A", "#171C21", "#1C2228", "#232A31", "#20262C", "#292F37",
        "#F2F0F5", "#C7C3CD", "#9995A2", "#353C45", "#505865",
        "#9D78E5", "#AF8DF0", "#865FCF", "#7EA866", "#D69A48", "#D65E61",
        "#45AFC1", "#B28AF2");

    public static AedaPalette HighContrast { get; } = new(
        true,
        "#000000", "#000000", "#000000", "#000000", "#000000", "#1A1A1A",
        "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF",
        "#00FFFF", "#FFFFFF", "#00FFFF", "#00FF00", "#FFFF00", "#FF4040",
        "#00FFFF", "#FFFF00");
}
