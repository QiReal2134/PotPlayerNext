using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace PotPlayerNext.Services;

public sealed class PreviewSurface : Grid
{
    private InputSystemCursorShape? current;
    public void SetCursor(PreviewEdges edges, bool canPan)
    {
        var horizontal = (edges & (PreviewEdges.Left | PreviewEdges.Right)) != 0;
        var vertical = (edges & (PreviewEdges.Top | PreviewEdges.Bottom)) != 0;
        var shape = horizontal && vertical
            ? (edges == (PreviewEdges.Left | PreviewEdges.Top) || edges == (PreviewEdges.Right | PreviewEdges.Bottom)
                ? InputSystemCursorShape.SizeNorthwestSoutheast : InputSystemCursorShape.SizeNortheastSouthwest)
            : horizontal ? InputSystemCursorShape.SizeWestEast
            : vertical ? InputSystemCursorShape.SizeNorthSouth
            : canPan ? InputSystemCursorShape.Hand : InputSystemCursorShape.Arrow;
        if (current == shape) return;
        current = shape; ProtectedCursor = InputSystemCursor.Create(shape);
    }
}
