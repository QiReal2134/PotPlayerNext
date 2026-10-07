using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Graphics.Imaging;

namespace PotPlayerNext.Services;

public sealed class ThumbnailService : IDisposable
{
    private const int Capacity = 128;
    private readonly SemaphoreSlim workers = new(4, 4);
    private readonly LruCache<string, BitmapImage> cache = new(Capacity);
    private readonly Dictionary<string, Task<BitmapImage?>> inFlight = new();
    private readonly object inFlightLock = new();
    private bool disposed;

    public Task<BitmapImage?> GetAsync(MediaItem item, CancellationToken token)
    {
        if (disposed || token.IsCancellationRequested) return Task.FromResult<BitmapImage?>(null);
        if (cache.TryGet(item.Path, out var hit)) return Task.FromResult<BitmapImage?>(hit);

        lock (inFlightLock)
        {
            if (inFlight.TryGetValue(item.Path, out var existing))
                return existing;

            var task = LoadThumbnailInternalAsync(item, token);
            inFlight[item.Path] = task;
            return task;
        }
    }

    private async Task<BitmapImage?> LoadThumbnailInternalAsync(MediaItem item, CancellationToken token)
    {
        try
        {
            await workers.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                var file = await StorageFile.GetFileFromPathAsync(item.Path);
                using var thumbnail = await file.GetThumbnailAsync(item.Kind == "video" ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView, 192, ThumbnailOptions.ResizeThumbnail);
                if (thumbnail is null || thumbnail.Size == 0 || token.IsCancellationRequested || disposed) return null;
                // Shell providers may return an image larger than the requested size.
                var decoder = await BitmapDecoder.CreateAsync(thumbnail);
                var size = ImageDecodeBudget.Calculate(decoder.PixelWidth, decoder.PixelHeight, 192);
                thumbnail.Seek(0);
                token.ThrowIfCancellationRequested();
                var image = new BitmapImage { DecodePixelWidth = size.Width, DecodePixelHeight = size.Height };
                await image.SetSourceAsync(thumbnail);
                if (token.IsCancellationRequested || disposed) return null;
                cache.Set(item.Path, image);
                return image;
            }
            finally
            {
                workers.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            // Unsupported codecs, deleted files, and shell thumbnail failures get a placeholder.
            System.Diagnostics.Debug.WriteLine($"Thumbnail: {error.Message}");
            return null;
        }
        finally
        {
            lock (inFlightLock)
            {
                inFlight.Remove(item.Path);
            }
        }
    }

    public void Clear() => cache.Clear();
    public void Dispose() { disposed = true; Clear(); /* Do not dispose a semaphore used by pending requests. */ }
}
