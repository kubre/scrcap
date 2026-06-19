namespace Scrcap.Core;

public readonly record struct CorePoint(double X, double Y);

public readonly record struct CoreSize(double Width, double Height);

public readonly record struct CoreRect(double X, double Y, double Width, double Height)
{
    public double MinX => X;

    public double MinY => Y;

    public double MaxX => X + Width;

    public double MaxY => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static CoreRect FromPoints(CorePoint a, CorePoint b)
    {
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        return new CoreRect(minX, minY, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    public bool Contains(CorePoint point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    public bool Intersects(CoreRect other) =>
        !IsEmpty
        && !other.IsEmpty
        && other.MaxX >= MinX
        && other.MinX <= MaxX
        && other.MaxY >= MinY
        && other.MinY <= MaxY;

    public CorePoint Clamp(CorePoint point) =>
        new(Math.Clamp(point.X, MinX, MaxX), Math.Clamp(point.Y, MinY, MaxY));
}

public abstract record ShapeKind
{
    private ShapeKind()
    {
    }

    public sealed record Arrow : ShapeKind;

    public sealed record Rectangle : ShapeKind;

    public sealed record Pixelate : ShapeKind;

    public sealed record Counter(int Number) : ShapeKind;

    public sealed record Text(string Value, double Size, double? MaxWidth = null) : ShapeKind;
}

public enum ShapeSize
{
    Small,
    Medium,
    Large,
}

public static class ShapeSizeExtensions
{
    public static double Scale(this ShapeSize size) =>
        size switch
        {
            ShapeSize.Small => 1.0,
            ShapeSize.Medium => 1.8,
            ShapeSize.Large => 2.8,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
}

public sealed record Shape(
    ShapeKind Kind,
    int ColorIndex,
    ShapeSize Size,
    CorePoint Start,
    CorePoint End)
{
    public CoreRect Bounds =>
        Kind is ShapeKind.Counter or ShapeKind.Text
            ? new CoreRect(Start.X, Start.Y, 1, 1)
            : CoreRect.FromPoints(Start, End);

    public Shape Shifted(double dx, double dy) =>
        this with
        {
            Start = new CorePoint(Start.X + dx, Start.Y + dy),
            End = new CorePoint(End.X + dx, End.Y + dy),
        };

    public Shape ClampedTo(CoreRect rect) =>
        this with
        {
            Start = rect.Clamp(Start),
            End = rect.Clamp(End),
        };
}

public sealed class AnnotationStack
{
    private readonly List<Shape> shapes = [];

    public IReadOnlyList<Shape> Shapes => shapes;

    public int Cursor { get; private set; }

    public IReadOnlyList<Shape> Visible => shapes.Take(Cursor).ToArray();

    public bool CanUndo => Cursor > 0;

    public bool CanRedo => Cursor < shapes.Count;

    public bool IsEmpty => Cursor == 0;

    public void Append(Shape shape)
    {
        if (Cursor < shapes.Count)
        {
            shapes.RemoveRange(Cursor, shapes.Count - Cursor);
        }

        shapes.Add(shape);
        Cursor = shapes.Count;
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        Cursor--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        Cursor++;
        return true;
    }

    public int NextCounterNumber =>
        Visible.Count(shape => shape.Kind is ShapeKind.Counter) + 1;
}

public sealed class AnnotationDocument
{
    private readonly List<DocumentSnapshot> snapshots = [];

    public AnnotationDocument(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Document dimensions must be positive.");
        }

        snapshots.Add(new DocumentSnapshot(new CoreSize(width, height), []));
        Cursor = 0;
    }

    public CoreSize Size => Current.Size;

    public IReadOnlyList<Shape> Shapes => Current.Shapes;

    public int Cursor { get; private set; }

    public bool CanUndo => Cursor > 0;

    public bool CanRedo => Cursor < snapshots.Count - 1;

    public int NextCounterNumber =>
        Shapes.Count(shape => shape.Kind is ShapeKind.Counter) + 1;

    public void AppendShape(Shape shape) =>
        Commit(Current with { Shapes = [.. Current.Shapes, shape] });

    public AutoExpandResult AppendShapeWithAutoExpand(Shape shape, CoreRect bounds, double padding = 0)
    {
        var growth = AutoExpandResult.FromBounds(bounds, Current.Size, padding);
        var shiftedShapes = growth.HasGrowth
            ? Current.Shapes.Select(existing => existing.Shifted(growth.Left, growth.Top)).ToArray()
            : Current.Shapes;
        var shiftedShape = growth.HasGrowth
            ? shape.Shifted(growth.Left, growth.Top)
            : shape;

        Commit(new DocumentSnapshot(
            new CoreSize(Current.Size.Width + growth.Left + growth.Right, Current.Size.Height + growth.Top + growth.Bottom),
            [.. shiftedShapes, shiftedShape]));
        return growth;
    }

    public bool Crop(CoreRect crop)
    {
        var bounded = NormalizeCrop(crop);
        if (bounded.Width < 2 || bounded.Height < 2)
        {
            return false;
        }

        var shapes = Current.Shapes
            .Select(shape => CropShape(shape, bounded))
            .OfType<Shape>()
            .ToArray();

        Commit(new DocumentSnapshot(new CoreSize(bounded.Width, bounded.Height), shapes));
        return true;
    }

    public bool AutoExpandToInclude(CoreRect bounds, double padding = 0)
    {
        if (bounds.IsEmpty)
        {
            return false;
        }

        var leftGrowth = Math.Max(0, -bounds.MinX + padding);
        var topGrowth = Math.Max(0, -bounds.MinY + padding);
        var rightGrowth = Math.Max(0, bounds.MaxX - Current.Size.Width + padding);
        var bottomGrowth = Math.Max(0, bounds.MaxY - Current.Size.Height + padding);

        if (leftGrowth <= 0 && topGrowth <= 0 && rightGrowth <= 0 && bottomGrowth <= 0)
        {
            return false;
        }

        var shifted = Current.Shapes
            .Select(shape => shape.Shifted(leftGrowth, topGrowth))
            .ToArray();
        Commit(new DocumentSnapshot(
            new CoreSize(Current.Size.Width + leftGrowth + rightGrowth, Current.Size.Height + topGrowth + bottomGrowth),
            shifted));
        return true;
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        Cursor--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        Cursor++;
        return true;
    }

    private DocumentSnapshot Current => snapshots[Cursor];

    private void Commit(DocumentSnapshot snapshot)
    {
        if (Cursor < snapshots.Count - 1)
        {
            snapshots.RemoveRange(Cursor + 1, snapshots.Count - Cursor - 1);
        }

        snapshots.Add(snapshot);
        Cursor = snapshots.Count - 1;
    }

    private CoreRect NormalizeCrop(CoreRect crop)
    {
        var document = new CoreRect(0, 0, Current.Size.Width, Current.Size.Height);
        var min = document.Clamp(new CorePoint(crop.MinX, crop.MinY));
        var max = document.Clamp(new CorePoint(crop.MaxX, crop.MaxY));
        return CoreRect.FromPoints(min, max);
    }

    private static Shape? CropShape(Shape shape, CoreRect crop)
    {
        if (shape.Kind is ShapeKind.Counter or ShapeKind.Text)
        {
            return crop.Contains(shape.Start) ? shape.Shifted(-crop.X, -crop.Y) : null;
        }

        if (!shape.Bounds.Intersects(crop))
        {
            return null;
        }

        return shape
            .ClampedTo(crop)
            .Shifted(-crop.X, -crop.Y);
    }

    private sealed record DocumentSnapshot(CoreSize Size, IReadOnlyList<Shape> Shapes);
}

public readonly record struct AutoExpandResult(double Left, double Top, double Right, double Bottom)
{
    public bool HasGrowth => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;

    public static AutoExpandResult None => new(0, 0, 0, 0);

    public static AutoExpandResult FromBounds(CoreRect bounds, CoreSize size, double padding = 0)
    {
        if (bounds.IsEmpty)
        {
            return None;
        }

        return new AutoExpandResult(
            Math.Max(0, -bounds.MinX + padding),
            Math.Max(0, -bounds.MinY + padding),
            Math.Max(0, bounds.MaxX - size.Width + padding),
            Math.Max(0, bounds.MaxY - size.Height + padding));
    }
}
