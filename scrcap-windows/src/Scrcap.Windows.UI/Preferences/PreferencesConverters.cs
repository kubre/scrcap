using System.Globalization;
using Scrcap.Core;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Scrcap.Windows.UI.Preferences;

public sealed class HexColorBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value as string ?? "#FFFFFF";
        return Settings.NormalizeHexColor(hex) is { } normalized
            ? new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(normalized))
            : WpfBrushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}

public sealed class InverseBooleanConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

public sealed record PreferenceOption<T>(T Value, string Text)
{
    public override string ToString() => Text;
}
