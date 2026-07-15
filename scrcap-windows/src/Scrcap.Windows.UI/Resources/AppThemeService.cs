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
        ["ColorMuted"] = WpfColor.FromRgb(101, 105, 112),
        ["ColorChrome"] = WpfColor.FromRgb(242, 242, 243),
        ["ColorPanel"] = WpfColor.FromRgb(251, 251, 252),
        ["ColorWindowFrame"] = WpfColor.FromRgb(133, 135, 141),
        ["ColorRule"] = WpfColor.FromRgb(168, 169, 174),
        ["ColorHairline"] = WpfColor.FromArgb(28, 0, 0, 0),
        ["ColorCanvas"] = WpfColor.FromRgb(214, 215, 218),
        ["ColorWell"] = WpfColor.FromRgb(251, 251, 252),
        ["ColorHover"] = WpfColor.FromArgb(31, 227, 14, 32),
        ["ColorPressed"] = WpfColor.FromArgb(66, 227, 14, 32),
        ["ColorActiveWash"] = WpfColor.FromArgb(51, 227, 14, 32),
    };

    private static readonly IReadOnlyDictionary<string, WpfColor> DarkPalette = new Dictionary<string, WpfColor>
    {
        ["ColorInk"] = WpfColor.FromRgb(233, 233, 234),
        ["ColorMuted"] = WpfColor.FromRgb(153, 154, 157),
        ["ColorChrome"] = WpfColor.FromRgb(27, 27, 28),
        ["ColorPanel"] = WpfColor.FromRgb(16, 16, 17),
        ["ColorWindowFrame"] = WpfColor.FromRgb(82, 82, 86),
        ["ColorRule"] = WpfColor.FromRgb(63, 63, 65),
        ["ColorHairline"] = WpfColor.FromArgb(26, 255, 255, 255),
        ["ColorCanvas"] = WpfColor.FromRgb(25, 26, 27),
        ["ColorWell"] = WpfColor.FromRgb(16, 16, 17),
        ["ColorHover"] = WpfColor.FromArgb(31, 227, 14, 32),
        ["ColorPressed"] = WpfColor.FromArgb(66, 227, 14, 32),
        ["ColorActiveWash"] = WpfColor.FromArgb(51, 227, 14, 32),
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
                if (brush.IsFrozen)
                {
                    dictionary[key] = new SolidColorBrush(color);
                    return;
                }

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
