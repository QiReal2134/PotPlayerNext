using PotPlayerNext.Services;

internal static class AsyncRequestChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        async Task Check(string name, Func<Task> test)
        {
            await test().WaitAsync(TimeSpan.FromSeconds(5));
            ++count; Console.WriteLine($"PASS: {name}");
        }
        await Check("One cancelled thumbnail consumer does not cancel another", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>();
            using var cancelled = new CancellationTokenSource();
            var source = Completion<string?>(); var calls = 0; CancellationToken shared = default;
            Task<string?> Load(CancellationToken token) { calls++; shared = token; return source.Task; }
            var first = loads.GetAsync("image", Load, cancelled.Token);
            var second = loads.GetAsync("image", Load, CancellationToken.None);
            cancelled.Cancel(); Require(await first is null && !shared.IsCancellationRequested && calls == 1);
            source.SetResult("thumbnail"); Require(await second == "thumbnail");
        });
        await Check("Cancelled old thumbnail completion cannot remove its replacement", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>();
            using var cancelled = new CancellationTokenSource();
            // Let the old provider continuation finish inline on SetResult, so the
            // identity-removal regression cannot pass merely due to scheduler timing.
            var oldSource = new TaskCompletionSource<string?>(); var replacement = Completion<string?>();
            var calls = 0; CancellationToken oldToken = default;
            var old = loads.GetAsync("image", token => { calls++; oldToken = token; return oldSource.Task; }, cancelled.Token);
            cancelled.Cancel(); Require(await old is null && oldToken.IsCancellationRequested);
            Task<string?> Load(CancellationToken _) { calls++; return replacement.Task; }
            var current = loads.GetAsync("image", Load, CancellationToken.None);
            // Simulate an OS provider that cannot be cancelled immediately.
            oldSource.SetResult("obsolete");
            var joined = loads.GetAsync("image", Load, CancellationToken.None);
            Require(calls == 2); replacement.SetResult("fresh");
            Require(await current == "fresh" && await joined == "fresh");
        });
        await Check("Synchronous thumbnail success/failure leaves no zombie entry", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>(); var calls = 0;
            Task<string?> Load(CancellationToken _) { calls++; return Task.FromResult<string?>("ok"); }
            Require(await loads.GetAsync("image", Load, default) == "ok");
            Require(await loads.GetAsync("image", Load, default) == "ok" && calls == 2);
            await ExpectFailure(() => loads.GetAsync("fault", _ => throw new IOException("fixture"), default));
            Require(await loads.GetAsync("fault", Load, default) == "ok");
        });
        await Check("Clear cancels old thumbnail generation, allowing same-path reload", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>();
            var source = Completion<string?>(); CancellationToken old = default;
            var task = loads.GetAsync("image", token => { old = token; return source.Task; }, default);
            loads.Clear(); Require(await task is null && old.IsCancellationRequested);
            Require(await loads.GetAsync("image", _ => Task.FromResult<string?>("new"), default) == "new");
            source.SetResult("old");
        });
        await Check("Dispose cancels pending thumbnail consumers and rejects new loads", async () =>
        {
            var loads = new SharedAsyncLoads<string, string>(); var source = Completion<string?>();
            var pending = loads.GetAsync("image", _ => source.Task, default);
            loads.Dispose(); Require(await pending is null);
            Require(await loads.GetAsync("image", _ => throw new Exception("must not run"), default) is null);
            source.SetResult("late"); loads.Dispose();
        });
        await Check("Already-cancelled consumer does not start a thumbnail load", async () =>
        {
            using var loads = new SharedAsyncLoads<string, string>(); using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Require(await loads.GetAsync("image", _ => throw new Exception("must not run"), cancelled.Token) is null);
        });
        await Check("Latest file probe wins even when older probe finishes last", async () =>
        {
            var requests = new LatestAsyncRequest(); var old = Completion<string>(); var current = Completion<string>();
            var opened = new List<string>();
            void Apply(string path) { opened.Add(path); requests.Invalidate(); }
            var a = requests.RunAsync(() => old.Task, Apply); var b = requests.RunAsync(() => current.Task, Apply);
            current.SetResult("B"); await b; old.SetResult("A"); await a;
            Require(opened.SequenceEqual(new[] { "B" }));
        });
        await Check("Folder switch or window close invalidates pending file probe", async () =>
        {
            var requests = new LatestAsyncRequest(); var source = Completion<string>(); var applied = false;
            var task = requests.RunAsync(() => source.Task, _ => applied = true);
            requests.Invalidate(); source.SetResult("old"); await task; Require(!applied);
        });
        await Check("Stale probe failure is ignored but current/apply failures propagate", async () =>
        {
            var requests = new LatestAsyncRequest(); var source = Completion<string>();
            var stale = requests.RunAsync(() => source.Task, _ => throw new Exception("must not apply"));
            requests.Invalidate(); source.SetException(new IOException("obsolete")); await stale;
            await ExpectFailure(() => requests.RunAsync(() => Task.FromException<string>(new IOException("current")), _ => { }));
            await ExpectFailure(() => requests.RunAsync(() => Task.FromResult("current"), _ => { requests.Invalidate(); throw new IOException("apply"); }));
        });
        await Check("Explorer or preview HWND dismisses the owned session only", () =>
        {
            var state = new PreviewSessionState(); var request = state.BeginOpen(new IntPtr(10));
            state.Opened(request, new IntPtr(20));
            Require(state.CaptureDismiss(new IntPtr(10)) == request && state.CaptureDismiss(new IntPtr(20)) == request);
            Require(state.CaptureDismiss(new IntPtr(30)) is null && state.CaptureDismiss(IntPtr.Zero) is null);
            Require(state.Dismiss(request) && state.CaptureDismiss(new IntPtr(10)) is null);
            return Task.CompletedTask;
        });
        await Check("Dismiss during first probe prevents delayed preview resurrection", () =>
        {
            var state = new PreviewSessionState(); var request = state.BeginOpen(new IntPtr(10));
            Require(state.CaptureDismiss(new IntPtr(10)) == request && state.Dismiss(request));
            Require(!state.IsCurrent(request)); state.Opened(request, new IntPtr(20)); state.EndOpen(request);
            Require(state.CaptureDismiss(new IntPtr(20)) is null); return Task.CompletedTask;
        });
        await Check("Queued old dismissal cannot close a newer preview", () =>
        {
            var state = new PreviewSessionState(); var old = state.BeginOpen(new IntPtr(10)); state.Opened(old, new IntPtr(20));
            var captured = state.CaptureDismiss(new IntPtr(20)); var current = state.BeginOpen(new IntPtr(30));
            state.Opened(current, new IntPtr(40)); Require(captured is not null && !state.Dismiss(captured.Value));
            Require(state.CaptureDismiss(new IntPtr(40)) == current); return Task.CompletedTask;
        });
        await Check("Opening key repeats do not create another preview", () =>
        {
            var keys = new PreviewKeyLatch();
            Require(keys.BeginDown(0x20));
            Require(!keys.BeginDown(0x20) && !keys.IsSuppressed(0x20));
            Require(!keys.EndUp(0x20) && keys.BeginDown(0x20));
            return Task.CompletedTask;
        });
        await Check("Dismissal remains suppressed until release after foreground changes", () =>
        {
            var keys = new PreviewKeyLatch();
            foreach (var key in new[] { 0x20, 0x1B })
            {
                Require(keys.BeginDown(key)); keys.Suppress(key);
                Require(!keys.BeginDown(key) && keys.IsSuppressed(key));
                Require(keys.EndUp(key) && !keys.IsSuppressed(key));
                Require(keys.BeginDown(key)); Require(!keys.EndUp(key));
            }
            return Task.CompletedTask;
        });
        return count;
    }
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Async request regression failed."); }
    private static async Task ExpectFailure(Func<Task> operation)
    {
        try { await operation(); } catch (IOException) { return; }
        throw new InvalidOperationException("Expected IOException.");
    }
}
