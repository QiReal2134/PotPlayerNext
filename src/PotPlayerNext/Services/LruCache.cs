namespace PotPlayerNext.Services;

/// <summary>Thread-safe bounded LRU cache.</summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object sync = new();
    private readonly int capacity;
    private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node)> items;
    private readonly LinkedList<TKey> order = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        items = new(comparer);
    }

    public int Count
    {
        get { lock (sync) { return items.Count; } }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (sync)
        {
            if (!items.TryGetValue(key, out var item)) { value = default!; return false; }
            order.Remove(item.Node); order.AddFirst(item.Node);
            value = item.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (sync)
        {
            if (items.Remove(key, out var previous)) order.Remove(previous.Node);
            items.Add(key, (value, order.AddFirst(key)));
            if (items.Count <= capacity) return;
            var oldest = order.Last!;
            items.Remove(oldest.Value); order.RemoveLast();
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            items.Clear();
            order.Clear();
        }
    }
}
