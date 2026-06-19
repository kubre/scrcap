using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
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
    [InlineData(600)]
    [InlineData(720)]
    [InlineData(980)]
    [InlineData(1200)]
    public void EditorRendersWithoutToolbarClippingAtRequiredWidths(int width)
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var settings = BaselineSettings(ThemeMode.Light);
            var window = new EditorWindow(CaptureResultForEditor(), settings);
            var viewModel = (EditorViewModel)window.DataContext;
            viewModel.ActiveTool = EditorTool.Text;
            viewModel.ColorIndex = 4;
            viewModel.ActiveSize = ShapeSize.Large;

            var bitmap = VisualBaseline.RenderWindow(window, width, 560);

            Assert.Equal(width, bitmap.PixelWidth);
            Assert.Equal(560, bitmap.PixelHeight);
        });
    }

    [Fact]
    public void EditorToolbarItemsStayInsideSixHundredDipWindowWhenTextToolIsActive()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var window = new EditorWindow(CaptureResultForEditor(), BaselineSettings(ThemeMode.Light))
            {
                Width = 600,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
            };
            var viewModel = (EditorViewModel)window.DataContext;
            viewModel.ActiveTool = EditorTool.Text;
            viewModel.ColorIndex = 4;
            viewModel.ActiveSize = ShapeSize.Large;
            window.Show();
            DrainDispatcher();

            foreach (var automationId in ToolbarAutomationIds())
            {
                var element = FindByAutomationId<FrameworkElement>(window, automationId);
                Assert.NotNull(element);
                Assert.True(element!.ActualWidth > 0, $"{automationId} should be visible.");
                var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                Assert.InRange(bounds.Left, 0, window.ActualWidth);
                Assert.InRange(bounds.Right, 0, window.ActualWidth);
            }

            window.Close();
            DrainDispatcher();
        });
    }

    [Fact]
    public void EditorToolbarSelectedStatesRenderActiveBackgrounds()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var window = new EditorWindow(CaptureResultForEditor(), BaselineSettings(ThemeMode.Light))
            {
                Width = 860,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
            };
            var viewModel = (EditorViewModel)window.DataContext;
            window.Show();
            DrainDispatcher();

            foreach (var (tool, automationId) in new[]
                     {
                         (EditorTool.Arrow, "ToolArrow"),
                         (EditorTool.Rectangle, "ToolRectangle"),
                         (EditorTool.Counter, "ToolCounter"),
                         (EditorTool.Text, "ToolText"),
                         (EditorTool.Pixelate, "ToolPixelate"),
                         (EditorTool.Crop, "ToolCrop"),
                     })
            {
                viewModel.ActiveTool = tool;
                DrainDispatcher();
                AssertToolbarState(window, automationId, ToolAutomationIds());
            }

            for (var index = 0; index < 5; index++)
            {
                viewModel.ColorIndex = index;
                DrainDispatcher();
                AssertToolbarState(window, $"Color{index + 1}", ColorAutomationIds());
            }

            foreach (var (size, automationId) in new[]
                     {
                         (ShapeSize.Small, "SizeSmall"),
                         (ShapeSize.Medium, "SizeMedium"),
                         (ShapeSize.Large, "SizeLarge"),
                     })
            {
                viewModel.ActiveSize = size;
                DrainDispatcher();
                AssertToolbarState(window, automationId, SizeAutomationIds());
            }

            window.Close();
            DrainDispatcher();
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

    [Theory]
    [InlineData(ThemeMode.Light, 0, "preferences-general-light")]
    [InlineData(ThemeMode.Dark, 0, "preferences-general-dark")]
    [InlineData(ThemeMode.Light, 1, "preferences-capture-light")]
    [InlineData(ThemeMode.Dark, 1, "preferences-capture-dark")]
    [InlineData(ThemeMode.Light, 2, "preferences-shortcuts-light")]
    [InlineData(ThemeMode.Dark, 2, "preferences-shortcuts-dark")]
    [InlineData(ThemeMode.Light, 3, "preferences-editor-light")]
    [InlineData(ThemeMode.Dark, 3, "preferences-editor-dark")]
    [InlineData(ThemeMode.Light, 4, "preferences-output-light")]
    [InlineData(ThemeMode.Dark, 4, "preferences-output-dark")]
    [InlineData(ThemeMode.Light, 5, "preferences-about-light")]
    [InlineData(ThemeMode.Dark, 5, "preferences-about-dark")]
    public void PreferencesTabsMatchVisualBaselines(ThemeMode theme, int tabIndex, string baselineName)
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(theme);
            using var temp = new TemporarySettingsDirectory();
            var settings = BaselineSettings(theme);
            settings.SaveFolder = @"C:\Users\Example\Pictures\scrcap";
            var store = new SettingsStore(temp.Path);
            Assert.True(store.Update(next => CopySettings(settings, next)));
            var window = new PreferencesWindow(store, tabIndex);

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

            var bitmap = VisualBaseline.RenderWindow(hud, 244, 64);
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
        viewModel.CommitShape(new CorePoint(74, 196), new CorePoint(74, 196), "Baseline");
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
        var root = new Canvas
        {
            Width = 640,
            Height = 360,
            Background = Brushes.Transparent,
        };

        var selectionRect = new Rect(146, 92, 330, 184);
        var dimMask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dimMask.Children.Add(new RectangleGeometry(new Rect(0, 0, 640, 360)));
        dimMask.Children.Add(new RectangleGeometry(selectionRect));
        root.Children.Add(new System.Windows.Shapes.Path
        {
            Data = dimMask,
            Fill = (Brush)Application.Current.FindResource("BrushOverlayDim"),
        });

        AddHint(root, 18, 18);
        AddLine(root, 320, 0, 320, 360);
        AddLine(root, 0, 180, 640, 180);
        AddSelectionAnts(root, selectionRect);
        AddText(root, "330 \u00d7 184", 484, 50, "BrushTagBackground", "BrushTagText", new Thickness(10, 5, 10, 5), "Consolas");
        AddMoveHint(root, 146, 284);
        AddText(root, "2/4  Example window  330 \u00d7 184", 188, 286, "BrushTagBackground", "BrushTagText", new Thickness(10, 6, 10, 6));
        return root;
    }

    private static void AddSelectionAnts(Canvas canvas, Rect rect)
    {
        AddAnt(canvas, rect, Brushes.Black, 3, [4, 4]);
        AddAnt(canvas, rect, Brushes.White, 1, [4, 4]);
        AddAnt(canvas, rect, (Brush)Application.Current.FindResource("BrushAccent"), 2, [8, 6]);
    }

    private static void AddAnt(Canvas canvas, Rect rect, Brush stroke, double thickness, DoubleCollection dash)
    {
        var ant = new System.Windows.Shapes.Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeDashArray = [6, 4],
            Fill = Brushes.Transparent,
        };
        ant.StrokeDashArray = dash;
        Canvas.SetLeft(ant, rect.X);
        Canvas.SetTop(ant, rect.Y);
        canvas.Children.Add(ant);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = (Brush)Application.Current.FindResource("BrushTagRule"),
            StrokeThickness = 1,
        });
    }

    private static void AddHint(Canvas canvas, double left, double top)
    {
        var text = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("BrushTagText"),
            FontFamily = (FontFamily)Application.Current.FindResource("FontUi"),
            FontSize = 11.5,
        };
        text.Inlines.Add("Drag a region. ");
        text.Inlines.Add(Keycap("Space"));
        text.Inlines.Add(" move  ");
        text.Inlines.Add(Keycap("Esc"));
        text.Inlines.Add(" cancel");
        AddInlineTag(canvas, text, left, top, new Thickness(10, 6, 10, 6));
    }

    private static void AddMoveHint(Canvas canvas, double left, double top)
    {
        var text = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("BrushTagText"),
            FontFamily = (FontFamily)Application.Current.FindResource("FontUi"),
            FontSize = 11.5,
        };
        text.Inlines.Add(Keycap("Space"));
        text.Inlines.Add(" move  ");
        text.Inlines.Add(Keycap("Esc"));
        text.Inlines.Add(" cancel  Spans 2 displays");
        AddInlineTag(canvas, text, left, top, new Thickness(10, 5, 10, 5));
    }

    private static InlineUIContainer Keycap(string text) =>
        new(new Border
        {
            Background = (Brush)Application.Current.FindResource("BrushAccent"),
            BorderBrush = (Brush)Application.Current.FindResource("BrushAccentDeep"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = (Brush)Application.Current.FindResource("BrushOnAccent"),
                FontFamily = (FontFamily)Application.Current.FindResource("FontMono"),
                FontSize = 11.5,
            },
        });

    private static void AddInlineTag(Canvas canvas, TextBlock text, double left, double top, Thickness padding)
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.FindResource("BrushTagBackground"),
            BorderBrush = (Brush)Application.Current.FindResource("BrushTagRule"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = padding,
            Child = text,
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        canvas.Children.Add(border);
    }

    private static void AddText(Canvas canvas, string text, double left, double top, string backgroundKey, string foregroundKey, Thickness padding, string fontFamily = "Segoe UI")
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.FindResource(backgroundKey),
            BorderBrush = (Brush)Application.Current.FindResource("BrushTagRule"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
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

    private static IReadOnlyList<string> ToolbarAutomationIds() =>
        ToolAutomationIds()
            .Concat(ColorAutomationIds())
            .Concat(SizeAutomationIds())
            .ToArray();

    private static IReadOnlyList<string> ToolAutomationIds() =>
        ["ToolArrow", "ToolRectangle", "ToolCounter", "ToolText", "ToolPixelate", "ToolCrop"];

    private static IReadOnlyList<string> ColorAutomationIds() =>
        ["Color1", "Color2", "Color3", "Color4", "Color5"];

    private static IReadOnlyList<string> SizeAutomationIds() =>
        ["SizeSmall", "SizeMedium", "SizeLarge"];

    private static void AssertToolbarState(Window window, string activeId, IReadOnlyList<string> groupIds)
    {
        var activeBrush = (SolidColorBrush)Application.Current.FindResource("BrushActiveWash");
        foreach (var automationId in groupIds)
        {
            var button = FindByAutomationId<Button>(window, automationId);
            Assert.NotNull(button);
            var chrome = FindDescendant<Border>(button!);
            Assert.NotNull(chrome);
            var brush = Assert.IsType<SolidColorBrush>(chrome!.Background);
            if (automationId == activeId)
            {
                Assert.Equal(activeBrush.Color, brush.Color);
            }
            else
            {
                Assert.Equal(Colors.Transparent, brush.Color);
            }
        }
    }

    private static T? FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        if (root is T element && AutomationProperties.GetAutomationId(element) == automationId)
        {
            return element;
        }

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            if (FindByAutomationId<T>(VisualTreeHelper.GetChild(root, index), automationId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T element)
        {
            return element;
        }

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, index)) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static void DrainDispatcher()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
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
