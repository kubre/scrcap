using System.Windows;
using System.Windows.Input;
using Scrcap.Windows.Platform.Capture;

namespace Scrcap.Windows.UI.Overlay;

public partial class ScrollingCaptureHud : Window
{
    private readonly Action cancel;

    public ScrollingCaptureHud(Action cancel)
    {
        this.cancel = cancel;
        InitializeComponent();
    }

    public static ScrollingCaptureHud ShowFor(PixelRect region, Action cancel)
    {
        var hud = new ScrollingCaptureHud(cancel);
        hud.Left = Math.Max(SystemParameters.VirtualScreenLeft, region.Right - hud.Width - 18);
        hud.Top = Math.Max(SystemParameters.VirtualScreenTop, region.Y + 18);
        hud.Show();
        hud.Activate();
        return hud;
    }

    public void UpdateProgress(ScrollingCaptureProgress progress)
    {
        var frameWord = progress.FrameCount == 1 ? "frame" : "frames";
        ProgressText.Text = $"{progress.FrameCount} {frameWord} - {progress.PixelHeight}/{progress.MaxPixelHeight}px";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        cancel();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            cancel();
            e.Handled = true;
        }
    }
}
