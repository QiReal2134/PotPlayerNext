namespace PotPlayerNext.Services;

/// <summary>UI-thread coordinator: only the latest async operation may update the UI.</summary>
public sealed class LatestAsyncRequest
{
    private long version;
    public void Invalidate() => ++version;

    public async Task RunAsync<T>(Func<Task<T>> load, Action<T> apply)
    {
        var request = ++version;
        T result;
        try { result = await load(); }
        catch (Exception) when (request != version) { return; }
        // Apply here, on the captured UI context, without a second await/return gap.
        // Do not catch apply errors: apply itself may invalidate the request.
        if (request == version) apply(result);
    }
}
