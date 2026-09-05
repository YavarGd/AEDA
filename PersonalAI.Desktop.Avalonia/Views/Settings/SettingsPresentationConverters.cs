using System.Globalization;
using Avalonia.Data.Converters;

namespace PersonalAI.Desktop.Avalonia.Views.Settings;

public static class SettingsPresentationConverters
{
    private const string OllamaUnavailablePrefix = "Ollama models unavailable:";

    public static IValueConverter SafeProviderStatus { get; } =
        new SafeProviderStatusConverter();

    public static string SanitizeProviderStatus(string? value) =>
        value?.StartsWith(OllamaUnavailablePrefix, StringComparison.Ordinal) == true
            ? "Ollama is unavailable. Check that Ollama is running."
            : value ?? string.Empty;

    private sealed class SafeProviderStatusConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            SanitizeProviderStatus(value as string);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
