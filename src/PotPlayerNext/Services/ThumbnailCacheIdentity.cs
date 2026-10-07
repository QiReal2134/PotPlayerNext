namespace PotPlayerNext.Services;

/// <summary>A path is not a content identity: replacement must invalidate thumbnails and failures.</summary>
internal readonly record struct ThumbnailCacheIdentity(string Path, string Kind, long Bytes, long LastWriteUtcTicks, bool Exists)
{
    public static IEqualityComparer<ThumbnailCacheIdentity> Comparer { get; } = new IdentityComparer();

    // FileInfo caches one metadata snapshot after Exists. Call off the UI thread
    // and under the thumbnail worker limit; even cache hits revalidate the file.
    public static ThumbnailCacheIdentity Read(string path, string kind, long knownBytes)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new(path, kind, file.Length, file.LastWriteTimeUtc.Ticks, true)
            : new(path, kind, knownBytes, 0, false);
    }

    private sealed class IdentityComparer : IEqualityComparer<ThumbnailCacheIdentity>
    {
        public bool Equals(ThumbnailCacheIdentity x, ThumbnailCacheIdentity y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path) && x.Kind == y.Kind &&
            x.Bytes == y.Bytes && x.LastWriteUtcTicks == y.LastWriteUtcTicks && x.Exists == y.Exists;

        public int GetHashCode(ThumbnailCacheIdentity value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path), value.Kind,
            value.Bytes, value.LastWriteUtcTicks, value.Exists);
    }
}
