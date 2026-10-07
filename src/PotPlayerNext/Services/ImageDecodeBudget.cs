namespace PotPlayerNext.Services;

public static class ImageDecodeBudget
{
    /// <summary>Bound both decoded dimensions without upscaling or changing known aspect ratios.</summary>
    public static (int Width, int Height) Calculate(uint width, uint height, int maxEdge)
    {
        if (maxEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        if (width == 0 || height == 0) return (maxEdge, maxEdge);
        var ratio = Math.Min(1d, (double)maxEdge / Math.Max(width, height));
        return (Math.Max(1, (int)(width * ratio)), Math.Max(1, (int)(height * ratio)));
    }
}
