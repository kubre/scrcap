using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Scrcap.Core;

namespace Scrcap.Windows.UI.Editor;

public readonly record struct PixelAlignedCrop(Int32Rect Pixels, CoreRect Logical);

public readonly record struct PixelAlignedCanvasExpansion(
    int LeftPixels,
    int TopPixels,
    int RightPixels,
    int BottomPixels,
    CanvasExpansion Logical);

public static class EditorRasterGeometry
{
    public static PixelAlignedCrop AlignCrop(CoreRect crop, int pixelWidth, int pixelHeight, double pixelsPerDipX, double pixelsPerDipY)
    {
        pixelsPerDipX = NormalizeScale(pixelsPerDipX);
        pixelsPerDipY = NormalizeScale(pixelsPerDipY);
        var left = Math.Clamp((int)Math.Floor(crop.X * pixelsPerDipX), 0, Math.Max(0, pixelWidth - 1));
        var top = Math.Clamp((int)Math.Floor(crop.Y * pixelsPerDipY), 0, Math.Max(0, pixelHeight - 1));
        var right = Math.Clamp((int)Math.Ceiling(crop.MaxX * pixelsPerDipX), left + 1, pixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(crop.MaxY * pixelsPerDipY), top + 1, pixelHeight);
        var pixels = new Int32Rect(left, top, right - left, bottom - top);
        return new PixelAlignedCrop(
            pixels,
            new CoreRect(
                left / pixelsPerDipX,
                top / pixelsPerDipY,
                pixels.Width / pixelsPerDipX,
                pixels.Height / pixelsPerDipY));
    }

    public static PixelAlignedCanvasExpansion AlignExpansion(CanvasExpansion expansion, double pixelsPerDipX, double pixelsPerDipY)
    {
        pixelsPerDipX = NormalizeScale(pixelsPerDipX);
        pixelsPerDipY = NormalizeScale(pixelsPerDipY);
        var left = Pixels(expansion.Left, pixelsPerDipX);
        var top = Pixels(expansion.Top, pixelsPerDipY);
        var right = Pixels(expansion.Right, pixelsPerDipX);
        var bottom = Pixels(expansion.Bottom, pixelsPerDipY);
        return new PixelAlignedCanvasExpansion(
            left,
            top,
            right,
            bottom,
            new CanvasExpansion(
                left / pixelsPerDipX,
                top / pixelsPerDipY,
                right / pixelsPerDipX,
                bottom / pixelsPerDipY));
    }

    private static int Pixels(double dips, double scale) =>
        Math.Max(0, (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero));

    private static double NormalizeScale(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1;
}

public static class EditorClipboard
{
    public const string PngFormat = "PNG";

    public static System.Windows.DataObject CreateDataObject(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            throw new ArgumentException("PNG payload cannot be empty.", nameof(pngBytes));
        }

        var data = new System.Windows.DataObject();
        data.SetImage(EditorWindow.DecodePng(pngBytes));
        data.SetData(PngFormat, new MemoryStream(pngBytes.ToArray(), writable: false), false);
        return data;
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

public static class DragOutPayload
{
    public static string CreateTempPng(byte[] pngBytes, string filenamePattern, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        var folder = Path.Combine(Path.GetTempPath(), "scrcap-drag");
        Directory.CreateDirectory(folder);
        CleanupOldFiles(folder, now.UtcDateTime.AddHours(-24));
        var generated = FilenameGenerator.Filename(filenamePattern, now);
        var stem = Path.GetFileNameWithoutExtension(generated);
        var filename = $"{stem}-{Guid.NewGuid():N}.png";
        var path = Path.Combine(folder, filename);
        File.WriteAllBytes(path, pngBytes);
        return path;
    }

    public static System.Windows.DataObject CreateDataObject(string pngPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngPath);
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { Path.GetFullPath(pngPath) });
        return data;
    }

    public static void ScheduleCleanup(string path, TimeSpan delay) =>
        _ = CleanupAfterDelay(path, delay);

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The drop target may still hold the file briefly.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; the next editor launch removes stale files.
        }
    }

    public static void CleanupOldFiles(string folder, DateTime cutoffUtc)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task CleanupAfterDelay(string path, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        Delete(path);
    }
}
