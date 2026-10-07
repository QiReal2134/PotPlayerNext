namespace PotPlayerNext.Services;

/// <summary>Coalesces loads without letting one cancelled consumer cancel another.</summary>
public sealed class SharedAsyncLoads<TKey, TValue> : IDisposable where TKey : notnull where TValue : class
{
    private sealed class Entry
    {
        public readonly CancellationTokenSource Stop = new();
        public readonly TaskCompletionSource<TValue?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters;
        public bool Finished, CancellationStarted, CancellationFinished, Disposed;
    }

    private readonly object sync = new();
    private readonly Dictionary<TKey, Entry> entries = new();
    private bool disposed;

    public async Task<TValue?> GetAsync(TKey key, Func<CancellationToken, Task<TValue?>> load, CancellationToken token)
    {
        Entry entry; bool start;
        lock (sync)
        {
            if (disposed || token.IsCancellationRequested) return null;
            start = !entries.TryGetValue(key, out entry!);
            if (start) entries.Add(key, entry = new Entry());
            ++entry.Waiters;
        }
        // Register the entry before calling the factory: even synchronous completion
        // must not leave a completed/faulted task stuck in the dictionary.
        if (start) _ = LoadAsync(key, entry, load);
        try { return await entry.Completion.Task.WaitAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        finally
        {
            bool cancel;
            lock (sync)
            {
                --entry.Waiters;
                cancel = entry.Waiters == 0 && MarkCancellation(entry);
                if (cancel) RemoveCurrent(key, entry);
                DisposeIfFinished(entry);
            }
            if (cancel) Cancel(entry);
        }
    }

    private async Task LoadAsync(TKey key, Entry entry, Func<CancellationToken, Task<TValue?>> load)
    {
        try
        {
            var result = await load(entry.Stop.Token);
            entry.Stop.Token.ThrowIfCancellationRequested();
            entry.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException) when (entry.Stop.IsCancellationRequested) { entry.Completion.TrySetResult(null); }
        catch (Exception error) { entry.Completion.TrySetException(error); }
        finally
        {
            lock (sync)
            {
                // A cancelled old load may finish after a replacement for the same key.
                RemoveCurrent(key, entry);
                entry.Finished = true;
                DisposeIfFinished(entry);
            }
        }
    }

    private void RemoveCurrent(TKey key, Entry entry)
    {
        if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) entries.Remove(key);
    }

    // All lifetime decisions happen under sync; cancellation callbacks run outside it.
    private static bool MarkCancellation(Entry entry)
    {
        if (entry.Finished || entry.CancellationStarted || entry.Completion.Task.IsCompleted) return false;
        entry.CancellationStarted = true;
        return true;
    }

    private void Cancel(Entry entry)
    {
        try { entry.Stop.Cancel(); entry.Completion.TrySetResult(null); }
        finally
        {
            lock (sync) { entry.CancellationFinished = true; DisposeIfFinished(entry); }
        }
    }

    private static void DisposeIfFinished(Entry entry)
    {
        if (!entry.Disposed && entry.Finished && entry.Waiters == 0 &&
            (!entry.CancellationStarted || entry.CancellationFinished))
        {
            entry.Disposed = true; entry.Stop.Dispose();
        }
    }

    public void Clear() => Reset(false);
    public void Dispose() => Reset(true);
    private void Reset(bool dispose)
    {
        Entry[] cancel;
        lock (sync)
        {
            disposed |= dispose;
            cancel = entries.Values.Where(MarkCancellation).ToArray();
            entries.Clear();
        }
        foreach (var entry in cancel) Cancel(entry);
    }
}
