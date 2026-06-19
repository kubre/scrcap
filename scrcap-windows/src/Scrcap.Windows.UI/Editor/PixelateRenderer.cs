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

    public BitmapSource Render(BitmapSource sourceBitmap, Int32Rect sourceBounds, int blockSize, double exportScale)
    {
        if (!ReferenceEquals(source, sourceBitmap))
        {
            source = sourceBitmap;
            sourceVersion++;
            cache.Clear();
        }

        var key = new PixelateCacheKey(sourceVersion, sourceBounds, Math.Max(1, blockSize), exportScale);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bitmap = CreatePixelatedBitmap(sourceBitmap, sourceBounds, key.BlockSize);
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

    private static BitmapSource CreatePixelatedBitmap(BitmapSource sourceBitmap, Int32Rect sourceBounds, int blockSize)
    {
        var cropped = new CroppedBitmap(sourceBitmap, sourceBounds);
        BitmapSource source = cropped.Format == PixelFormats.Bgra32
            ? cropped
            : new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
        var stride = sourceBounds.Width * 4;
        var sourcePixels = new byte[stride * sourceBounds.Height];
        var outputPixels = new byte[sourcePixels.Length];
        source.CopyPixels(sourcePixels, stride, 0);

        for (var blockY = 0; blockY < sourceBounds.Height; blockY += blockSize)
        {
            for (var blockX = 0; blockX < sourceBounds.Width; blockX += blockSize)
            {
                var sampleOffset = (blockY * stride) + (blockX * 4);
                var blockRight = Math.Min(blockX + blockSize, sourceBounds.Width);
                var blockBottom = Math.Min(blockY + blockSize, sourceBounds.Height);

                for (var y = blockY; y < blockBottom; y++)
                {
                    for (var x = blockX; x < blockRight; x++)
                    {
                        var offset = (y * stride) + (x * 4);
                        outputPixels[offset] = sourcePixels[sampleOffset];
                        outputPixels[offset + 1] = sourcePixels[sampleOffset + 1];
                        outputPixels[offset + 2] = sourcePixels[sampleOffset + 2];
                        outputPixels[offset + 3] = sourcePixels[sampleOffset + 3];
                    }
                }
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
    int BlockSize,
    double ExportScale);
