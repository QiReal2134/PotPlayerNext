using System.Text.Json;

namespace PotPlayerNext.Services;

public sealed record PlaybackOptions(double SeekSeconds = 1.5, bool HoldDoubleSpeed = true, bool ShowPreviewControls = false, bool ExplorerPreviewEnabled = true)
{
    public PlaybackOptions Normalize() => this with { SeekSeconds = double.IsFinite(SeekSeconds) ? Math.Clamp(SeekSeconds, 0.1, 120) : 1.5 };
}

public static class PlaybackSettings
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PotPlayerNext", "settings.json");
    public static PlaybackOptions Load(string? path = null)
    {
        try
        {
            using var stream = new FileStream(path ?? DefaultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (JsonSerializer.Deserialize<PlaybackOptions>(stream) ?? new()).Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(PlaybackOptions options, string? path = null)
        => Update(_ => options, path);

    // Serialize read-modify-write across independent normal/preview/library processes.
    public static PlaybackOptions Update(Func<PlaybackOptions, PlaybackOptions> update, string? path = null)
    {
        path = Path.GetFullPath(path ?? DefaultPath);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\PotPlayerNext.Settings." + key);
        var acquired = false;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("设置文件忙，请重试。");
        try
        {
            var options = update(Load(path)).Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
                const int maxAttempts = 5;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        File.Move(temporary, path, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(50 * attempt);
                    }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return options;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
