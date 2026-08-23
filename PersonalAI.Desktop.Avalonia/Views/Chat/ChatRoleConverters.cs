using System.Globalization;
using Avalonia.Data.Converters;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Small converters the chat item template needs to distinguish speakers by text and
/// layout rather than by colour alone.
/// </summary>
public static class ChatRoleConverters
{
    public static IValueConverter IsAssistant { get; } =
        new RoleMatchConverter(ChatRole.Assistant);

    public static IValueConverter IsUser { get; } =
        new RoleMatchConverter(ChatRole.User);

    public static IValueConverter IsSystem { get; } =
        new RoleMatchConverter(ChatRole.System);

    public static IValueConverter IsNotAssistant { get; } =
        new RoleMatchConverter(ChatRole.Assistant, negate: true);

    public static IValueConverter IsTool { get; } =
        new RoleMatchConverter(ChatRole.Tool);

    /// <summary>
    /// True for roles that render as plain prose text: everything except Assistant (which
    /// renders as markdown) and Tool (which renders as a distinct activity row).
    /// </summary>
    public static IValueConverter IsPlainText { get; } = new PlainTextRoleConverter();

    public static IValueConverter RoleLabel { get; } = new RoleLabelConverter();

    public static IValueConverter IsStreaming { get; } =
        new MessageStatusMatchConverter(ChatMessageStatus.Streaming);

    public static IValueConverter IsCancelled { get; } =
        new MessageStatusMatchConverter(ChatMessageStatus.Cancelled);

    public static IValueConverter IsFailed { get; } =
        new MessageStatusMatchConverter(ChatMessageStatus.Failed);

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

    private sealed class MessageStatusMatchConverter(ChatMessageStatus expected)
        : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is ChatMessageStatus status && status == expected;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class PlainTextRoleConverter : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            value is ChatRole role && role != ChatRole.Assistant && role != ChatRole.Tool;

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
