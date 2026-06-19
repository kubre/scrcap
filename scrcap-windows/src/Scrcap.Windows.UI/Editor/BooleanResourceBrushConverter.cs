using System.Globalization;
using System.Windows.Data;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace Scrcap.Windows.UI.Editor;

public sealed class BooleanResourceBrushConverter : IValueConverter
{
    public string? TrueResourceKey { get; set; }

    public string? FalseResourceKey { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false,
        };

        var key = isTrue ? TrueResourceKey : FalseResourceKey;
        if (key is { Length: > 0 } && WpfApplication.Current?.TryFindResource(key) is WpfBrush brush)
        {
            return brush;
        }

        return WpfBrushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}
