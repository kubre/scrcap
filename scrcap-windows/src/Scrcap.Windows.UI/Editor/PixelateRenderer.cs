using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scrcap.Windows.UI.Editor;

internal sealed class PixelateRenderer
{
    private const int MaxCacheEntries = 24;

    private readonly Dictionary<PixelateCacheKey, BitmapSource> cache = [];
    private BitmapSource? source;
    private long sourceVersion;

    public void Clear()
    {
        source = null;
        sourceVersion++;
        cache.Clear();
    }

    public BitmapSource Render(BitmapSource sourceBitmap, Int32Rect sourceBounds, int columns, int rows, double exportScale, out bool cacheHit)
    {
        if (!ReferenceEquals(source, sourceBitmap))
        {
            source = sourceBitmap;
            sourceVersion++;
            cache.Clear();
        }

        var key = new PixelateCacheKey(sourceVersion, sourceBounds, Math.Max(1, columns), Math.Max(1, rows), exportScale);
        if (cache.TryGetValue(key, out var cached))
        {
            cacheHit = true;
            return cached;
        }

        cacheHit = false;
        var bitmap = CreatePixelatedBitmap(sourceBitmap, sourceBounds, key.Columns, key.Rows);
        if (cache.Count >= MaxCacheEntries)
        {
            cache.Clear();
        }

        cache[key] = bitmap;
        return bitmap;
    }

    public static bool TryGetPixelBounds(Rect rect, BitmapSource source, out Int32Rect bounds)
    {
        var left = (int)Math.Floor(Math.Clamp(rect.Left, 0, source.PixelWidth));
        var top = (int)Math.Floor(Math.Clamp(rect.Top, 0, source.PixelHeight));
        var right = (int)Math.Ceiling(Math.Clamp(rect.Right, 0, source.PixelWidth));
        var bottom = (int)Math.Ceiling(Math.Clamp(rect.Bottom, 0, source.PixelHeight));

        if (right <= left || bottom <= top)
        {
            bounds = default;
            return false;
        }

        bounds = new Int32Rect(left, top, right - left, bottom - top);
        return true;
    }

    private static BitmapSource CreatePixelatedBitmap(BitmapSource sourceBitmap, Int32Rect sourceBounds, int columns, int rows)
    {
        var cropped = new CroppedBitmap(sourceBitmap, sourceBounds);
        BitmapSource source = cropped.Format == PixelFormats.Bgra32
            ? cropped
            : new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
        var stride = sourceBounds.Width * 4;
        var sourcePixels = new byte[stride * sourceBounds.Height];
        var outputPixels = new byte[sourcePixels.Length];
        source.CopyPixels(sourcePixels, stride, 0);

        var lowRes = new byte[Math.Max(1, columns) * Math.Max(1, rows) * 4];
        for (var row = 0; row < rows; row++)
        {
            var y0 = row * sourceBounds.Height / rows;
            var y1 = Math.Max(y0 + 1, (row + 1) * sourceBounds.Height / rows);
            for (var column = 0; column < columns; column++)
            {
                var x0 = column * sourceBounds.Width / columns;
                var x1 = Math.Max(x0 + 1, (column + 1) * sourceBounds.Width / columns);
                long blue = 0;
                long green = 0;
                long red = 0;
                long alpha = 0;
                var count = 0;

                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var offset = (y * stride) + (x * 4);
                        blue += sourcePixels[offset];
                        green += sourcePixels[offset + 1];
                        red += sourcePixels[offset + 2];
                        alpha += sourcePixels[offset + 3];
                        count++;
                    }
                }

                var lowOffset = ((row * columns) + column) * 4;
                lowRes[lowOffset] = (byte)(blue / count);
                lowRes[lowOffset + 1] = (byte)(green / count);
                lowRes[lowOffset + 2] = (byte)(red / count);
                lowRes[lowOffset + 3] = (byte)(alpha / count);
            }
        }

        for (var y = 0; y < sourceBounds.Height; y++)
        {
            var lowY = Math.Min(rows - 1, y * rows / sourceBounds.Height);
            for (var x = 0; x < sourceBounds.Width; x++)
            {
                var lowX = Math.Min(columns - 1, x * columns / sourceBounds.Width);
                var sourceOffset = ((lowY * columns) + lowX) * 4;
                var targetOffset = (y * stride) + (x * 4);
                outputPixels[targetOffset] = lowRes[sourceOffset];
                outputPixels[targetOffset + 1] = lowRes[sourceOffset + 1];
                outputPixels[targetOffset + 2] = lowRes[sourceOffset + 2];
                outputPixels[targetOffset + 3] = lowRes[sourceOffset + 3];
            }
        }

        var bitmap = BitmapSource.Create(sourceBounds.Width, sourceBounds.Height, 96, 96, PixelFormats.Bgra32, null, outputPixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}

internal readonly record struct PixelateCacheKey(
    long SourceVersion,
    Int32Rect SourceBounds,
    int Columns,
    int Rows,
    double ExportScale);
