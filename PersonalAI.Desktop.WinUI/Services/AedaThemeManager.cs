using Microsoft.UI.Xaml;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Services;

public static class AedaThemeManager
{
    private const string PalettePrefix = "ms-appx:///Themes/Palettes/";

    public static ElementTheme Apply(ThemePreference requested)
    {
        var theme = AedaThemeCatalog.Get(requested);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.StartsWith(PalettePrefix, StringComparison.Ordinal) == true);
        var source = new Uri($"{PalettePrefix}{theme.Id}.xaml");

        if (current?.Source != source)
        {
            if (current is not null)
            {
                dictionaries.Remove(current);
            }

            dictionaries.Add(new ResourceDictionary { Source = source });
        }

        return theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
    }
}
