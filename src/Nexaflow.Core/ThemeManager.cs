using System.Windows;

namespace Nexaflow.Core;

internal static class ThemeManager
{
    private const string PackBase = "pack://application:,,,/Nexacore;component/Themes/Colors.";

    internal static void Apply(ThemeOption theme)
    {
        var uri    = new Uri($"{PackBase}{theme}.xaml");
        var dict   = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        merged[0]  = dict;
    }
}
