namespace PotPlayerNext.Services;

public sealed record MediaItem(string Path, string Name, string Kind, long Bytes)
{
    public string Details => $"{(Kind == "video" ? "视频" : "图片")} · {Bytes / 1048576d:0.##} MB";
}
public sealed record ScanResult(List<MediaItem> Items, int Skipped, bool Truncated, string? Error);
