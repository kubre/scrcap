using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Scrcap.Core;
using WpfColor = System.Windows.Media.Color;

namespace Scrcap.Windows.UI.Resources;

public static class AppThemeService
{
    private static readonly IReadOnlyDictionary<string, WpfColor> LightPalette = new Dictionary<string, WpfColor>
    {
        ["ColorInk"] = WpfColor.FromRgb(25, 26, 28),
        ["ColorMuted"] = WpfColor.FromRgb(102, 105, 115),
        ["ColorChrome"] = WpfColor.FromRgb(247, 247, 248),
        ["ColorPanel"] = Colors.White,
        ["ColorRule"] = WpfColor.FromRgb(197, 198, 203),
        ["ColorCanvas"] = Colors.White,
        ["ColorHover"] = WpfColor.FromRgb(240, 240, 242),
        ["ColorPressed"] = WpfColor.FromRgb(229, 229, 232),
    };

    private static readonly IReadOnlyDictionary<string, WpfColor> DarkPalette = new Dictionary<string, WpfColor>
    {
        ["ColorInk"] = WpfColor.FromRgb(242, 243, 245),
        ["ColorMuted"] = WpfColor.FromRgb(168, 171, 181),
        ["ColorChrome"] = WpfColor.FromRgb(28, 29, 32),
        ["ColorPanel"] = WpfColor.FromRgb(38, 39, 43),
        ["ColorRule"] = WpfColor.FromRgb(74, 76, 84),
        ["ColorCanvas"] = WpfColor.FromRgb(18, 19, 22),
        ["ColorHover"] = WpfColor.FromRgb(50, 52, 58),
        ["ColorPressed"] = WpfColor.FromRgb(62, 65, 72),
    };

    public static ThemeMode Resolve(ThemeMode requested) =>
        requested == ThemeMode.System ? SystemThemeMode() : requested;

    public static void Apply(ResourceDictionary resources, ThemeMode requested)
    {
        var palette = Resolve(requested) == ThemeMode.Dark ? DarkPalette : LightPalette;
        foreach (var (colorKey, color) in palette)
        {
            SetColor(resources, colorKey, color);
            SetBrushColor(resources, "Brush" + colorKey["Color".Length..], color);
        }
    }

    private static ThemeMode SystemThemeMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ThemeMode.Light;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
    }

    private static void SetColor(ResourceDictionary resources, string key, WpfColor color)
    {
        if (TryFindDictionary(resources, key) is { } dictionary)
        {
            dictionary[key] = color;
        }
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, WpfColor color)
    {
        if (TryFindDictionary(resources, key) is { } dictionary)
        {
            if (dictionary[key] is SolidColorBrush brush)
            {
                brush.Color = color;
                return;
            }

            dictionary[key] = new SolidColorBrush(color);
        }
    }

    private static ResourceDictionary? TryFindDictionary(ResourceDictionary resources, string key)
    {
        if (resources.Contains(key))
        {
            return resources;
        }

        foreach (var merged in resources.MergedDictionaries)
        {
            if (TryFindDictionary(merged, key) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
