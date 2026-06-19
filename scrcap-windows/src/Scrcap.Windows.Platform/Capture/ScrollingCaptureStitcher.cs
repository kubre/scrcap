using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Scrcap.Core;

namespace Scrcap.Windows.Platform.Capture;

internal sealed record ScrollingStitchOptions(int MaxHeight, int StickyHeaderMaxPixels = 160, int BottomProbeFrames = 2);

internal sealed record ScrollingStitchResult(Bitmap Bitmap, ScrollingCaptureStopReason StopReason, int ConsumedFrames) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

internal static class ScrollingCaptureStitcher
{
    public static ScrollingStitchResult Stitch(IReadOnlyList<Bitmap> frames, ScrollingStitchOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        var retained = new List<Bitmap>();
        var accumulatedHashes = new List<ulong>();
        IReadOnlyList<ulong>? firstFrameHashes = null;
        IReadOnlyList<ulong>? previousFrameHashes = null;
        var bottomProbeCount = 0;
        var stopReason = ScrollingCaptureStopReason.Completed;
        var width = frames[0].Width;

        try
        {
            foreach (var source in frames)
            {
                if (TotalHeight(retained) >= options.MaxHeight)
                {
                    stopReason = ScrollingCaptureStopReason.MaxHeightReached;
                    break;
                }

                if (source.Width != width)
                {
                    stopReason = ScrollingCaptureStopReason.AlignmentFailed;
                    break;
                }

                var hashes = RowHashes(source);
                if (retained.Count == 0)
                {
                    retained.Add(CloneBitmap(source));
                    firstFrameHashes = hashes;
                    previousFrameHashes = hashes;
                    accumulatedHashes.AddRange(hashes);
                    continue;
                }

                var stickyRows = ScrollingFramePlanner.DetectStickyHeaderRows(
                    firstFrameHashes ?? [],
                    hashes,
                    options.StickyHeaderMaxPixels);
                var newContentStart = ScrollingFramePlanner.FindNewContentStart(accumulatedHashes, hashes, stickyRows);
                if (newContentStart is null)
                {
                    stopReason = ScrollingCaptureStopReason.AlignmentFailed;
                    break;
                }

                var noNewRows = newContentStart.Value >= source.Height;
                if (!noNewRows)
                {
                    var top = Math.Clamp(newContentStart.Value, 0, source.Height - 1);
                    retained.Add(CropBitmap(source, new Rectangle(0, top, source.Width, source.Height - top)));
                    accumulatedHashes.AddRange(hashes.Skip(top));
                    bottomProbeCount = 0;
                }

                var repeatedFrame = previousFrameHashes is not null && ScrollingFramePlanner.RowsEqual(previousFrameHashes, hashes);
                if (noNewRows || repeatedFrame)
                {
                    bottomProbeCount++;
                }

                previousFrameHashes = hashes;
                if (bottomProbeCount >= options.BottomProbeFrames)
                {
                    stopReason = ScrollingCaptureStopReason.BottomReached;
                    break;
                }
            }

            if (stopReason == ScrollingCaptureStopReason.Completed && TotalHeight(retained) >= options.MaxHeight)
            {
                stopReason = ScrollingCaptureStopReason.MaxHeightReached;
            }

            var stitched = StitchFrames(retained, width, Math.Min(options.MaxHeight, TotalHeight(retained)));
            return new ScrollingStitchResult(stitched, stopReason, retained.Count);
        }
        finally
        {
            foreach (var bitmap in retained)
            {
                bitmap.Dispose();
            }
        }
    }

    private static IReadOnlyList<ulong> RowHashes(Bitmap bitmap)
    {
        var hashes = new ulong[bitmap.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                hashes[y] = StitchEngine.RowHash(row.AsSpan(0, bitmap.Width * 4));
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return hashes;
    }

    private static Bitmap StitchFrames(IReadOnlyList<Bitmap> frames, int width, int maxHeight)
    {
        var stitched = new Bitmap(width, maxHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(stitched);
        var y = 0;
        foreach (var frame in frames)
        {
            var remaining = maxHeight - y;
            if (remaining <= 0)
            {
                break;
            }

            var sourceHeight = Math.Min(frame.Height, remaining);
            graphics.DrawImage(frame, new Rectangle(0, y, width, sourceHeight), new Rectangle(0, 0, width, sourceHeight), GraphicsUnit.Pixel);
            y += sourceHeight;
        }

        return stitched;
    }

    private static Bitmap CropBitmap(Bitmap bitmap, Rectangle rect)
    {
        var clone = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(bitmap, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return clone;
    }

    private static Bitmap CloneBitmap(Bitmap bitmap)
    {
        var clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        return clone;
    }

    private static int TotalHeight(IEnumerable<Bitmap> frames) => frames.Sum(frame => frame.Height);
}
