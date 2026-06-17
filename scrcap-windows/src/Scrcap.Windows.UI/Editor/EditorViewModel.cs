using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scrcap.Core;
using System.Windows.Media;

namespace Scrcap.Windows.UI.Editor;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private EditorTool activeTool = EditorTool.Arrow;
    private int colorIndex;
    private ShapeSize activeSize = ShapeSize.Small;
    private double zoom = 1;
    private string pendingText = "Text";
    private bool hasSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorViewModel(Settings settings)
    {
        Settings = settings;
        ColorIndex = 0;
        ActiveSize = ShapeSize.Small;
    }

    public Settings Settings { get; }

    public AnnotationDocument? Document { get; private set; }

    public IReadOnlyList<Shape> VisibleShapes => Document?.Shapes ?? [];

    public EditorTool ActiveTool
    {
        get => activeTool;
        set => Set(ref activeTool, value);
    }

    public int ColorIndex
    {
        get => colorIndex;
        set => Set(ref colorIndex, Math.Clamp(value, 0, Settings.PaletteSlotCount - 1));
    }

    public ShapeSize ActiveSize
    {
        get => activeSize;
        set => Set(ref activeSize, value);
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

    public System.Windows.Media.Color ActiveColor =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.PaletteHex[Math.Clamp(ColorIndex, 0, Settings.PaletteSlotCount - 1)]);

    public double StrokeWidth => Settings.StrokeWidth * ActiveSize.Scale();

    public void LoadDocument(double width, double height)
    {
        Document = new AnnotationDocument(width, height);
        HasSource = true;
        OnDocumentChanged();
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
            OnPropertyChanged(nameof(ActiveColor));
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

    public bool Crop(CoreRect rect)
    {
        if (Document is null || !Document.Crop(rect))
        {
            return false;
        }

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

    public void ZoomIn() => Zoom = NextZoom(+1);

    public void ZoomOut() => Zoom = NextZoom(-1);

    public void ResetZoom() => Zoom = 1;

    public void FitZoom(double viewportWidth, double viewportHeight)
    {
        if (Document is null || viewportWidth <= 0 || viewportHeight <= 0)
        {
            Zoom = 1;
            return;
        }

        var fit = Math.Min(viewportWidth * 0.9 / Document.Size.Width, viewportHeight * 0.9 / Document.Size.Height);
        Zoom = fit >= 1 ? 1 : fit;
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

    private void OnDocumentChanged()
    {
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(VisibleShapes));
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
