using PotPlayerNext.Services;

var count = 0;
Check("LRU eviction respects recent reads", () =>
{
    var cache = new LruCache<string, int>(2);
    cache.Set("a", 1); cache.Set("b", 2);
    Require(cache.TryGet("a", out var value) && value == 1);
    cache.Set("c", 3);
    Require(!cache.TryGet("b", out _) && cache.TryGet("a", out _) && cache.Count == 2);
});
Check("LRU replacement does not consume capacity", () =>
{
    var cache = new LruCache<int, int>(2);
    cache.Set(1, 10); cache.Set(2, 20); cache.Set(1, 30); cache.Set(3, 40);
    Require(cache.Count == 2 && cache.TryGet(1, out var value) && value == 30 && !cache.TryGet(2, out _));
});
Check("LRU bounds 10,000 insertions and clears nodes", () =>
{
    var cache = new LruCache<int, int>(128);
    for (var index = 0; index < 10_000; ++index) { cache.Set(index, index); Require(cache.Count <= 128); }
    cache.Clear(); Require(cache.Count == 0 && !cache.TryGet(9999, out _));
    cache.Set(1, 1); Require(cache.TryGet(1, out _) && cache.Count == 1);
});
Check("LRU comparer is honored", () =>
{
    var cache = new LruCache<string, int>(1, StringComparer.OrdinalIgnoreCase);
    cache.Set("FILE", 1); Require(cache.TryGet("file", out var value) && value == 1);
});
Check("LRU invalid capacity rejected", () => ExpectArgumentError(() => new LruCache<int, int>(0)));
Check("50MP image stays within decoded pixel budget", () => Require(ImageDecodeBudget.Calculate(10_000, 5_000, 2560) == (2560, 1280)));
Check("Tall panorama and extreme dimensions bounded", () =>
{
    Require(ImageDecodeBudget.Calculate(100, 100_000, 192) == (1, 192));
    Require(ImageDecodeBudget.Calculate(uint.MaxValue, uint.MaxValue, 2560) == (2560, 2560));
});
Check("Small images are not upscaled", () => Require(ImageDecodeBudget.Calculate(64, 32, 192) == (64, 32)));
Check("Unknown image dimensions get finite bounds", () => Require(ImageDecodeBudget.Calculate(0, 0, 192) == (192, 192)));
Check("Invalid decode budget rejected", () => ExpectArgumentError(() => ImageDecodeBudget.Calculate(100, 100, 0)));
Check("Forward tap seeks once and ignores autorepeat", () =>
{
    var key = new ForwardKeyGesture(); Require(key.Press(true)); Require(!key.Press(true));
    Require(key.Release() == ForwardKeyAction.Seek && key.Release() == ForwardKeyAction.None);
});
Check("Held forward restores speed without seeking", () =>
{
    var key = new ForwardKeyGesture(); key.Press(true);
    Require(key.HoldElapsed() && !key.HoldElapsed()); Require(key.Release() == ForwardKeyAction.RestoreRate);
});
Check("Disabled hold stays a single seek", () =>
{
    var key = new ForwardKeyGesture(); key.Press(false);
    Require(!key.HoldElapsed() && key.Release() == ForwardKeyAction.Seek);
});
Check("Focus loss cancels pending tap and restores held speed", () =>
{
    var key = new ForwardKeyGesture(); key.Press(true); Require(key.Cancel() == ForwardKeyAction.None);
    Require(key.Release() == ForwardKeyAction.None); key.Press(true); key.HoldElapsed();
    Require(key.Cancel() == ForwardKeyAction.RestoreRate && !key.HoldElapsed());
});
Check("Seek clamps both ends and uses fractional step", () =>
{
    Require(PlaybackSeek.Target(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), 1.5).TotalSeconds == 11.5);
    Require(PlaybackSeek.Target(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), -1.5) == TimeSpan.Zero);
    Require(PlaybackSeek.Target(TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(20), 1.5).TotalSeconds == 20);
});
Check("Playback defaults and invalid settings", () =>
{
    var options = new PlaybackOptions(); Require(options.SeekSeconds == 1.5 && options.HoldDoubleSpeed && !options.ShowPreviewControls);
    Require((options with { SeekSeconds = double.NaN }).Normalize().SeekSeconds == 1.5);
    Require((options with { SeekSeconds = 500 }).Normalize().SeekSeconds == 120);
});
Check("Settings persist and malformed JSON recovers", () =>
{
    var directory = Path.GetFullPath("artifacts/core-logic"); Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "settings-test.json");
    var options = new PlaybackOptions(3.5, false, true); PlaybackSettings.Save(options, path);
    Require(PlaybackSettings.Load(path) == options);
    File.WriteAllText(path, "invalid json"); Require(PlaybackSettings.Load(path) == new PlaybackOptions());
});
Check("Image and video fit preserve aspect ratio", () =>
{
    Require(PreviewGeometry.Fit(320, 180, 960, 640) == (960, 540));
    var video = PreviewGeometry.Fit(640, 480, 960, 640);
    Require(Math.Abs(video.Width - 640.0 * 640 / 480) < 0.000001 && Math.Abs(video.Height - 640) < 0.000001);
    Require(PreviewGeometry.Fit(0, 0, 960, 640) == (1, 1));
});
Check("Pan requires zoom and viewport overflow", () =>
{
    Require(!PreviewGeometry.CanPan(960, 540, 1, 960, 640));
    Require(!PreviewGeometry.CanPan(100, 100, 2, 960, 640));
    Require(PreviewGeometry.CanPan(960, 540, 2, 960, 640));
    Require(PreviewGeometry.CanPan(540, 640, 1.2, 960, 640));
});
Check("Center respects negative secondary monitor coordinates", () =>
{
    Require(PreviewGeometry.Center(0, 0, 1920, 1080, 960, 640) == (480, 220));
    Require(PreviewGeometry.Center(-1920, 40, 1920, 1040, 960, 640) == (-1440, 240));
});
Check("Resize corners and edges detected without affecting media center", () =>
{
    Require(PreviewGeometry.Edges(0, 0, 960, 640) == (PreviewEdges.Left | PreviewEdges.Top));
    Require(PreviewGeometry.Edges(958, 638, 960, 640) == (PreviewEdges.Right | PreviewEdges.Bottom));
    Require(PreviewGeometry.Edges(480, 320, 960, 640) == PreviewEdges.None);
});
Check("Resize keeps opposite edges stable and enforces minimum", () =>
{
    Require(PreviewGeometry.Resize(100, 100, 960, 640, -100, -80, PreviewEdges.Right | PreviewEdges.Bottom, 240, 160) == (100, 100, 860, 560));
    Require(PreviewGeometry.Resize(100, 100, 960, 640, 2000, 2000, PreviewEdges.Left | PreviewEdges.Top, 240, 160) == (820, 580, 240, 160));
});
Check("Shell launch preserves Unicode, spaces and percent literals", () =>
{
    var path = Path.GetFullPath("你好 image %1 & sample.bmp");
    Require(LaunchRequest.Parse(new[] { path }).Path == path);
    Require(LaunchRequest.Parse(new[] { "--", path }).Path == path);
});
Check("Launch parser accepts no arguments and rejects ambiguous input", () =>
{
    Require(LaunchRequest.Parse(Array.Empty<string>()) == new LaunchRequest());
    Require(LaunchRequest.Parse(new[] { "a.bmp", "b.bmp" }).Error is not null);
    Require(LaunchRequest.Parse(new[] { "--" }).Error is not null);
    Require(LaunchRequest.Parse(new[] { "" }).Error is not null);
});
Check("Normal launch, quick preview and background are distinct", () =>
{
    var path = Path.GetFullPath("fixture.mp4");
    Require(!LaunchRequest.Parse(new[] { path }).Preview);
    Require(LaunchRequest.Parse(new[] { "--preview", "--", path }).Preview);
    Require(LaunchRequest.Parse(new[] { "--background" }).Background);
    Require(LaunchRequest.Parse(new[] { "--stop-background" }).StopBackground);
    Require(LaunchRequest.Parse(new[] { "--preview" }).Error is not null);
});
Check("Explorer setting persists and old settings migrate", () =>
{
    var path = Path.GetFullPath("artifacts/core-logic/explorer-settings.json");
    var settings = new PlaybackOptions(2.5, true, false, true);
    PlaybackSettings.Save(settings, path); Require(PlaybackSettings.Load(path) == settings);
    File.WriteAllText(path, "{\"SeekSeconds\":3.5,\"HoldDoubleSpeed\":false,\"ShowPreviewControls\":true}");
    Require(PlaybackSettings.Load(path) == new PlaybackOptions(3.5, false, true, true));
});
Check("Settings read-modify-write preserves independent fields under contention", () =>
{
    var path = Path.GetFullPath("artifacts/core-logic/concurrent-settings.json");
    PlaybackSettings.Save(new PlaybackOptions(), path);
    Parallel.Invoke(
        () => { for (var i = 0; i < 30; i++) PlaybackSettings.Update(s => s with { SeekSeconds = 4.5 }, path); },
        () => { for (var i = 0; i < 30; i++) PlaybackSettings.Update(s => s with { ShowPreviewControls = true }, path); });
    Require(PlaybackSettings.Load(path) == new PlaybackOptions(4.5, true, true));
    Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "concurrent-settings.json.*.tmp").Length == 0);
});
Check("Image preview opens at its aspect ratio within work area", () =>
{
    Require(PreviewGeometry.ImageClientSize(320, 180, 1920, 1080) == (960, 540));
    Require(PreviewGeometry.ImageClientSize(400, 400, 1920, 1080) == (640, 640));
    Require(PreviewGeometry.ImageClientSize(180, 320, 1920, 1080) == (360, 640));
    var small = PreviewGeometry.ImageClientSize(4000, 1000, 800, 600);
    Require(small == (720, 180));
});
Console.WriteLine($"PASS: {count} production core-logic checks. No UI interaction performed.");
return 0;

void Check(string name, Action action) { action(); ++count; Console.WriteLine($"PASS: {name}"); }
static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
static void ExpectArgumentError(Action action)
{
    try { action(); } catch (ArgumentOutOfRangeException) { return; }
    throw new InvalidOperationException("Expected ArgumentOutOfRangeException.");
}
