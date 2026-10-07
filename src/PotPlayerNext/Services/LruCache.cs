namespace PotPlayerNext.Services;

/// <summary>Thread-safe LRU cache bounded by both entry count and retained weight.</summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object sync = new();
    private readonly int capacity;
    private readonly long maxWeight;
    private readonly Func<TValue, long> weightSelector;
    private readonly Dictionary<TKey, (TValue Value, long Weight, LinkedListNode<TKey> Node)> items;
    private readonly LinkedList<TKey> order = new();
    private long weight;

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
        : this(capacity, capacity, _ => 1, comparer) { }

    public LruCache(int capacity, long maxWeight, Func<TValue, long> weightSelector, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxWeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxWeight));
        ArgumentNullException.ThrowIfNull(weightSelector);
        this.capacity = capacity;
        this.maxWeight = maxWeight;
        this.weightSelector = weightSelector;
        items = new(comparer);
    }

    public long Weight
    {
        get { lock (sync) { return weight; } }
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
        var itemWeight = weightSelector(value);
        if (itemWeight < 0) throw new ArgumentOutOfRangeException(nameof(value), "Cache weight cannot be negative.");
        lock (sync)
        {
            RemoveCore(key);
            // A value larger than the entire budget is returned to its caller but
            // never retained. Subtract before adding to avoid long overflow.
            if (itemWeight > maxWeight) return;
            while (items.Count >= capacity || weight > maxWeight - itemWeight)
                RemoveCore(order.Last!.Value);
            items.Add(key, (value, itemWeight, order.AddFirst(key)));
            weight += itemWeight;
        }
    }

    public bool Remove(TKey key)
    {
        lock (sync) { return RemoveCore(key); }
    }

    private bool RemoveCore(TKey key)
    {
        if (!items.Remove(key, out var item)) return false;
        order.Remove(item.Node);
        weight -= item.Weight;
        return true;
    }

    public void Clear()
    {
        lock (sync)
        {
            items.Clear();
            order.Clear();
            weight = 0;
        }
    }
}
