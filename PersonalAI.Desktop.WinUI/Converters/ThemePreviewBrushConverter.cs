using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Converters;

public sealed class ThemePreviewBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value is ThemePreference theme ? theme switch
        {
            ThemePreference.Graphite => Windows.UI.Color.FromArgb(255, 46, 46, 46),
            ThemePreference.MineralStone => Windows.UI.Color.FromArgb(255, 61, 102, 91),
            ThemePreference.SharpAlmond => Windows.UI.Color.FromArgb(255, 176, 165, 232),
            _ => Windows.UI.Color.FromArgb(255, 243, 243, 243)
        } : Microsoft.UI.Colors.Transparent);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
