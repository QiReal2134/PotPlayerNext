using System.Runtime.InteropServices;
using System.Text.Json;

namespace PotPlayerNext.Services;

public sealed record MediaItem(string Path, string Name, string Kind, long Bytes)
{
    public string Details => $"{(Kind == "video" ? "视频" : "图片")} · {Bytes / 1048576d:0.##} MB";
}
public sealed record ScanResult(List<MediaItem> Items, int Skipped, bool Truncated, string? Error);

public static class NativeCatalog
{
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ppn_scan_folder([MarshalAs(UnmanagedType.LPUTF8Str)] string folder);
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ppn_probe_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("potplayer_next_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ppn_string_free(IntPtr value);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Task<ScanResult> ScanAsync(string folder) => ReadAsync(() => ppn_scan_folder(folder));
    public static Task<ScanResult> ProbeAsync(string path) => ReadAsync(() => ppn_probe_file(path));

    private static Task<ScanResult> ReadAsync(Func<IntPtr> operation) => Task.Run(() =>
    {
        var ptr = operation();
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("Rust 核心未返回目录数据。");
        try
        {
            return JsonSerializer.Deserialize<ScanResult>(Marshal.PtrToStringUTF8(ptr)!, JsonOptions)
                ?? throw new InvalidOperationException("Rust 核心返回了无效数据。");
        }
        finally { ppn_string_free(ptr); }
    });
}
