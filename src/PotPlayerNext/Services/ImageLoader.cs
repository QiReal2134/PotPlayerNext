using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PotPlayerNext.Services;

/// <summary>Source plus original, EXIF-oriented dimensions (not the bounded decoded size).</summary>
public sealed record LoadedImage(ImageSource Source, uint Width, uint Height);

public static class ImageLoader
{
    // Bound vector input as well as raster output. Path-complexity is renderer-dependent.
    private const ulong MaximumSvgBytes = 16 * 1024 * 1024;

    /// <summary>Call from the UI thread. Uses existing Windows codecs; does not install codecs.</summary>
    public static async Task<LoadedImage> LoadAsync(StorageFile file, uint maxEdge, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateBudget(maxEdge);
        token.ThrowIfCancellationRequested();
        using var stream = await file.OpenReadAsync();
        token.ThrowIfCancellationRequested();
        if (file.FileType.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = await LoadSvgAsync(stream, maxEdge, token);
            return EnsureCurrent(svg, token);
        }

        var decoder = await BitmapDecoder.CreateAsync(stream);
        token.ThrowIfCancellationRequested();
        var width = decoder.OrientedPixelWidth;
        var height = decoder.OrientedPixelHeight;
        if (file.FileType.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            // Keep native GIF animation. All other raster formats reuse the single decoder below.
            var size = ImageDecodeBudget.Calculate(width, height, checked((int)maxEdge));
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = size.Width, DecodePixelHeight = size.Height };
            try
            {
                await bitmap.SetSourceAsync(stream);
                token.ThrowIfCancellationRequested();
                return new LoadedImage(bitmap, width, height);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A registered Windows decoder can succeed where the XAML BitmapImage path does not.
                token.ThrowIfCancellationRequested();
                System.Diagnostics.Debug.WriteLine($"BitmapImage fallback: {error.Message}");
            }
        }
        var loaded = await DecodeRasterAsync(decoder, maxEdge, token);
        return EnsureCurrent(loaded, token);
    }

    /// <summary>Decodes a caller-owned stream once, including shell thumbnails.</summary>
    internal static async Task<LoadedImage> LoadRasterAsync(IRandomAccessStream stream, uint maxEdge, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateBudget(maxEdge);
        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        token.ThrowIfCancellationRequested();
        var result = await DecodeRasterAsync(decoder, maxEdge, token);
        return EnsureCurrent(result, token);
    }

    private static async Task<LoadedImage> DecodeRasterAsync(BitmapDecoder decoder, uint maxEdge, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Scaling is applied before EXIF orientation, so budget the raw axes. Rotation only swaps them.
        var size = ImageDecodeBudget.Calculate(decoder.PixelWidth, decoder.PixelHeight, checked((int)maxEdge));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)size.Width,
            ScaledHeight = (uint)size.Height,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        token.ThrowIfCancellationRequested();
        var source = new SoftwareBitmapSource();
        try
        {
            await source.SetBitmapAsync(softwareBitmap);
            token.ThrowIfCancellationRequested();
            return new LoadedImage(source, decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static async Task<LoadedImage> LoadSvgAsync(IRandomAccessStream stream, uint maxEdge, CancellationToken token)
    {
        if (stream.Size > MaximumSvgBytes) throw new InvalidDataException("SVG files must be no larger than 16 MiB.");
        token.ThrowIfCancellationRequested();
        var dimensions = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var header = stream.CloneStream().AsStreamForRead();
            return SvgDimensions.Read(header);
        }, token);
        token.ThrowIfCancellationRequested();
        var size = SvgDimensions.RasterSize(dimensions.Width, dimensions.Height, maxEdge);
        var svg = new SvgImageSource { RasterizePixelWidth = size.Width, RasterizePixelHeight = size.Height };
        stream.Seek(0);
        var status = await svg.SetSourceAsync(stream);
        token.ThrowIfCancellationRequested();
        if (status != SvgImageSourceLoadStatus.Success)
            throw new InvalidDataException($"SVG rendering failed: {status}.");
        return new LoadedImage(svg, dimensions.Width, dimensions.Height);
    }

    private static void ValidateBudget(uint maxEdge)
    {
        if (maxEdge == 0 || maxEdge > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxEdge));
    }

    private static LoadedImage EnsureCurrent(LoadedImage result, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            // An inner await may complete just before cancellation. No caller owns this source yet.
            (result.Source as IDisposable)?.Dispose();
            token.ThrowIfCancellationRequested();
        }
        return result;
    }
}
