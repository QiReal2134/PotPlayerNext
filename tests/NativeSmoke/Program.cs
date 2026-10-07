using System.Diagnostics;
using System.Text.Json;
using PotPlayerNext.Services;

// Exercises the same C# P/Invoke adapter as production, with the actual Rust release DLL.
var root = Path.GetFullPath("artifacts/native-smoke");
Directory.CreateDirectory(root);
var run = Guid.NewGuid().ToString("N");
var fixture = Path.GetFullPath(Path.Combine(root, run));
Directory.CreateDirectory(fixture);
var evidence = new List<object>();
var success = false;
try
{
    var image = Path.Combine(fixture, "你好 image.PNG");
    var video = Path.Combine(fixture, "1 video.mp4");
    await File.WriteAllBytesAsync(image, new byte[7]);
    await File.WriteAllBytesAsync(video, new byte[11]);
    await File.WriteAllTextAsync(Path.Combine(fixture, "notes.txt"), "not media");
    Directory.CreateDirectory(Path.Combine(fixture, "nested.png"));
    await File.WriteAllBytesAsync(Path.Combine(fixture, "nested.png", "hidden.jpg"), new byte[5]);
    var scan = await NativeCatalog.ScanAsync(fixture);
    Require(scan.Error is null && scan.Items.Count == 2 && !scan.Truncated, "Scan result / filter / non-recursion");
    Require(scan.Items[0].Path == video && scan.Items[1].Path == image, "Unicode paths and case-insensitive extension");
    Require(scan.Items[0].Bytes == 11 && scan.Items[1].Bytes == 7, "Metadata bytes across ABI");
    evidence.Add(new { test = "C# to Rust scan ABI", passed = true, count = scan.Items.Count });
    var probe = await NativeCatalog.ProbeAsync(image);
    Require(probe.Error is null && probe.Items.Single().Path == image, "Probe ABI");
    Require((await NativeCatalog.ProbeAsync(Path.Combine(fixture, "notes.txt"))).Error is not null, "Unsupported file error");
    Require((await NativeCatalog.ScanAsync(Path.Combine(fixture, "missing"))).Error is not null, "Missing folder error");
    evidence.Add(new { test = "Single-file probe and JSON errors", passed = true });

    var watch = Stopwatch.StartNew();
    for (var index = 0; index < 1000; ++index)
    {
        var item = await NativeCatalog.ProbeAsync(image);
        Require(item.Items.Count == 1 && item.Error is null, "Repeated pointer allocation/free");
    }
    evidence.Add(new { test = "1,000 real ABI allocate/free calls", passed = true, milliseconds = watch.ElapsedMilliseconds });

    // Small empty files are metadata fixtures, not decoder or video-performance samples.
    var large = Path.Combine(fixture, "large"); Directory.CreateDirectory(large);
    for (var index = 0; index < 20_001; ++index) File.WriteAllBytes(Path.Combine(large, $"{index:D5}.jpg"), Array.Empty<byte>());
    watch.Restart();
    var limited = await NativeCatalog.ScanAsync(large);
    Require(limited.Error is null && limited.Items.Count == 20_000 && limited.Truncated, "Real 20,001-file truncation");
    evidence.Add(new { test = "20,001-file scan limit", passed = true, count = limited.Items.Count, milliseconds = watch.ElapsedMilliseconds });
    watch.Restart();
    Require((await NativeCatalog.ProbeAsync(Path.Combine(large, "20000.jpg"))).Items.Count == 1, "Probe works beyond folder scan limit");
    evidence.Add(new { test = "Probe independent of directory scan limit", passed = true, milliseconds = watch.ElapsedMilliseconds });
    success = true;
    Console.WriteLine("PASS: real Rust release DLL + production C# ABI adapter.");
}
catch (Exception error)
{
    evidence.Add(new { test = "Failure", passed = false, error = error.ToString() });
    Console.Error.WriteLine(error);
}
finally
{
    var report = JsonSerializer.Serialize(new { scope = "Rust release DLL and production C# P/Invoke adapter", passed = success, timestampUtc = DateTimeOffset.UtcNow, evidence }, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), report);
    await File.WriteAllTextAsync(Path.Combine(root, $"results-{run}.json"), report);
    Console.WriteLine(report);
    // Delete only this generated fixture: resolved target is checked against the intended root.
    if (!fixture.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(fixture) != run)
        throw new InvalidOperationException("Fixture cleanup scope check failed.");
    Directory.Delete(fixture, recursive: true);
}
return success ? 0 : 1;

static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException($"FAILED: {name}"); }
