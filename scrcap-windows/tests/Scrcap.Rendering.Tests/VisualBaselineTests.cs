using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Editor;
using Scrcap.Windows.UI.Overlay;
using Scrcap.Windows.UI.Preferences;
using WpfColor = System.Windows.Media.Color;

namespace Scrcap.Rendering.Tests;

public sealed class VisualBaselineTests
{
    [Theory]
    [InlineData(ThemeMode.Light, "editor-light")]
    [InlineData(ThemeMode.Dark, "editor-dark")]
    public void EditorMatchesVisualBaseline(ThemeMode theme, string baselineName)
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(theme);
            var settings = BaselineSettings(theme);
            var window = new EditorWindow(CaptureResultForEditor(), settings);
            var viewModel = (EditorViewModel)window.DataContext;
            AddEditorAnnotations(viewModel);

            var bitmap = VisualBaseline.RenderWindow(window, 860, 560);
            VisualBaseline.AssertMatches(baselineName, bitmap);
        });
    }

    [Theory]
    [InlineData(ThemeMode.Light, "preferences-light")]
    [InlineData(ThemeMode.Dark, "preferences-dark")]
    public void PreferencesMatchesVisualBaseline(ThemeMode theme, string baselineName)
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(theme);
            using var temp = new TemporarySettingsDirectory();
            var settings = BaselineSettings(theme);
            settings.SaveFolder = @"C:\Users\Example\Pictures\scrcap";
            var store = new SettingsStore(temp.Path);
            Assert.True(store.Update(next => CopySettings(settings, next)));
            var window = new PreferencesWindow(store);

            var bitmap = VisualBaseline.RenderWindow(window, 720, 540);
            VisualBaseline.AssertMatches(baselineName, bitmap);
        });
    }

    [Fact]
    public void OverlayTagsMatchVisualBaseline()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var bitmap = VisualBaseline.RenderElement(CreateOverlayFixture(), 640, 360);
            VisualBaseline.AssertMatches("overlay-tags", bitmap);
        });
    }

    [Fact]
    public void ScrollingHudMatchesVisualBaseline()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Dark);
            var hud = new ScrollingCaptureHud(() => { });
            hud.UpdateProgress(new ScrollingCaptureProgress(3, 1280, 2400));

            var bitmap = VisualBaseline.RenderWindow(hud, 220, 64);
            VisualBaseline.AssertMatches("scrolling-hud", bitmap);
        });
    }

    private static CaptureResult CaptureResultForEditor()
    {
        var source = SampleBitmap(560, 320);
        var metadata = new CaptureMetadata(CaptureMode.Region, "baseline-sample.png", new PixelRect(12, 24, source.PixelWidth, source.PixelHeight), DateTimeOffset.UnixEpoch);
        return new CaptureResult(EditorWindow.CapturedPixelsFromBitmapSource(source, metadata), metadata);
    }

    private static void AddEditorAnnotations(EditorViewModel viewModel)
    {
        viewModel.ActiveTool = EditorTool.Rectangle;
        viewModel.ColorIndex = 0;
        viewModel.ActiveSize = ShapeSize.Medium;
        viewModel.CommitShape(new CorePoint(54, 42), new CorePoint(256, 148));

        viewModel.ActiveTool = EditorTool.Arrow;
        viewModel.ColorIndex = 1;
        viewModel.CommitShape(new CorePoint(304, 236), new CorePoint(468, 108));

        viewModel.ActiveTool = EditorTool.Counter;
        viewModel.ColorIndex = 2;
        viewModel.CommitShape(new CorePoint(412, 230), new CorePoint(412, 230));

        viewModel.ActiveTool = EditorTool.Text;
        viewModel.ColorIndex = 3;
        viewModel.PendingText = "Baseline";
        viewModel.CommitShape(new CorePoint(74, 196), new CorePoint(74, 196));
    }

    private static Settings BaselineSettings(ThemeMode theme)
    {
        var settings = Settings.Defaults();
        settings.ThemeMode = theme;
        settings.PaletteHex = ["#E30E20", "#18A058", "#1B64D8", "#111111", "#C42BC7"];
        settings.StrokeWidth = 3;
        settings.TextSize = 22;
        return settings;
    }

    private static void CopySettings(Settings source, Settings target)
    {
        target.ThemeMode = source.ThemeMode;
        target.SaveFolder = source.SaveFolder;
        target.PaletteHex = [.. source.PaletteHex];
        target.StrokeWidth = source.StrokeWidth;
        target.TextSize = source.TextSize;
    }

    private static BitmapSource SampleBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var lane = x / 80;
                var isGrid = x % 40 == 0 || y % 40 == 0;
                pixels[offset] = isGrid ? (byte)205 : (byte)(235 - lane * 7);
                pixels[offset + 1] = isGrid ? (byte)205 : (byte)(232 - (y / 32) * 3);
                pixels[offset + 2] = isGrid ? (byte)205 : (byte)(230 - lane * 5);
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static FrameworkElement CreateOverlayFixture()
    {
        var root = new Grid
        {
            Background = (Brush)Application.Current.FindResource("BrushOverlayDim"),
        };
        var canvas = new Canvas();
        root.Children.Add(canvas);

        AddText(canvas, "Drag to capture a region. Press Esc to cancel.", 18, 18, "BrushPanel", "BrushInk", new Thickness(10, 6, 10, 6));
        AddLine(canvas, 320, 0, 320, 360);
        AddLine(canvas, 0, 180, 640, 180);

        var selection = new System.Windows.Shapes.Rectangle
        {
            Width = 330,
            Height = 184,
            Stroke = (Brush)Application.Current.FindResource("BrushAccent"),
            StrokeThickness = 2,
            StrokeDashArray = [6, 4],
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(selection, 146);
        Canvas.SetTop(selection, 92);
        canvas.Children.Add(selection);

        AddText(canvas, "330 x 184", 484, 284, "BrushAccent", "BrushOnAccent", new Thickness(6, 3, 6, 3), "Consolas");
        AddText(canvas, "Spans 2 displays", 146, 56, "BrushPanel", "BrushInk", new Thickness(6, 3, 6, 3));
        AddText(canvas, "Example window", 188, 286, "BrushAccent", "BrushOnAccent", new Thickness(8, 4, 8, 4));
        return root;
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = (Brush)Application.Current.FindResource("BrushAccent"),
            StrokeThickness = 1,
        });
    }

    private static void AddText(Canvas canvas, string text, double left, double top, string backgroundKey, string foregroundKey, Thickness padding, string fontFamily = "Segoe UI")
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.FindResource(backgroundKey),
            CornerRadius = new CornerRadius(4),
            Padding = padding,
            Child = new TextBlock
            {
                Text = text,
                Foreground = (Brush)Application.Current.FindResource(foregroundKey),
                FontFamily = new FontFamily(fontFamily),
                FontSize = 12,
            },
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        canvas.Children.Add(border);
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-rendering-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
