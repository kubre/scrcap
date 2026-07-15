using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class AnnotationStackTests
{
    [Fact]
    public void AppendUndoRedoKeepsVisibleCursorAndTruncatesRedoTail()
    {
        var stack = new AnnotationStack();
        stack.Append(TestShape(new ShapeKind.Arrow()));
        stack.Append(TestShape(new ShapeKind.Rectangle()));
        stack.Append(TestShape(new ShapeKind.Pixelate()));

        Assert.Equal(3, stack.Visible.Count);
        Assert.True(stack.Undo());
        Assert.True(stack.Undo());
        Assert.Equal([new ShapeKind.Arrow()], stack.Visible.Select(shape => shape.Kind));

        Assert.True(stack.Redo());
        stack.Append(TestShape(new ShapeKind.Counter(1)));

        Assert.False(stack.CanRedo);
        Assert.Equal([new ShapeKind.Arrow(), new ShapeKind.Rectangle(), new ShapeKind.Counter(1)], stack.Visible.Select(shape => shape.Kind));
    }

    [Fact]
    public void CounterNumberFollowsVisibleCounters()
    {
        var stack = new AnnotationStack();
        stack.Append(TestShape(new ShapeKind.Counter(1)));
        stack.Append(TestShape(new ShapeKind.Counter(2)));

        Assert.Equal(3, stack.NextCounterNumber);

        stack.Undo();

        Assert.Equal(2, stack.NextCounterNumber);
    }

    [Fact]
    public void DocumentCropKeepsAndShiftsIntersectingShapes()
    {
        var document = new AnnotationDocument(100, 80);
        document.AppendShape(TestShape(new ShapeKind.Arrow(), new CorePoint(5, 5), new CorePoint(20, 20)));
        document.AppendShape(TestShape(new ShapeKind.Rectangle(), new CorePoint(30, 30), new CorePoint(90, 70)));
        document.AppendShape(TestShape(new ShapeKind.Text("kept", 16), new CorePoint(35, 35), new CorePoint(35, 35)));
        document.AppendShape(TestShape(new ShapeKind.Counter(1), new CorePoint(95, 75), new CorePoint(95, 75)));

        Assert.True(document.Crop(new CoreRect(25, 20, 50, 40)));

        Assert.Equal(new CoreSize(50, 40), document.Size);
        Assert.Equal(2, document.Shapes.Count);
        Assert.Equal(new CorePoint(5, 10), document.Shapes[0].Start);
        Assert.Equal(new CorePoint(50, 40), document.Shapes[0].End);
        Assert.Equal(new CorePoint(10, 15), document.Shapes[1].Start);
    }

    [Fact]
    public void DocumentUndoRestoresBitmapDimensionsAndShapesAfterCrop()
    {
        var document = new AnnotationDocument(100, 80);
        document.AppendShape(TestShape(new ShapeKind.Rectangle(), new CorePoint(10, 10), new CorePoint(80, 60)));

        Assert.True(document.Crop(new CoreRect(20, 20, 20, 20)));
        Assert.Equal(new CoreSize(20, 20), document.Size);

        Assert.True(document.Undo());

        Assert.Equal(new CoreSize(100, 80), document.Size);
        Assert.Single(document.Shapes);
        Assert.Equal(new CorePoint(10, 10), document.Shapes[0].Start);
    }

    [Fact]
    public void DocumentCommitTruncatesRedoTailAcrossShapeAndCrop()
    {
        var document = new AnnotationDocument(100, 80);
        document.AppendShape(TestShape(new ShapeKind.Arrow()));
        Assert.True(document.Crop(new CoreRect(0, 0, 50, 50)));
        Assert.True(document.Undo());

        document.AppendShape(TestShape(new ShapeKind.Counter(1), new CorePoint(2, 2), new CorePoint(2, 2)));

        Assert.False(document.CanRedo);
        Assert.Equal(new CoreSize(100, 80), document.Size);
        Assert.Equal(2, document.Shapes.Count);
    }

    [Fact]
    public void DocumentAutoExpandCanBeUndoneWithAnnotationHistory()
    {
        var document = new AnnotationDocument(100, 80);
        document.AutoExpandToInclude(new CoreRect(-10, 0, 130, 90));
        document.AppendShape(TestShape(new ShapeKind.Arrow(), new CorePoint(0, 1), new CorePoint(120, 90)));

        Assert.Equal(new CoreSize(130, 90), document.Size);
        Assert.True(document.Undo());
        Assert.Empty(document.Shapes);
        Assert.Equal(new CoreSize(130, 90), document.Size);
        Assert.True(document.Undo());
        Assert.Equal(new CoreSize(100, 80), document.Size);
    }

    [Fact]
    public void CanvasExpansionUsesExactCeiledMissingEdges()
    {
        var expansion = CanvasExpansion.Fitting(new CoreRect(-0.2, -4.1, 106.4, 88.3), new CoreSize(100, 80));

        Assert.Equal(new CanvasExpansion(1, 5, 7, 5), expansion);
    }

    [Fact]
    public void DocumentAutoExpandShiftsExistingShapesByLeadingGrowthOnly()
    {
        var document = new AnnotationDocument(100, 80);
        document.AppendShape(TestShape(new ShapeKind.Rectangle(), new CorePoint(10, 12), new CorePoint(30, 32)));

        Assert.True(document.AutoExpandToInclude(new CoreRect(-2.4, -1.2, 110, 90), out var offset));

        Assert.Equal(new CorePoint(3, 2), offset);
        Assert.Equal(new CoreSize(111, 91), document.Size);
        Assert.Equal(new CorePoint(13, 14), document.Shapes[0].Start);
        Assert.Equal(new CorePoint(33, 34), document.Shapes[0].End);
    }

    private static Shape TestShape(ShapeKind kind) =>
        new(kind, 0, ShapeSize.Small, new CorePoint(1, 2), new CorePoint(10, 20));

    private static Shape TestShape(ShapeKind kind, CorePoint start, CorePoint end) =>
        new(kind, 0, ShapeSize.Small, start, end);
}
