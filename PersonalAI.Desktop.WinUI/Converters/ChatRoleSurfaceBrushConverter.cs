using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.WinUI.Converters;

public sealed class ChatRoleSurfaceBrushConverter : IValueConverter
{
    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        var key = value is ChatRole role
            ? role switch
            {
                ChatRole.User => "PersonalAiMessageSurfaceBrush",
                ChatRole.Tool => "PersonalAiToolActivitySurfaceBrush",
                _ => "Transparent"
            }
            : "Transparent";

        if (key == "Transparent")
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language) =>
        throw new NotSupportedException();
}
