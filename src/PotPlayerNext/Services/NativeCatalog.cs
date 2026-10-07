using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotPlayerNext.Services;

public static class NativeCatalog
{
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ppn_scan_folder([MarshalAs(UnmanagedType.LPUTF8Str)] string folder);
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ppn_probe_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ppn_string_free(IntPtr value);
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint ppn_string_length(IntPtr value);

    public static Task<ScanResult> ScanAsync(string folder) => ReadAsync(() => ppn_scan_folder(folder));
    public static Task<ScanResult> ProbeAsync(string path) => ReadAsync(() => ppn_probe_file(path));

    private static Task<ScanResult> ReadAsync(Func<IntPtr> operation) => Task.Run(() =>
    {
        var ptr = operation();
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("Rust 核心未返回目录数据。");
        try
        {
            return ReadUtf8(ptr, checked((int)ppn_string_length(ptr)));
        }
        finally { ppn_string_free(ptr); }
    });

    // The Rust-owned bytes remain alive until the caller's finally frees them.
    // Avoid a second full-catalog UTF-16 string and reflective JSON metadata.
    internal static unsafe ScanResult ReadUtf8(IntPtr ptr, int length) =>
        JsonSerializer.Deserialize(new ReadOnlySpan<byte>((void*)ptr, length), CatalogJsonContext.Default.ScanResult)
        ?? throw new InvalidOperationException("Rust 核心返回了无效数据。");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScanResult))]
internal partial class CatalogJsonContext : JsonSerializerContext { }
