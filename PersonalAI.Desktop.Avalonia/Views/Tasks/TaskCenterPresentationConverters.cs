using System.Globalization;
using Avalonia.Data.Converters;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.Avalonia.Views.Tasks;

public static class TaskCenterPresentationConverters
{
    private const int AccessiblePreviewLimit = 64;

    public static IValueConverter BoundedName { get; } = new BoundedNameConverter();

    public static IValueConverter Timestamp { get; } = new TimestampConverter();

    public static IValueConverter HasText { get; } = new HasTextConverter();

    public static IValueConverter HasValue { get; } = new HasValueConverter();

    public static IValueConverter IsNull { get; } = new IsNullConverter();

    public static IMultiValueConverter IsSelectedTask { get; } = new IsSelectedTaskConverter();

    public static IMultiValueConverter TaskAccessibleName { get; } = new TaskAccessibleNameConverter();

    private sealed class BoundedNameConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var prefix = Normalize(parameter as string) ?? DefaultPrefix(value);
            var text = Normalize(value switch
            {
                AedaTaskApprovalSummary approval => approval.Title,
                AedaTaskSummary task => task.Title,
                AedaTaskTimelineItem activity => $"{activity.Title}, {activity.Summary}",
                AedaTaskArtifactLink artifact => artifact.Label,
                string supplied => supplied,
                _ => null
            });
            if (text is null)
            {
                return prefix;
            }

            var preview = Bound(text);
            return string.IsNullOrEmpty(prefix) ? preview : $"{prefix}: {preview}";
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

    private sealed class HasValueConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is not null;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class IsNullConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is null;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class IsSelectedTaskConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            values.Count >= 2 &&
            values[0] is TaskId rowId &&
            values[1] is TaskId selectedId &&
            rowId == selectedId;
    }

    private sealed class TaskAccessibleNameConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var selected = values.Count >= 3 &&
                values[0] is TaskId rowId &&
                values[2] is TaskId selectedId &&
                rowId == selectedId;
            var prefix = selected ? "Selected task" : "Task";
            var title = values.Count >= 2 ? Normalize(values[1] as string) : null;
            return title is null ? prefix : $"{prefix}: {Bound(title)}";
        }
    }

    private static string DefaultPrefix(object? value) => value switch
    {
        AedaTaskApprovalSummary => "Approval request",
        AedaTaskSummary => "Task",
        AedaTaskTimelineItem => "Activity",
        AedaTaskArtifactLink => "Artifact",
        _ => string.Empty
    };

    private static string Bound(string text) =>
        text.Length <= AccessiblePreviewLimit
            ? text
            : $"{text[..(AccessiblePreviewLimit - 1)]}…";

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
