using System.Globalization;
using Avalonia.Data.Converters;

namespace PersonalAI.Desktop.Avalonia.Views.Memory;

public static class MemoryPresentationConverters
{
    public static IValueConverter BoundedName { get; } = new BoundedNameConverter();

    public static IValueConverter Timestamp { get; } = new TimestampConverter();

    public static IValueConverter HasText { get; } = new HasTextConverter();

    public static IMultiValueConverter IsSelected { get; } = new SelectedMemoryConverter();

    private sealed class BoundedNameConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var text = value as string ?? string.Empty;
            var preview = text.Length <= 48 ? text : $"{text[..47]}…";
            return $"{parameter ?? "Memory"}: {preview}";
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class TimestampConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is DateTimeOffset timestamp
                ? timestamp.ToLocalTime().ToString("g", culture)
                : string.Empty;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class HasTextConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is string text && !string.IsNullOrWhiteSpace(text);

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class SelectedMemoryConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            values.Count >= 2 &&
            values[0] is string rowId &&
            values[1] is string selectedId &&
            string.Equals(rowId, selectedId, StringComparison.Ordinal);
    }
}
