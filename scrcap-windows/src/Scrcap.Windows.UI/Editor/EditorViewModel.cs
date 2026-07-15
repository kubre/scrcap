using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scrcap.Core;
using WpfBrush = System.Windows.Media.Brush;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Scrcap.Windows.UI.Editor;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private EditorTool activeTool = EditorTool.Arrow;
    private int colorIndex;
    private ShapeSize activeSize = ShapeSize.Small;
    private double zoom = 1;
    private double panOffsetX;
    private double panOffsetY;
    private string pendingText = "Text";
    private bool hasSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorViewModel(Settings settings)
    {
        Settings = settings;
        ColorIndex = 0;
        ActiveSize = ShapeSize.Small;
    }

    public Settings Settings { get; private set; }

    public AnnotationDocument? Document { get; private set; }

    public IReadOnlyList<Shape> VisibleShapes => Document?.Shapes ?? [];

    public EditorTool ActiveTool
    {
        get => activeTool;
        set
        {
            if (!Set(ref activeTool, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsArrowToolActive));
            OnPropertyChanged(nameof(IsRectangleToolActive));
            OnPropertyChanged(nameof(IsCounterToolActive));
            OnPropertyChanged(nameof(IsTextToolActive));
            OnPropertyChanged(nameof(IsPixelateToolActive));
            OnPropertyChanged(nameof(IsCropToolActive));
        }
    }

    public int ColorIndex
    {
        get => colorIndex;
        set
        {
            if (!Set(ref colorIndex, Math.Clamp(value, 0, Settings.PaletteSlotCount - 1)))
            {
                return;
            }

            OnPropertyChanged(nameof(ActiveColor));
            OnPropertyChanged(nameof(IsColor1Active));
            OnPropertyChanged(nameof(IsColor2Active));
            OnPropertyChanged(nameof(IsColor3Active));
            OnPropertyChanged(nameof(IsColor4Active));
            OnPropertyChanged(nameof(IsColor5Active));
        }
    }

    public ShapeSize ActiveSize
    {
        get => activeSize;
        set
        {
            if (!Set(ref activeSize, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StrokeWidth));
            OnPropertyChanged(nameof(IsSmallSizeActive));
            OnPropertyChanged(nameof(IsMediumSizeActive));
            OnPropertyChanged(nameof(IsLargeSizeActive));
        }
    }

    public double Zoom
    {
        get => zoom;
        set
        {
            var rounded = ClosestZoom(value);
            if (Set(ref zoom, rounded))
            {
                OnPropertyChanged(nameof(ZoomPercent));
            }
        }
    }

    public string ZoomPercent => $"{Math.Round(Zoom * 100):0}%";

    public double PanOffsetX
    {
        get => panOffsetX;
        private set => Set(ref panOffsetX, value);
    }

    public double PanOffsetY
    {
        get => panOffsetY;
        private set => Set(ref panOffsetY, value);
    }

    public string DoneText => Settings.EscBehavior == EscBehavior.CopyAndClose ? "Copy & Close" : "Close";

    public string PendingText
    {
        get => pendingText;
        set => Set(ref pendingText, value);
    }

    public bool HasSource
    {
        get => hasSource;
        private set => Set(ref hasSource, value);
    }

    public bool CanUndo => Document?.CanUndo == true;

    public bool CanRedo => Document?.CanRedo == true;

    public IReadOnlyList<string> Palette => Settings.PaletteHex;

    public IReadOnlyList<WpfBrush> PaletteBrushes { get; private set; } = [];

    public System.Windows.Media.Color ActiveColor =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.PaletteHex[Math.Clamp(ColorIndex, 0, Settings.PaletteSlotCount - 1)]);

    public double StrokeWidth => Settings.StrokeWidth * ActiveSize.Scale();

    public string DocumentSizeText => Document is null
        ? string.Empty
        : $"{Math.Round(Document.Size.Width):0} x {Math.Round(Document.Size.Height):0}";

    public bool IsArrowToolActive => ActiveTool == EditorTool.Arrow;

    public bool IsRectangleToolActive => ActiveTool == EditorTool.Rectangle;

    public bool IsCounterToolActive => ActiveTool == EditorTool.Counter;

    public bool IsTextToolActive => ActiveTool == EditorTool.Text;

    public bool IsPixelateToolActive => ActiveTool == EditorTool.Pixelate;

    public bool IsCropToolActive => ActiveTool == EditorTool.Crop;

    public bool IsColor1Active => ColorIndex == 0;

    public bool IsColor2Active => ColorIndex == 1;

    public bool IsColor3Active => ColorIndex == 2;

    public bool IsColor4Active => ColorIndex == 3;

    public bool IsColor5Active => ColorIndex == 4;

    public bool IsSmallSizeActive => ActiveSize == ShapeSize.Small;

    public bool IsMediumSizeActive => ActiveSize == ShapeSize.Medium;

    public bool IsLargeSizeActive => ActiveSize == ShapeSize.Large;

    public void LoadDocument(double width, double height)
    {
        Document = new AnnotationDocument(width, height);
        HasSource = true;
        RefreshPaletteBrushes();

        OnDocumentChanged();
    }

    public void ApplySettings(Settings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ColorIndex = Math.Clamp(ColorIndex, 0, Settings.PaletteSlotCount - 1);
        RefreshPaletteBrushes();
        OnPropertyChanged(nameof(DoneText));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(PaletteBrushes));
        OnPropertyChanged(nameof(ActiveColor));
        OnPropertyChanged(nameof(StrokeWidth));
    }

    private void RefreshPaletteBrushes()
    {
        PaletteBrushes = Settings.PaletteHex
            .Take(Settings.PaletteSlotCount)
            .Select(hex => (WpfBrush)new WpfSolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)))
            .ToArray();
        foreach (var brush in PaletteBrushes.OfType<WpfSolidColorBrush>())
        {
            brush.Freeze();
        }
    }

    public void SelectToolByKey(string key)
    {
        ActiveTool = key.ToUpperInvariant() switch
        {
            "Q" => EditorTool.Arrow,
            "W" => EditorTool.Rectangle,
            "E" => EditorTool.Counter,
            "R" => EditorTool.Text,
            "T" => EditorTool.Pixelate,
            "Y" => EditorTool.Crop,
            _ => ActiveTool,
        };
    }

    public void SelectColorByKey(string key)
    {
        if (int.TryParse(key, out var value) && value is >= 1 and <= Settings.PaletteSlotCount)
        {
            ColorIndex = value - 1;
        }
    }

    public void SelectSizeByKey(string key)
    {
        ActiveSize = key.ToUpperInvariant() switch
        {
            "Z" => ShapeSize.Small,
            "X" => ShapeSize.Medium,
            "C" => ShapeSize.Large,
            _ => ActiveSize,
        };
    }

    public void CommitShape(CorePoint start, CorePoint end, string? text = null)
    {
        if (Document is null)
        {
            return;
        }

        var textValue = string.IsNullOrWhiteSpace(text ?? PendingText) ? "Text" : text ?? PendingText;
        ShapeKind kind = ActiveTool switch
        {
            EditorTool.Arrow => new ShapeKind.Arrow(),
            EditorTool.Rectangle => new ShapeKind.Rectangle(),
            EditorTool.Counter => new ShapeKind.Counter(Document.NextCounterNumber),
            EditorTool.Text => new ShapeKind.Text(textValue, Settings.TextSize),
            EditorTool.Pixelate => new ShapeKind.Pixelate(),
            EditorTool.Crop => new ShapeKind.Rectangle(),
            _ => throw new ArgumentOutOfRangeException(),
        };

        Document.AppendShape(new Shape(kind, ColorIndex, ActiveSize, start, end));
        OnDocumentChanged();
    }

    public void CommitText(CorePoint anchor, string text)
    {
        if (Document is null)
        {
            return;
        }

        var value = text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Document.AppendShape(new Shape(
            new ShapeKind.Text(value, Settings.TextSize),
            ColorIndex,
            ActiveSize,
            anchor,
            anchor));
        OnDocumentChanged();
    }

    public bool Crop(CoreRect rect)
    {
        if (Document is null || !Document.Crop(rect))
        {
            return false;
        }

        OnDocumentChanged();
        return true;
    }

    public bool ExpandCanvas(CanvasExpansion expansion)
    {
        if (Document is null || !expansion.HasWork)
        {
            return false;
        }

        Document.ExpandCanvas(expansion);
        OnDocumentChanged();
        return true;
    }

    public bool Undo()
    {
        if (Document?.Undo() != true)
        {
            return false;
        }

        OnDocumentChanged();
        return true;
    }

    public bool Redo()
    {
        if (Document?.Redo() != true)
        {
            return false;
        }

        OnDocumentChanged();
        return true;
    }

    public void ZoomIn() => SetZoomKeepingViewport(NextZoom(+1));

    public void ZoomOut() => SetZoomKeepingViewport(NextZoom(-1));

    public void ResetZoom()
    {
        SetZoomKeepingViewport(1);
        ResetPan();
    }

    public void FitZoom(double viewportWidth, double viewportHeight)
    {
        if (Document is null || viewportWidth <= 0 || viewportHeight <= 0)
        {
            Zoom = 1;
            ResetPan();
            return;
        }

        var fit = Math.Min(viewportWidth * 0.9 / Document.Size.Width, viewportHeight * 0.9 / Document.Size.Height);
        Zoom = fit >= 1 ? 1 : fit;
        ResetPan();
    }

    public void PanBy(double deltaX, double deltaY)
    {
        PanOffsetX += deltaX;
        PanOffsetY += deltaY;
    }

    public void SetPan(double offsetX, double offsetY)
    {
        PanOffsetX = offsetX;
        PanOffsetY = offsetY;
    }

    public void ResetPan() => SetPan(0, 0);

    public void ZoomAt(int direction, double viewportX, double viewportY, double viewportWidth, double viewportHeight)
    {
        if (Document is null || viewportWidth <= 0 || viewportHeight <= 0)
        {
            SetZoomKeepingViewport(NextZoom(direction));
            return;
        }

        var oldZoom = Zoom;
        var nextZoom = NextZoom(direction);
        if (Math.Abs(nextZoom - oldZoom) < 0.001)
        {
            return;
        }

        var oldImageX = ImageOriginX(Document.Size.Width, viewportWidth, oldZoom, PanOffsetX);
        var oldImageY = ImageOriginY(Document.Size.Height, viewportHeight, oldZoom, PanOffsetY);
        var imageX = (viewportX - oldImageX) / oldZoom;
        var imageY = (viewportY - oldImageY) / oldZoom;

        Zoom = nextZoom;

        var centeredX = Math.Max(0, (viewportWidth - Document.Size.Width * nextZoom) / 2);
        var centeredY = Math.Max(0, (viewportHeight - Document.Size.Height * nextZoom) / 2);
        SetPan(viewportX - imageX * nextZoom - centeredX, viewportY - imageY * nextZoom - centeredY);
    }

    private double NextZoom(int direction)
    {
        var index = Array.FindIndex(ZoomSteps, step => Math.Abs(step - Zoom) < 0.001);
        if (index < 0)
        {
            index = Array.FindIndex(ZoomSteps, step => step > Zoom);
            if (index < 0)
            {
                index = ZoomSteps.Length - 1;
            }
        }

        return ZoomSteps[Math.Clamp(index + direction, 0, ZoomSteps.Length - 1)];
    }

    private static double ClosestZoom(double value) =>
        ZoomSteps.OrderBy(step => Math.Abs(step - value)).First();

    private static double[] ZoomSteps { get; } = [0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4];

    private void SetZoomKeepingViewport(double value) => Zoom = value;

    private static double ImageOriginX(double documentWidth, double viewportWidth, double zoom, double panOffsetX) =>
        Math.Max(0, (viewportWidth - documentWidth * zoom) / 2) + panOffsetX;

    private static double ImageOriginY(double documentHeight, double viewportHeight, double zoom, double panOffsetY) =>
        Math.Max(0, (viewportHeight - documentHeight * zoom) / 2) + panOffsetY;

    private void OnDocumentChanged()
    {
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(VisibleShapes));
        OnPropertyChanged(nameof(DocumentSizeText));
        OnPropertyChanged(nameof(PaletteBrushes));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
