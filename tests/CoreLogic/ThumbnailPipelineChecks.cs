using System.Diagnostics;
using PotPlayerNext.Services;

internal static class ThumbnailPipelineChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(string name, Action test)
        {
            test(); ++count; Console.WriteLine($"PASS: {name}");
        }
        async Task CheckAsync(string name, Func<Task> test)
        {
            await test().WaitAsync(TimeSpan.FromSeconds(5));
            ++count; Console.WriteLine($"PASS: {name}");
        }

        Check("Weighted LRU evicts enough old values for the byte budget", () =>
        {
            var cache = new LruCache<string, long>(128, 10, bytes => bytes);
            cache.Set("a", 4); cache.Set("b", 4); cache.TryGet("a", out _); cache.Set("c", 6);
            Require(cache.Count == 2 && cache.Weight == 10);
            Require(cache.TryGet("a", out _) && cache.TryGet("c", out _) && !cache.TryGet("b", out _));
            cache.Set("d", 9);
            Require(cache.Count == 1 && cache.Weight == 9 && cache.TryGet("d", out _));
        });
        Check("Weighted LRU replacement, removal and clear account bytes exactly", () =>
        {
            var cache = new LruCache<string, long>(128, 10, bytes => bytes);
            cache.Set("a", 6); cache.Set("b", 4); cache.Set("a", 2);
            Require(cache.Count == 2 && cache.Weight == 6);
            Require(cache.Remove("b") && !cache.Remove("missing") && cache.Weight == 2);
            cache.Clear(); Require(cache.Count == 0 && cache.Weight == 0);
            cache.Set("c", 10); Require(cache.TryGet("c", out _) && cache.Weight == 10);
        });
        Check("Over-budget values replace stale keys without retaining oversized data", () =>
        {
            var cache = new LruCache<string, long>(2, 10, bytes => bytes);
            cache.Set("a", 4); cache.Set("b", 4); cache.Set("a", 11);
            Require(!cache.TryGet("a", out _) && cache.TryGet("b", out _) && cache.Weight == 4);
        });
        Check("Weighted LRU also limits zero-weight entry count", () =>
        {
            var cache = new LruCache<int, long>(2, 100, bytes => bytes);
            for (var i = 0; i < 100; i++) cache.Set(i, 0);
            Require(cache.Count == 2 && cache.Weight == 0 && !cache.TryGet(97, out _));
        });
        Check("Weighted LRU rejects invalid budgets and negative sizes", () =>
        {
            ExpectArgumentError(() => new LruCache<int, long>(2, 0, bytes => bytes));
            var cache = new LruCache<int, long>(2, 10, bytes => bytes); cache.Set(1, 2);
            ExpectArgumentError(() => cache.Set(1, -1));
            Require(cache.TryGet(1, out var value) && value == 2 && cache.Weight == 2);
        });
        Check("Weighted LRU never overflows long accounting", () =>
        {
            var cache = new LruCache<int, long>(2, long.MaxValue, bytes => bytes);
            cache.Set(1, long.MaxValue); cache.Set(2, long.MaxValue);
            Require(cache.Count == 1 && cache.Weight == long.MaxValue && !cache.TryGet(1, out _));
        });
        Check("10,000 thumbnail cache insertions remain within 12MiB and 128 entries", () =>
        {
            const long budget = 12L * 1024 * 1024;
            const long thumbnailBytes = 192L * 192 * 4;
            var cache = new LruCache<int, long>(128, budget, bytes => bytes);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 10_000; i++)
            {
                cache.Set(i, thumbnailBytes);
                Require(cache.Count <= 128 && cache.Weight <= budget);
            }
            watch.Stop();
            Require(cache.Count == 85 && cache.Weight == 85 * thumbnailBytes);
            Console.WriteLine($"BENCH: weighted cache 10,000 inserts + bound assertions: {watch.Elapsed.TotalMilliseconds:0.00} ms; retained {cache.Count} × {thumbnailBytes} bytes.");
        });
        Check("Concurrent cache writes preserve count and byte bounds", () =>
        {
            var cache = new LruCache<int, long>(128, 12L * 1024 * 1024, bytes => bytes);
            Parallel.For(0, 10_000, i =>
            {
                cache.Set(i, 192L * 192 * 4);
                Require(cache.Count <= 128 && cache.Weight <= 12L * 1024 * 1024);
            });
        });
        Check("128 landscape thumbnail entries fit the byte budget but still obey count eviction", () =>
        {
            var size = ImageDecodeBudget.Calculate(1920, 1080, 192);
            var bytes = size.Width * (long)size.Height * 4;
            var cache = new LruCache<int, long>(128, 12L * 1024 * 1024, value => value);
            for (var i = 0; i < 10_000; i++) cache.Set(i, bytes);
            Require(cache.Count == 128 && cache.Weight == bytes * 128 && cache.Weight < 12L * 1024 * 1024);
            Require(!cache.TryGet(9871, out _) && cache.TryGet(9872, out _));
        });
        Check("Negative thumbnail cache expires at 30 seconds without sliding on reads", () =>
        {
            var clock = new ManualClock();
            var cache = new ExpiringKeyCache<string>(256, TimeSpan.FromSeconds(30), clock: clock);
            cache.Add("broken"); clock.Advance(TimeSpan.FromSeconds(29)); Require(cache.Contains("broken"));
            clock.Advance(TimeSpan.FromSeconds(1)); Require(!cache.Contains("broken") && cache.Count == 0);
        });
        Check("Negative thumbnail cache is bounded and honors recent reads", () =>
        {
            var cache = new ExpiringKeyCache<string>(2, TimeSpan.FromSeconds(30));
            cache.Add("a"); cache.Add("b"); Require(cache.Contains("a")); cache.Add("c");
            Require(cache.Count == 2 && !cache.Contains("b") && cache.Contains("a"));
            cache.Clear(); Require(cache.Count == 0 && !cache.Contains("c"));
        });
        Check("Negative cache TTL can renew only after a new failed attempt", () =>
        {
            var clock = new ManualClock();
            var cache = new ExpiringKeyCache<string>(256, TimeSpan.FromSeconds(30), clock: clock);
            cache.Add("broken"); clock.Advance(TimeSpan.FromSeconds(30)); Require(!cache.Contains("broken"));
            cache.Add("broken"); clock.Advance(TimeSpan.FromSeconds(29)); Require(cache.Contains("broken"));
            cache.Remove("broken"); Require(!cache.Contains("broken"));
            ExpectArgumentError(() => new ExpiringKeyCache<int>(2, TimeSpan.Zero));
        });
        Check("Cancelled loads cannot insert a negative thumbnail cache entry", () =>
        {
            var cache = new ExpiringKeyCache<string>(256, TimeSpan.FromSeconds(30));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Require(!cache.Add("cancelled", cancelled.Token));
            Require(cache.Count == 0 && !cache.Contains("cancelled"));
            Require(cache.Add("cancelled") && cache.Contains("cancelled"));
        });
        Check("Thumbnail cache identity differentiates changed files but not Windows path casing", () =>
        {
            var key = new ThumbnailCacheIdentity("C:\\Media\\a.jpg", "image", 100, 5, true);
            var comparer = ThumbnailCacheIdentity.Comparer;
            var equivalent = key with { Path = "c:\\media\\A.JPG" };
            Require(comparer.Equals(key, equivalent) && comparer.GetHashCode(key) == comparer.GetHashCode(equivalent));
            Require(!comparer.Equals(key, key with { LastWriteUtcTicks = 6 }));
            Require(!comparer.Equals(key, key with { Bytes = 101 }));
            Require(!comparer.Equals(key, key with { Exists = false }));
            Require(!comparer.Equals(key, key with { Kind = "video" }));
        });
        Check("Real file metadata invalidates same-length edits and negative missing-file results", () =>
        {
            var directory = Path.GetFullPath("artifacts/core-logic"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"thumbnail-identity-{Guid.NewGuid():N}.tmp");
            try
            {
                var missing = ThumbnailCacheIdentity.Read(path, "image", 4);
                File.WriteAllText(path, "AAAA");
                File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var original = ThumbnailCacheIdentity.Read(path, "image", 4);
                File.WriteAllText(path, "BBBB");
                File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc));
                var edited = ThumbnailCacheIdentity.Read(path, "image", 4);
                Require(!missing.Exists && original.Exists && original.Bytes == edited.Bytes);
                Require(!ThumbnailCacheIdentity.Comparer.Equals(missing, original));
                Require(!ThumbnailCacheIdentity.Comparer.Equals(original, edited));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
        await CheckAsync("100 thumbnail consumers coalesce into one provider call", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>();
            var source = Completion<string?>(); var calls = 0;
            Task<string?> Load(CancellationToken _) { calls++; return source.Task; }
            var requests = Enumerable.Range(0, 100).Select(_ => loads.GetAsync("image", Load, default)).ToArray();
            Require(calls == 1); source.SetResult("thumbnail");
            Require((await Task.WhenAll(requests)).All(image => image == "thumbnail"));
        });
        await CheckAsync("Last cancelled consumer removes queued provider work before file open", async () =>
        {
            using var workers = new SemaphoreSlim(1, 1);
            using var loads = new SharedAsyncLoads<string, string>();
            using var consumer = new CancellationTokenSource();
            await workers.WaitAsync();
            var providerStopped = Completion<bool>(); var opens = 0;
            async Task<string?> Load(CancellationToken token)
            {
                try
                {
                    await workers.WaitAsync(token);
                    try { token.ThrowIfCancellationRequested(); opens++; return "thumbnail"; }
                    finally { workers.Release(); }
                }
                catch (OperationCanceledException) { providerStopped.SetResult(true); return null; }
            }
            var queued = loads.GetAsync("image", Load, consumer.Token);
            consumer.Cancel(); Require(await queued is null); await providerStopped.Task;
            workers.Release();
            Require(opens == 0 && await loads.GetAsync("image", Load, default) == "thumbnail" && opens == 1);
        });
        return count;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }

    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Thumbnail pipeline regression failed."); }
    private static void ExpectArgumentError(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Expected ArgumentOutOfRangeException.");
    }
}
