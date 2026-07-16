using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Core.Modules;

namespace PersonalAI.Desktop.WinUI.Converters;

/// <summary>
/// Maps a module tile's display name (e.g. "AEDA Code", "AEDA Memory") to a
/// themed accent brush, so each module on the dashboard gets a distinct flat
/// color identity instead of every tile sharing the same neutral surface.
///
/// This converter does NOT require any ViewModel change — it keys off the
/// DisplayName string that ModuleTiles already exposes today. If a future
/// module is added with a name not recognized below, it falls back to
/// PersonalAi*ModuleDefault* brushes rather than throwing.
///
/// Usage in XAML (see ThemeResources.xaml for the three pre-wired instances):
///   Background="{Binding DisplayName, Converter={StaticResource ModuleAccentFillConverter}}"
///   Foreground="{Binding DisplayName, Converter={StaticResource ModuleAccentForegroundConverter}}"
///   BorderBrush="{Binding DisplayName, Converter={StaticResource ModuleAccentBorderConverter}}"
///
/// Set the AccentKind property per instance (Fill / Foreground / Border) via
/// the x:Key'd declarations in ThemeResources.xaml — three separate converter
/// instances, one per kind, all driven by this one class.
/// </summary>
public enum ModuleAccentKind
{
    Fill,
    Foreground,
    Border,
}

public sealed class ModuleAccentBrushConverter : IValueConverter
{
    public ModuleAccentKind AccentKind { get; set; } = ModuleAccentKind.Fill;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var moduleKey = value is AedaModuleKind kind
            ? kind switch
            {
                AedaModuleKind.Code => "Code",
                AedaModuleKind.Memory => "Memory",
                AedaModuleKind.Research => "Research",
                AedaModuleKind.TaskCenter => "Task",
                _ => "Default"
            }
            : ResolveModuleKey(value as string ?? string.Empty);
        var resourceKey = $"PersonalAiModule{moduleKey}{AccentKind}Brush";

        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        // Fall back to the generic "Default" module brush if the specific
        // module key isn't recognized (e.g. a brand-new module added in the
        // ViewModel before this converter's lookup table is updated).
        var fallbackKey = $"PersonalAiModuleDefault{AccentKind}Brush";
        if (Application.Current.Resources.TryGetValue(fallbackKey, out var fallback)
            && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    /// <summary>
    /// Normalizes a display name like "AEDA Code" or "Aeda Pic Studio" into
    /// the PascalCase key segment used by the brush resource names
    /// (e.g. "Code", "PicStudio"). Matching is case-insensitive and tolerant
    /// of the "AEDA"/"Aeda" prefix being present or absent.
    /// </summary>
    private static string ResolveModuleKey(string displayName)
    {
        var normalized = displayName
            .Replace("AEDA", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return normalized.ToLowerInvariant() switch
        {
            "code" => "Code",
            "memory" => "Memory",
            "research" => "Research",
            "claw" => "Claw",
            "pic studio" => "PicStudio",
            "office" => "Office",
            "voice" => "Voice",
            _ => "Default",
        };
    }
}
