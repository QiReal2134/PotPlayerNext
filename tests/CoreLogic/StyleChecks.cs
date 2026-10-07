using PotPlayerNext.Services;

internal static class StyleChecks
{
    public static int Run()
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "core-logic", "styles", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string TestPath(string name) => Path.Combine(directory, name + ".json");
        var count = 0;
        void Check(string name, Action test)
        {
            test(); ++count; Console.WriteLine($"PASS: {name}");
        }

        Check("Beta UI is opt-in for defaults and existing positional callers", () =>
        {
            foreach (var options in new[] { new PlaybackOptions(), new PlaybackOptions(3.5, false, true), new PlaybackOptions(4.5, true, false, false) })
            {
                Require(!options.ExperimentalUiEnabled && options.UiStyle == LibraryUiStyles.Compact);
                Require(LibraryUiStyles.Effective(options) == LibraryUiStyles.Default);
            }
        });
        Check("Old settings migrate without activating beta UI or losing playback fields", () =>
        {
            var path = TestPath("legacy");
            File.WriteAllText(path, "{\"SeekSeconds\":3.5,\"HoldDoubleSpeed\":false,\"ShowPreviewControls\":true,\"ExplorerPreviewEnabled\":false}");
            Require(PlaybackSettings.Load(path) == new PlaybackOptions(3.5, false, true, false));
            File.WriteAllText(path, "{}");
            Require(PlaybackSettings.Load(path) == new PlaybackOptions());
        });
        Check("Style validation accepts known IDs and falls back from invalid or null values", () =>
        {
            Require(LibraryUiStyles.Normalize(" CINEMA ") == LibraryUiStyles.Cinema);
            Require(LibraryUiStyles.Normalize("Compact") == LibraryUiStyles.Compact);
            foreach (var style in new string?[] { null, "", "gallery", "future-style", LibraryUiStyles.Default })
            {
                var normalized = new PlaybackOptions(500, false, true, false, true, style!).Normalize();
                Require(normalized.UiStyle == LibraryUiStyles.Compact && normalized.SeekSeconds == 120);
                Require(normalized.ExperimentalUiEnabled && !normalized.HoldDoubleSpeed && normalized.ShowPreviewControls && !normalized.ExplorerPreviewEnabled);
            }
            var path = TestPath("null-style");
            File.WriteAllText(path, "{\"ExperimentalUiEnabled\":true,\"UiStyle\":null}");
            Require(PlaybackSettings.Load(path) == new PlaybackOptions(ExperimentalUiEnabled: true));
        });
        Check("Turning beta off restores default while retaining the selected style", () =>
        {
            var options = new PlaybackOptions(ExperimentalUiEnabled: true, UiStyle: LibraryUiStyles.Cinema);
            Require(LibraryUiStyles.Effective(options) == LibraryUiStyles.Cinema);
            options = (options with { ExperimentalUiEnabled = false }).Normalize();
            Require(LibraryUiStyles.Effective(options) == LibraryUiStyles.Default && options.UiStyle == LibraryUiStyles.Cinema);
            Require(LibraryUiStyles.Effective(options with { ExperimentalUiEnabled = true }) == LibraryUiStyles.Cinema);
        });
        Check("Evidence UI style overrides are gated, in-memory, and limited to known IDs", () =>
        {
            var options = new PlaybackOptions(4.5, false, true, false);
            foreach (var destination in new string?[] { null, "", " " })
                Require(ReferenceEquals(options, LibraryUiStyles.TestOverride(options, destination, LibraryUiStyles.Cinema)));
            foreach (var style in new string?[] { null, "", "gallery", "future-style" })
                Require(ReferenceEquals(options, LibraryUiStyles.TestOverride(options, "evidence.jsonl", style)));
            var cinema = LibraryUiStyles.TestOverride(options, "evidence.jsonl", " CINEMA ");
            Require(cinema == (options with { ExperimentalUiEnabled = true, UiStyle = LibraryUiStyles.Cinema }));
            Require(LibraryUiStyles.TestOverride(options, "evidence.jsonl", LibraryUiStyles.Compact) == (options with { ExperimentalUiEnabled = true }));
            Require(LibraryUiStyles.TestOverride(cinema, "evidence.jsonl", LibraryUiStyles.Default) == (cinema with { ExperimentalUiEnabled = false }));
            Require(!options.ExperimentalUiEnabled && options.UiStyle == LibraryUiStyles.Compact);
        });
        Check("Beta styles round-trip and remain off after a persisted opt-out", () =>
        {
            var path = TestPath("round-trip");
            foreach (var style in new[] { LibraryUiStyles.Compact, LibraryUiStyles.Cinema })
            {
                var options = new PlaybackOptions(3.5, false, true, false, true, style);
                PlaybackSettings.Save(options, path);
                Require(PlaybackSettings.Load(path) == options);
                PlaybackSettings.Update(current => current with { ExperimentalUiEnabled = false }, path);
                Require(PlaybackSettings.Load(path) == (options with { ExperimentalUiEnabled = false }));
            }
        });
        Check("Preview and normal setting updates preserve independent beta preferences", () =>
        {
            var path = TestPath("independent-updates");
            var initial = new PlaybackOptions(2.5, true, false, false, true, LibraryUiStyles.Cinema);
            PlaybackSettings.Save(initial, path);
            // Same merge shape used by PreviewWindow's transport toggle.
            PlaybackSettings.Update(current => current with { ShowPreviewControls = true }, path);
            PlaybackSettings.Update(current => current with { SeekSeconds = 4.5, HoldDoubleSpeed = false }, path);
            Require(PlaybackSettings.Load(path) == (initial with { ShowPreviewControls = true, SeekSeconds = 4.5, HoldDoubleSpeed = false }));
            PlaybackSettings.Update(current => current with { ExperimentalUiEnabled = false, UiStyle = LibraryUiStyles.Compact }, path);
            Require(PlaybackSettings.Load(path) == new PlaybackOptions(4.5, false, true, false));
        });
        Check("Concurrent playback, preview, and style updates do not erase each other", () =>
        {
            var path = TestPath("concurrent");
            PlaybackSettings.Save(new PlaybackOptions(), path);
            Parallel.Invoke(
                () => { for (var i = 0; i < 20; i++) PlaybackSettings.Update(current => current with { SeekSeconds = 5.5, ExplorerPreviewEnabled = false }, path); },
                () => { for (var i = 0; i < 20; i++) PlaybackSettings.Update(current => current with { ShowPreviewControls = true }, path); },
                () => { for (var i = 0; i < 20; i++) PlaybackSettings.Update(current => current with { ExperimentalUiEnabled = true, UiStyle = LibraryUiStyles.Cinema }, path); });
            Require(PlaybackSettings.Load(path) == new PlaybackOptions(5.5, true, true, false, true, LibraryUiStyles.Cinema));
            Require(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp").Length == 0);
        });
        Check("Repeated atomic settings replacements preserve updates and share-delete readers", () =>
        {
            var path = TestPath("atomic-replacements");
            var initial = new PlaybackOptions(3.5, true, false, false, true, LibraryUiStyles.Cinema);
            PlaybackSettings.Save(initial, path);
            var original = File.ReadAllBytes(path);
            // This reader deliberately remains open on the original file across replacements.
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int rounds = 3, updatesPerRound = 12;
            for (var round = 0; round < rounds; ++round)
            {
                Parallel.Invoke(
                    () => { for (var i = 0; i < updatesPerRound; ++i) PlaybackSettings.Update(current => current with { SeekSeconds = current.SeekSeconds + 1 }, path); },
                    () => { for (var i = 0; i < updatesPerRound; ++i) PlaybackSettings.Update(current => current with { ShowPreviewControls = true }, path); },
                    () => { for (var i = 0; i < updatesPerRound; ++i) PlaybackSettings.Update(current => current with { UiStyle = current.UiStyle == LibraryUiStyles.Cinema ? LibraryUiStyles.Compact : LibraryUiStyles.Cinema }, path); });
                Require(PlaybackSettings.Load(path) == (initial with { SeekSeconds = initial.SeekSeconds + (round + 1) * updatesPerRound, ShowPreviewControls = true }));
                Require(Directory.GetFiles(directory, Path.GetFileName(path) + ".*.tmp").Length == 0);
            }
            using var snapshot = new MemoryStream();
            reader.CopyTo(snapshot);
            Require(snapshot.ToArray().SequenceEqual(original));
        });
        if (OperatingSystem.IsWindows())
            Check("Read-only settings commit fails without altering destination or leaking staged files", () =>
            {
                var path = TestPath("read-only-destination");
                var initial = new PlaybackOptions(4.5, false, true, false, true, LibraryUiStyles.Cinema);
                PlaybackSettings.Save(initial, path);
                var original = File.ReadAllBytes(path);
                var attributes = File.GetAttributes(path);
                try
                {
                    File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                    var failed = false;
                    try { PlaybackSettings.Update(current => current with { SeekSeconds = 8.5, UiStyle = LibraryUiStyles.Compact }, path); }
                    catch (UnauthorizedAccessException error) when ((error.HResult & 0xffff) == 5) { failed = true; }
                    catch (IOException) { failed = true; }
                    Require(failed && File.ReadAllBytes(path).SequenceEqual(original));
                    Require(PlaybackSettings.Load(path) == initial);
                    Require(Directory.GetFiles(directory, Path.GetFileName(path) + ".*.tmp").Length == 0);
                }
                finally { File.SetAttributes(path, attributes); }
                // The mutex and path remain usable after a propagated commit failure.
                PlaybackSettings.Update(current => current with { SeekSeconds = 8.5 }, path);
                Require(PlaybackSettings.Load(path) == (initial with { SeekSeconds = 8.5 }));
            });
        return count;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Style assertion failed.");
    }
}
