using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Scrcap.Windows.UI.Resources;

public static class ChromeWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
        window.Deactivated += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var cornerPreference = DwmwcpRound;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        if (TryGetColor(window, "ColorWindowFrame", out var color))
        {
            var colorRef = ColorRef(color);
            _ = DwmSetWindowAttribute(
                hwnd,
                DwmwaBorderColor,
                ref colorRef,
                Marshal.SizeOf<int>());
        }
    }

    private static bool TryGetColor(FrameworkElement element, string key, out WpfColor color)
    {
        if (element.TryFindResource(key) is WpfColor found)
        {
            color = found;
            return true;
        }

        color = default;
        return false;
    }

    private static int ColorRef(WpfColor color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
