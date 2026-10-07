using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PotPlayerNext.Services;

public sealed class ThumbnailService : IDisposable
{
    private const int Capacity = 128;
    private const uint ThumbnailEdge = 192;
    private const long DecodedCacheBudget = 12L * 1024 * 1024;
    private sealed record CachedThumbnail(ImageSource Source, long DecodedBytes);

    private readonly object lifetime = new();
    private readonly SemaphoreSlim workers = new(4, 4);
    private readonly LruCache<ThumbnailCacheIdentity, CachedThumbnail> cache =
        new(Capacity, DecodedCacheBudget, image => image.DecodedBytes, ThumbnailCacheIdentity.Comparer);
    private readonly ExpiringKeyCache<ThumbnailCacheIdentity> unavailable =
        new(256, TimeSpan.FromSeconds(30), ThumbnailCacheIdentity.Comparer);
    private readonly SharedAsyncLoads<(string Path, string Kind), CachedThumbnail> inFlight = new();
    private volatile bool disposed;
    private long generation;
    public (int Entries, long EstimatedBytes) CacheUsage => (cache.Count, cache.Weight);

    public async Task<ImageSource?> GetAsync(MediaItem item, CancellationToken token)
    {
        if (disposed || token.IsCancellationRequested) return null;
        // Coalesce before metadata I/O and the worker queue. One recycled container
        // cancels only its own wait; the provider stops when the final consumer leaves.
        var result = await inFlight.GetAsync((item.Path, item.Kind),
            sharedToken => LoadThumbnailInternalAsync(item, sharedToken), token);
        return result?.Source;
    }

    private async Task<CachedThumbnail?> LoadThumbnailInternalAsync(MediaItem item, CancellationToken token)
    {
        var stamp = Volatile.Read(ref generation);
        var key = new ThumbnailCacheIdentity(item.Path, item.Kind, item.Bytes, 0, false);
        try
        {
            // Cancelled scrolling requests leave the queue without opening a file.
            await workers.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                key = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return ThumbnailCacheIdentity.Read(item.Path, item.Kind, item.Bytes);
                }, token);
                token.ThrowIfCancellationRequested();
                if (disposed || stamp != Volatile.Read(ref generation)) return null;
                if (cache.TryGet(key, out var hit)) return hit;
                if (unavailable.Contains(key)) return null;

                if (!key.Exists)
                {
                    RememberUnavailable(key, stamp, token);
                    return null;
                }

                var file = await StorageFile.GetFileFromPathAsync(item.Path).AsTask(token);
                token.ThrowIfCancellationRequested();
                var loaded = await LoadShellOrImageAsync(file, item.Kind, token);
                if (loaded is null)
                {
                    RememberUnavailable(key, stamp, token);
                    return null;
                }

                var size = ImageDecodeBudget.Calculate(loaded.Width, loaded.Height, (int)ThumbnailEdge);
                // Raster transforms do not upscale; orientation only swaps axes.
                // Vectors can upscale, so keep a conservative full-edge allowance.
                var bytes = loaded.Source is SvgImageSource
                    ? ThumbnailEdge * (long)ThumbnailEdge * 4
                    : size.Width * (long)size.Height * 4;
                var image = new CachedThumbnail(loaded.Source, bytes);
                lock (lifetime)
                {
                    if (!disposed && stamp == generation && !token.IsCancellationRequested)
                    {
                        unavailable.Remove(key);
                        cache.Set(key, image);
                        return image;
                    }
                }
                // Never dispose a cached/shared source: a live container may own it.
                // This source has not been published and is safe to release now.
                (loaded.Source as IDisposable)?.Dispose();
                return null;
            }
            finally
            {
                workers.Release();
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            // Genuine failures are retried after a short bounded interval. Cancelled
            // providers (including HRESULT cancellation) must not poison this cache.
            RememberUnavailable(key, stamp, token);
            System.Diagnostics.Debug.WriteLine($"Thumbnail: {error.Message}");
            return null;
        }
    }

    private static async Task<LoadedImage?> LoadShellOrImageAsync(StorageFile file, string kind, CancellationToken token)
    {
        try
        {
            using var thumbnail = await file.GetThumbnailAsync(
                kind == "video" ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView,
                ThumbnailEdge, ThumbnailOptions.ResizeThumbnail).AsTask(token);
            token.ThrowIfCancellationRequested();
            // Some formats return only a file-type icon; that is not a media preview.
            if (thumbnail is { Size: > 0, Type: ThumbnailType.Image })
                return await ImageLoader.LoadRasterAsync(thumbnail, ThumbnailEdge, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Shell thumbnail: {error.Message}");
        }

        token.ThrowIfCancellationRequested();
        // SVG and installed platform image codecs can work even without a shell
        // provider. Video remains a placeholder if the shell cannot provide a frame.
        if (kind != "image") return null;
        if (file.FileType.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return await ImageLoader.LoadAsync(file, ThumbnailEdge, token);

        // Gallery thumbnails are static. Avoid the preview loader's additional
        // BitmapImage parse (and animation memory) for common raster formats.
        using var stream = await file.OpenReadAsync().AsTask(token);
        token.ThrowIfCancellationRequested();
        return await ImageLoader.LoadRasterAsync(stream, ThumbnailEdge, token);
    }

    private void RememberUnavailable(ThumbnailCacheIdentity key, long stamp, CancellationToken token)
    {
        lock (lifetime)
        {
            if (!disposed && stamp == generation && !token.IsCancellationRequested) unavailable.Add(key, token);
        }
    }

    public void Clear()
    {
        lock (lifetime)
        {
            ++generation;
            cache.Clear();
            unavailable.Clear();
        }
        inFlight.Clear();
    }

    public void Dispose()
    {
        lock (lifetime)
        {
            disposed = true;
            ++generation;
            cache.Clear();
            unavailable.Clear();
        }
        inFlight.Dispose();
        // Pending provider callbacks still release workers; do not dispose the semaphore.
    }
}
