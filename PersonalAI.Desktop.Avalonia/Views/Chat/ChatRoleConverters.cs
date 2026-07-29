using System.Globalization;
using Avalonia.Data.Converters;
using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Small converters the chat item template needs to distinguish speakers by text and
/// layout rather than by colour alone.
/// </summary>
public static class ChatRoleConverters
{
    public static IValueConverter IsAssistant { get; } =
        new RoleMatchConverter(ChatRole.Assistant);

    public static IValueConverter IsNotAssistant { get; } =
        new RoleMatchConverter(ChatRole.Assistant, negate: true);

    public static IValueConverter RoleLabel { get; } = new RoleLabelConverter();

    private sealed class RoleMatchConverter(ChatRole expected, bool negate = false)
        : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is ChatRole role && role == expected ? !negate : negate;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class RoleLabelConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is ChatRole role
                ? ChatPresentation.DescribeRole(role)
                : string.Empty;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
