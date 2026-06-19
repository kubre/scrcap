using System.Drawing;
using System.Windows.Forms;

namespace Scrcap.CaptureFixture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(args.Contains("--scroll-fixture", StringComparer.OrdinalIgnoreCase)
            ? new ScrollFixtureForm()
            : new CaptureFixtureForm());
    }
}

internal sealed class CaptureFixtureForm : Form
{
    public CaptureFixtureForm()
    {
        Text = "scrcap deterministic capture fixture";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(80, 80);
        ClientSize = new Size(240, 180);
        TopMost = true;
        ShowInTaskbar = false;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Console.WriteLine($"SCRCAP_FIXTURE_HWND={Handle.ToInt64()}");
        Console.WriteLine($"SCRCAP_FIXTURE_BOUNDS={Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}");
        Console.Out.Flush();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Color.Cyan);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var red = new SolidBrush(Color.Red);
        using var green = new SolidBrush(Color.Lime);
        using var blue = new SolidBrush(Color.Blue);
        using var yellow = new SolidBrush(Color.Yellow);
        e.Graphics.FillRectangle(red, 0, 0, 24, 24);
        e.Graphics.FillRectangle(green, ClientSize.Width - 24, 0, 24, 24);
        e.Graphics.FillRectangle(blue, 0, ClientSize.Height - 24, 24, 24);
        e.Graphics.FillRectangle(yellow, ClientSize.Width - 24, ClientSize.Height - 24, 24, 24);
    }
}

internal sealed class ScrollFixtureForm : Form
{
    private readonly Panel scrollPanel = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
    };

    public ScrollFixtureForm()
    {
        Text = "scrcap deterministic scroll fixture";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(120, 120);
        ClientSize = new Size(260, 190);
        TopMost = true;
        ShowInTaskbar = false;
        var content = new ScrollRowsControl
        {
            Location = new Point(0, 0),
            Size = new Size(240, 720),
        };
        scrollPanel.Controls.Add(content);
        Controls.Add(scrollPanel);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Console.WriteLine($"SCRCAP_FIXTURE_HWND={Handle.ToInt64()}");
        Console.WriteLine($"SCRCAP_SCROLL_PANEL_HWND={scrollPanel.Handle.ToInt64()}");
        Console.WriteLine("SCRCAP_SCROLL_ROWS=24");
        Console.Out.Flush();
    }

    private sealed class ScrollRowsControl : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            for (var row = 0; row < 24; row++)
            {
                using var brush = new SolidBrush(Color.FromArgb(255, row * 9, 255 - row * 7, 40 + row * 5));
                e.Graphics.FillRectangle(brush, 0, row * 30, Width, 30);
                TextRenderer.DrawText(e.Graphics, $"row-{row:00}", Font, new Rectangle(8, row * 30 + 6, Width - 16, 18), Color.Black);
            }
        }
    }
}
