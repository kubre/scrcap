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

        var memory = CreateCompatibleDC(screen);
        var bitmapHandle = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        var oldObject = SelectObject(memory, bitmapHandle);

        try
        {
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, Srccopy))
            {
                throw new InvalidOperationException("Fallback screen capture failed.");
            }

            using var captured = Image.FromHbitmap(bitmapHandle);
            var clone = new Bitmap(captured.Width, captured.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(clone))
            {
                graphics.DrawImageUnscaled(captured, 0, 0);
                if (includeCursor)
                {
                    DrawCursor(graphics, rect);
                }
            }

            return clone;
        }
        finally
        {
            SelectObject(memory, oldObject);
            DeleteObject(bitmapHandle);
            DeleteDC(memory);
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
