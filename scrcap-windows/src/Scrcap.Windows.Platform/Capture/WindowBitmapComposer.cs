using System.Drawing;
using System.Drawing.Imaging;

namespace Scrcap.Windows.Platform.Capture;

internal static class WindowBitmapComposer
{
    public const int ShadowPadding = 20;

    public static Bitmap Compose(Bitmap bitmap, CaptureRequest request)
    {
        if (request.WindowBackgroundTransparent && !request.IncludeWindowShadow)
        {
            return CloneBitmap(bitmap);
        }

        var pad = request.IncludeWindowShadow ? ShadowPadding : 0;
        var composed = new Bitmap(bitmap.Width + pad * 2, bitmap.Height + pad * 2, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(composed);
        graphics.Clear(ColorTranslator.FromHtml(request.WindowBackgroundHex));
        if (request.IncludeWindowShadow)
        {
            using var shadow = new SolidBrush(Color.FromArgb(42, 0, 0, 0));
            graphics.FillRectangle(shadow, pad / 2, pad / 2, bitmap.Width + pad, bitmap.Height + pad);
        }

        graphics.DrawImageUnscaled(bitmap, pad, pad);
        return composed;
    }

    private static Bitmap CloneBitmap(Bitmap bitmap)
    {
        var clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        return clone;
    }
}
