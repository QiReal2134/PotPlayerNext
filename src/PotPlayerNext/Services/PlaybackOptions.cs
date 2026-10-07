using System.Text.Json;

namespace PotPlayerNext.Services;

public sealed record PlaybackOptions(double SeekSeconds = 1.5, bool HoldDoubleSpeed = true, bool ShowPreviewControls = false, bool ExplorerPreviewEnabled = true,
    bool ExperimentalUiEnabled = false, string UiStyle = LibraryUiStyles.Compact)
{
    public PlaybackOptions Normalize() => this with
    {
        SeekSeconds = double.IsFinite(SeekSeconds) ? Math.Clamp(SeekSeconds, 0.1, 120) : 1.5,
        UiStyle = LibraryUiStyles.Normalize(UiStyle)
    };
}

public static class LibraryUiStyles
{
    public const string Default = "default";
    public const string Compact = "compact";
    public const string Cinema = "cinema";

    public static string Normalize(string? style) =>
        string.Equals(style?.Trim(), Cinema, StringComparison.OrdinalIgnoreCase) ? Cinema : Compact;

    // The opt-in flag is the only way to activate beta styles, including migrated settings.
    public static string Effective(PlaybackOptions options) => options.ExperimentalUiEnabled ? Normalize(options.UiStyle) : Default;

    // Evidence-only, in-memory startup override; unknown values never opt a user in.
    public static PlaybackOptions TestOverride(PlaybackOptions options, string? evidenceDestination, string? requestedStyle)
    {
        if (string.IsNullOrWhiteSpace(evidenceDestination)) return options;
        requestedStyle = requestedStyle?.Trim();
        if (string.Equals(requestedStyle, Compact, StringComparison.OrdinalIgnoreCase)) return options with { ExperimentalUiEnabled = true, UiStyle = Compact };
        if (string.Equals(requestedStyle, Cinema, StringComparison.OrdinalIgnoreCase)) return options with { ExperimentalUiEnabled = true, UiStyle = Cinema };
        if (string.Equals(requestedStyle, Default, StringComparison.OrdinalIgnoreCase)) return options with { ExperimentalUiEnabled = false };
        return options;
    }
}

public static class PlaybackSettings
{
    private static readonly JsonSerializerOptions SerializationOptions = new() { WriteIndented = true };
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
                File.WriteAllText(temporary, JsonSerializer.Serialize(options, SerializationOptions));
                const int maxAttempts = 5;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        // Replacing an existing file atomically preserves readers opened
                        // with FileShare.Delete; keep the staged file on the same volume.
                        if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
                        else File.Move(temporary, path);
                        break;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(50 * attempt);
                    }
                    catch (UnauthorizedAccessException error) when (attempt < maxAttempts && OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 5)
                    {
                        // Windows scanners can briefly deny the commit; permanent access
                        // failures (including read-only files) still propagate at the bound.
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
