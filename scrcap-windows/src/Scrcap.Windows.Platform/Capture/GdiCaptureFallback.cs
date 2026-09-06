using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Scrcap.Windows.Platform.Capture;

internal sealed class GdiCaptureFallback
{
    public Bitmap CaptureScreenRect(PixelRect rect, bool includeCursor)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Capture rectangle must have positive dimensions.");
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not acquire screen device context.");
        }

        var memory = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var oldObject = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) { throw new InvalidOperationException("CreateCompatibleDC failed for fallback capture."); }
            bitmapHandle = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
            if (bitmapHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateCompatibleBitmap failed for {rect.Width} x {rect.Height} capture.");
            }
            oldObject = SelectObject(memory, bitmapHandle);
            if (oldObject == IntPtr.Zero || oldObject == new IntPtr(-1))
            {
                throw new InvalidOperationException("SelectObject failed for fallback capture.");
            }
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, Srccopy))
            {
                throw new InvalidOperationException("Fallback screen capture failed.");
            }

            using var captured = Image.FromHbitmap(bitmapHandle);
            var clone = new Bitmap(captured.Width, captured.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var graphics = Graphics.FromImage(clone);
                graphics.DrawImageUnscaled(captured, 0, 0);
                if (includeCursor) { DrawCursor(graphics, rect); }
                return clone;
            }
            catch
            {
                clone.Dispose();
                throw;
            }
        }
        finally
        {
            if (oldObject != IntPtr.Zero && oldObject != new IntPtr(-1)) { SelectObject(memory, oldObject); }
            if (bitmapHandle != IntPtr.Zero) { DeleteObject(bitmapHandle); }
            if (memory != IntPtr.Zero) { DeleteDC(memory); }
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static void DrawCursor(Graphics graphics, PixelRect rect)
    {
        var cursor = Cursor.Current ?? Cursors.Default;
        var position = Cursor.Position;
        var target = new Rectangle(position.X - rect.X, position.Y - rect.Y, cursor.Size.Width, cursor.Size.Height);
        if (target.IntersectsWith(new Rectangle(0, 0, rect.Width, rect.Height)))
        {
            cursor.Draw(graphics, target);
        }
    }

    private const int Srccopy = 0x00CC0020;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
}
