using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PotPlayerNext.Services;

if (args.Length != 2) throw new ArgumentException("Pass baseline and current Rust DLL paths.");
var root = Path.GetFullPath("artifacts/performance-030"); Directory.CreateDirectory(root);
var fixture = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var evidence = new List<object>();
try
{
    for (var i = 0; i < 400; i++) File.WriteAllBytes(Path.Combine(fixture, $"{i:D4} 你好.jpg"), []);
    for (var i = 0; i < 800; i++) File.WriteAllBytes(Path.Combine(fixture, $"{i:D4}.txt"), []);
    foreach (var (name, dll) in new[] { ("baseline", args[0]), ("current", args[1]) })
    {
        var path = Path.GetFullPath(dll);
        var library = NativeLibrary.Load(path);
        try
        {
            var scan = Marshal.GetDelegateForFunctionPointer<Scan>(NativeLibrary.GetExport(library, "ppn_scan_folder"));
            var free = Marshal.GetDelegateForFunctionPointer<Free>(NativeLibrary.GetExport(library, "ppn_string_free"));
            void Run() { var ptr = scan(fixture); if (ptr == IntPtr.Zero) throw new Exception("Native scan returned null."); free(ptr); }
            Run(); var watch = Stopwatch.StartNew();
            for (var i = 0; i < 50; i++) Run();
            evidence.Add(new { test = "Warm metadata scan 400 media + 800 non-media, 50 iterations; no decoding", name, milliseconds = watch.Elapsed.TotalMilliseconds,
                dllSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) });
        }
        finally { NativeLibrary.Free(library); }
    }
    // Same 20,000 records for both parser paths, allocated once outside timed loops.
    var catalog = new ScanResult(Enumerable.Range(0, 20_000).Select(i => new MediaItem(Path.Combine(fixture, $"{i:D5} 图片.jpg"), $"{i:D5} 图片.jpg", "image", i)).ToList(), 0, false, null);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, CatalogJsonContext.Default.ScanResult);
    var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
    try
    {
        Marshal.Copy(bytes, 0, buffer, bytes.Length); Marshal.WriteByte(buffer, bytes.Length, 0);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var (name, parse) in new (string, Func<ScanResult>)[] {
            ("baseline UTF16/reflection", () => JsonSerializer.Deserialize<ScanResult>(Marshal.PtrToStringUTF8(buffer)!, options)!),
            ("current native UTF8/source-generated metadata", () => NativeCatalog.ReadUtf8(buffer, bytes.Length)) })
        {
            if (parse().Items.Count != 20_000) throw new Exception("Parser benchmark invalid result.");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) if (parse().Items.Count != 20_000) throw new Exception("Parser result differs.");
            evidence.Add(new { test = "Same 20,000-record JSON, 20 warm parses, managed allocations only", name,
                milliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before, jsonBytes = bytes.Length });
        }
    }
    finally { Marshal.FreeHGlobal(buffer); }
    var report = JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow, scope = "Local warm microbench only; not cold startup, decoder/GPU RAM or all-device performance", evidence }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(root, "results.json"), report); Console.WriteLine(report);
}
finally
{
    if (!fixture.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(fixture), "N", out _)) throw new Exception("Cleanup scope failed.");
    Directory.Delete(fixture, true);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Scan([MarshalAs(UnmanagedType.LPUTF8Str)] string folder);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Free(IntPtr value);
