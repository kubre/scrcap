using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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
        graphics.Clear(request.WindowBackgroundTransparent
            ? Color.Transparent
            : ColorTranslator.FromHtml(request.WindowBackgroundHex));
        if (request.IncludeWindowShadow)
        {
            using var shadow = CreateSoftShadow(bitmap.Width, bitmap.Height, pad);
            graphics.DrawImageUnscaled(shadow, 0, 0);
        }

        graphics.DrawImageUnscaled(bitmap, pad, pad);
        return composed;
    }

    private static Bitmap CreateSoftShadow(int contentWidth, int contentHeight, int padding)
    {
        var width = contentWidth + padding * 2;
        var height = contentHeight + padding * 2;
        var shadow = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = shadow.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];
            var sigma = Math.Max(1, padding / 2.5);
            var denominator = 2 * sigma * sigma;
            for (var y = 0; y < height; y++)
            {
                var dy = y < padding
                    ? padding - y
                    : y >= padding + contentHeight
                        ? y - (padding + contentHeight - 1)
                        : 0;
                for (var x = 0; x < width; x++)
                {
                    var dx = x < padding
                        ? padding - x
                        : x >= padding + contentWidth
                            ? x - (padding + contentWidth - 1)
                            : 0;
                    var distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared > padding * padding)
                    {
                        continue;
                    }

                    var alpha = (byte)Math.Clamp(
                        (int)Math.Round(58 * Math.Exp(-distanceSquared / denominator)),
                        0,
                        58);
                    pixels[y * stride + x * 4 + 3] = alpha;
                }
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            shadow.UnlockBits(data);
        }

        return shadow;
    }

    private static Bitmap CloneBitmap(Bitmap bitmap)
    {
        var clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        return clone;
    }
}
