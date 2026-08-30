using System.Globalization;
using Avalonia.Data.Converters;
using PersonalAI.Core.Research;

namespace PersonalAI.Desktop.Avalonia.Views.Research;

public static class ResearchPresentationConverters
{
    private const int AccessiblePreviewLimit = 64;

    public static IValueConverter BoundedName { get; } = new BoundedNameConverter();

    public static IValueConverter Timestamp { get; } = new TimestampConverter();

    public static IValueConverter HasText { get; } = new HasTextConverter();

    public static IValueConverter IsEmpty { get; } = new IsEmptyConverter();

    public static IValueConverter Count { get; } = new CountConverter();

    public static IValueConverter LocalSource { get; } = new LocalSourceConverter();

    public static IMultiValueConverter IsSelectedReport { get; } = new SelectedReportConverter();

    public static IMultiValueConverter Provenance { get; } = new ProvenanceConverter();

    public static IMultiValueConverter EvidenceRelation { get; } = new EvidenceRelationConverter();

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
                ResearchClaim claim => claim.Text,
                VerificationReport report => report.Question.Text,
                VerificationFinding finding => $"{finding.Verdict}, {finding.SafeSummary}",
                EvidenceItem evidence => evidence.Excerpt,
                CitationReference citation => citation.SourceLabel,
                string supplied => supplied,
                _ => null
            });
            if (text is null)
            {
                return prefix;
            }

            var preview = text.Length <= AccessiblePreviewLimit
                ? text
                : $"{text[..(AccessiblePreviewLimit - 1)]}…";
            return string.IsNullOrEmpty(prefix) ? preview : $"{prefix}: {preview}";
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();

        private static string DefaultPrefix(object? value) => value switch
        {
            ResearchClaim => "Extracted claim",
            VerificationReport => "Verification report",
            VerificationFinding => "Finding",
            EvidenceItem => "Evidence",
            CitationReference => "Citation",
            _ => string.Empty
        };

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

    private sealed class LocalSourceConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is true ? "Local source" : "External source";

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class IsEmptyConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is not System.Collections.ICollection collection || collection.Count == 0;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class CountConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) => value switch
            {
                IReadOnlyCollection<ResearchClaim> claims => claims.Count.ToString(culture),
                IReadOnlyCollection<EvidenceItem> evidence => evidence.Count.ToString(culture),
                _ => "0"
            };

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class SelectedReportConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            values.Count >= 2 &&
            values[0] is VerificationReportId rowId &&
            values[1] is VerificationReportId selectedId &&
            rowId == selectedId;
    }

    private sealed class ProvenanceConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var localOnly = values.Count > 0 && values[0] is true;
            var remoteUsed = values.Count > 1 && values[1] is true;
            if (remoteUsed)
            {
                return localOnly
                    ? "Local evidence with remote search used"
                    : "Remote search used";
            }

            return localOnly
                ? "Local evidence only · Remote search not used"
                : "Remote search not used";
        }
    }

    private sealed class EvidenceRelationConverter : IMultiValueConverter
    {
        public object Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var supports = values.Count > 0 && values[0] is true;
            var contradicts = values.Count > 1 && values[1] is true;
            return contradicts
                ? "Contradicts claim"
                : supports
                    ? "Supports claim"
                    : "Related";
        }
    }
}
