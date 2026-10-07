namespace PotPlayerNext.Services;

[Flags]
public enum PreviewEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public static class PreviewGeometry
{
    public static (int Width, int Height) ImageClientSize(double width, double height, int workWidth, int workHeight)
    {
        var size = Fit(width, height, Math.Min(960, Math.Max(1, workWidth * 0.9)), Math.Min(640, Math.Max(1, workHeight * 0.9)));
        return (Math.Max(1, (int)Math.Round(size.Width)), Math.Max(1, (int)Math.Round(size.Height)));
    }

    public static PreviewEdges Edges(double x, double y, double width, double height, double band = 6)
    {
        var edges = PreviewEdges.None;
        if (x < band) edges |= PreviewEdges.Left; else if (x >= width - band) edges |= PreviewEdges.Right;
        if (y < band) edges |= PreviewEdges.Top; else if (y >= height - band) edges |= PreviewEdges.Bottom;
        return edges;
    }

    public static (int X, int Y, int Width, int Height) Resize(int x, int y, int width, int height, int dx, int dy, PreviewEdges edges, int minWidth, int minHeight)
    {
        var left = x; var top = y; var right = x + width; var bottom = y + height;
        if (edges.HasFlag(PreviewEdges.Left)) left = Math.Min(x + dx, right - minWidth);
        if (edges.HasFlag(PreviewEdges.Right)) right = Math.Max(right + dx, left + minWidth);
        if (edges.HasFlag(PreviewEdges.Top)) top = Math.Min(y + dy, bottom - minHeight);
        if (edges.HasFlag(PreviewEdges.Bottom)) bottom = Math.Max(bottom + dy, top + minHeight);
        return (left, top, right - left, bottom - top);
    }

    public static (double Width, double Height) Fit(double width, double height, double viewportWidth, double viewportHeight)
    {
        if (width <= 0 || height <= 0 || viewportWidth <= 0 || viewportHeight <= 0) return (1, 1);
        var scale = Math.Min(viewportWidth / width, viewportHeight / height);
        return (width * scale, height * scale);
    }

    public static bool CanPan(double width, double height, double zoom, double viewportWidth, double viewportHeight) =>
        zoom > 1.001 && (width * zoom > viewportWidth + 1 || height * zoom > viewportHeight + 1);

    public static (int X, int Y) Center(int x, int y, int width, int height, int windowWidth, int windowHeight) =>
        (x + (width - windowWidth) / 2, y + (height - windowHeight) / 2);
}
