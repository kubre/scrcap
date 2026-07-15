using System.Windows;
using WpfPoint = System.Windows.Point;

namespace Scrcap.Windows.UI.Overlay;

internal sealed class OverlaySelectionDragState
{
    private WpfPoint dragStart;
    private WpfPoint moveStart;
    private Rect moveStartSelection;
    private bool hasDrag;
    private bool isMoving;

    public Rect CurrentSelection { get; private set; }

    public void Begin(WpfPoint point)
    {
        dragStart = point;
        CurrentSelection = new Rect(point, point);
        hasDrag = true;
        isMoving = false;
    }

    public Rect ResizeTo(WpfPoint point)
    {
        EnsureDrag();
        EndMove();
        CurrentSelection = Normalize(dragStart, point);
        return CurrentSelection;
    }

    public Rect MoveTo(WpfPoint point)
    {
        EnsureDrag();
        BeginMove(point);
        var movedSelection = moveStartSelection;
        movedSelection.Offset(point - moveStart);
        CurrentSelection = movedSelection;
        return CurrentSelection;
    }

    public void End()
    {
        hasDrag = false;
        isMoving = false;
    }

    private void BeginMove(WpfPoint point)
    {
        if (isMoving)
        {
            return;
        }

        isMoving = true;
        moveStart = point;
        moveStartSelection = CurrentSelection;
    }

    private void EndMove()
    {
        if (!isMoving)
        {
            return;
        }

        dragStart = new WpfPoint(
            dragStart.X + CurrentSelection.X - moveStartSelection.X,
            dragStart.Y + CurrentSelection.Y - moveStartSelection.Y);
        isMoving = false;
    }

    private void EnsureDrag()
    {
        if (!hasDrag)
        {
            throw new InvalidOperationException("Selection drag has not started.");
        }
    }

    private static Rect Normalize(WpfPoint start, WpfPoint end) =>
        new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));
}
