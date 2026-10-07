namespace PotPlayerNext.Services;

/// <summary>Bounded negative-result cache with a monotonic, non-sliding lifetime.</summary>
internal sealed class ExpiringKeyCache<TKey> where TKey : notnull
{
    private readonly object sync = new();
    private readonly LruCache<TKey, long> entries;
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;

    public ExpiringKeyCache(int capacity, TimeSpan lifetime, IEqualityComparer<TKey>? comparer = null, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        entries = new(capacity, comparer);
        this.lifetime = lifetime;
        this.clock = clock ?? TimeProvider.System;
    }

    public int Count { get { lock (sync) { return entries.Count; } } }

    public bool Contains(TKey key)
    {
        lock (sync)
        {
            if (!entries.TryGet(key, out var created)) return false;
            if (clock.GetElapsedTime(created) < lifetime) return true;
            entries.Remove(key);
            return false;
        }
    }

    public bool Add(TKey key, CancellationToken token = default)
    {
        lock (sync)
        {
            if (token.IsCancellationRequested) return false;
            entries.Set(key, clock.GetTimestamp());
            return true;
        }
    }

    public void Remove(TKey key) { lock (sync) { entries.Remove(key); } }
    public void Clear() { lock (sync) { entries.Clear(); } }
}
