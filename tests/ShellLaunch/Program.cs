using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 1) throw new ArgumentException("Pass the installed executable path.");
var executable = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath("artifacts/shell-launch"); Directory.CreateDirectory(directory);
var fixtures = Path.GetFullPath("artifacts/media-smoke");
var evidence = new List<object>();
var originalEvents = Environment.GetEnvironmentVariable("PPN_TEST_EVENTS");
var defaultExtension = Environment.GetEnvironmentVariable("PPN_TEST_DEFAULT_EXTENSION");
var directOnly = Environment.GetEnvironmentVariable("PPN_TEST_DIRECT_ONLY") == "1";
var quickPreview = Environment.GetEnvironmentVariable("PPN_TEST_QUICK_PREVIEW") == "1";
if (defaultExtension is not null && defaultExtension is not (".bmp" or ".mp4"))
    throw new ArgumentException("Default-route test supports .bmp or .mp4 only.");
var repetitions = int.TryParse(Environment.GetEnvironmentVariable("PPN_TEST_REPETITIONS"), out var requestedRepetitions)
    ? Math.Clamp(requestedRepetitions, 1, 10) : 1;
try
{
    for (var repetition = 0; repetition < repetitions; repetition++)
    foreach (var (kind, extension, progId) in new[] { ("image", ".bmp", "PotPlayerNext.Image"), ("video", ".mp4", "PotPlayerNext.Video") })
    {
        if (defaultExtension is not null)
        {
            if (extension != defaultExtension) continue;
            using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
            if (choice?.GetValue("ProgId") as string != progId)
                throw new InvalidOperationException("UserChoice does not select PotPlayerNext; test will not modify defaults.");
        }
        var fixture = Directory.GetFiles(fixtures, "*" + extension).FirstOrDefault() ?? throw new FileNotFoundException("Run scripts/test-media.ps1 first.");
        var path = Path.Combine(directory, "媒体 空格 %1 & fixture" + extension); File.Copy(fixture, path, overwrite: true);
        var expectedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        foreach (var viaShell in defaultExtension is not null ? new[] { true } : directOnly ? new[] { false } : new[] { false, true })
        {
            var eventsPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".jsonl");
            Environment.SetEnvironmentVariable("PPN_TEST_EVENTS", eventsPath);
            using var process = viaShell ? Shell.OpenWithClass(path, defaultExtension is null ? progId : null) : LaunchDirect(executable, path, quickPreview);
            try
            {
                object? windowContract = null;
                var watch = Stopwatch.StartNew(); var loaded = false;
                while (watch.Elapsed < TimeSpan.FromSeconds(30))
                {
                    if (process.HasExited) throw new InvalidOperationException($"Player exited unexpectedly: {ExitDescription(process)}; events: {eventsPath}");
                    if (File.Exists(eventsPath))
                    {
                        var events = new List<JsonElement>();
                        using var stream = new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream);
                        while (reader.ReadLine() is { } line)
                        {
                            try { events.Add(JsonSerializer.Deserialize<JsonElement>(line)); }
                            catch (JsonException) { } // Last line may still be in flight.
                        }
                        if (events.Any(e => e.GetProperty("name").GetString() is "preview-error" or "operation-error" or "ui-unhandled-error")) throw new InvalidOperationException($"Player reported an error; events: {eventsPath}");
                        var activated = events.Any(e => e.GetProperty("name").GetString() == "preview-activated" && e.GetProperty("data").GetProperty("fileToken").GetString() == expectedToken);
                        var decoded = events.Any(e => e.GetProperty("name").GetString() == (kind == "image" ? "image-decoded" : "video-opened") && e.GetProperty("data").GetProperty("width").GetInt32() > 0);
                        if (activated && decoded)
                        {
                            if (directOnly)
                            {
                                if (events.Any(e => e.GetProperty("name").GetString() is "library-window-created" or "splash-started")) throw new InvalidOperationException("File launch created a library or splash window.");
                                var mode = events.Single(e => e.GetProperty("name").GetString() == "media-window-mode").GetProperty("data");
                                if (mode.GetProperty("quickPreview").GetBoolean() != quickPreview ||
                                    mode.GetProperty("alwaysOnTop").GetBoolean() != quickPreview ||
                                    mode.GetProperty("shownInSwitchers").GetBoolean() == quickPreview ||
                                    mode.GetProperty("titleBar").GetBoolean() == quickPreview ||
                                    mode.GetProperty("hasBorder").GetBoolean() == quickPreview)
                                    throw new InvalidOperationException("Normal/preview window contract mismatch.");
                                if (quickPreview && kind == "image")
                                {
                                    var fit = events.Single(e => e.GetProperty("name").GetString() == "image-window-fit").GetProperty("data");
                                    var w = fit.GetProperty("width").GetInt32(); var h = fit.GetProperty("height").GetInt32();
                                    var mw = fit.GetProperty("mediaWidth").GetDouble(); var mh = fit.GetProperty("mediaHeight").GetDouble();
                                    if (Math.Abs(w * mh / mw - h) > 1) throw new InvalidOperationException("Image client aspect ratio leaves avoidable bars.");
                                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && mode.GetProperty("borderColorResult").GetInt32() != 0)
                                        throw new InvalidOperationException("DWM preview outline suppression failed.");
                                    windowContract = new { mode = mode.Clone(), imageFit = fit.Clone() };
                                }
                                else windowContract = new { mode = mode.Clone() };
                            }
                            loaded = true; break;
                        }
                    }
                    await Task.Delay(100);
                }
                if (!loaded) throw new TimeoutException("No requested preview activation and media decode evidence.");
                await Task.Delay(1200);
                if (process.HasExited) throw new InvalidOperationException("Player exited after media load.");
                evidence.Add(new { repetition = repetition + 1, kind, route = defaultExtension is not null ? "ShellExecuteEx current default, no class override" : viaShell ? "ShellExecuteEx explicit registered ProgID" : "quoted ArgumentList direct launch", passed = true, correctFile = true, decoded = true, windowContract });
                Console.WriteLine($"PASS: {kind}, {(defaultExtension is not null ? "current default (no class override)" : viaShell ? "registered shell class" : "direct launch")}, Unicode/space/%1/& path, preview activation + decode.");
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        }
    }
}
finally { Environment.SetEnvironmentVariable("PPN_TEST_EVENTS", originalEvents); }
File.WriteAllText(Path.Combine(directory, defaultExtension is null ? "results.json" : "default-results.json"), JsonSerializer.Serialize(new { passed = true, timestampUtc = DateTimeOffset.UtcNow, appDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(executable, ".dll")))), directOnly, quickPreview, scope = defaultExtension is null ? "Direct/explicit registered ProgID launch; direct-only also checks library absence and media window properties; not UserChoice or visual acceptance" : "Current UserChoice shell launch without class override; correct file activation and decode, not physical Explorer double-click or visual acceptance; defaults unchanged", evidence }, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static Process LaunchDirect(string executable, string path, bool quickPreview)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false };
    if (quickPreview) info.ArgumentList.Add("--preview");
    info.ArgumentList.Add("--"); info.ArgumentList.Add(path);
    return Process.Start(info) ?? throw new InvalidOperationException("No child process.");
}

static string ExitDescription(Process process)
{
    try { return process.ExitCode.ToString(); }
    catch (InvalidOperationException) { return "exit code unavailable for shell-attached process"; }
}

static class Shell
{
    // This invokes a registered handler without altering the user's real default application.
    public static Process OpenWithClass(string file, string? progId)
    {
        var info = new ExecuteInfo { Size = Marshal.SizeOf<ExecuteInfo>(), Mask = (progId is null ? 0u : 0x1u) | 0x40u | 0x100u, Verb = "open", File = file, Class = progId, Show = 1 };
        if (!ShellExecuteEx(ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (info.Process == IntPtr.Zero) throw new InvalidOperationException("Shell did not return a new process handle.");
        try { return Process.GetProcessById(checked((int)GetProcessId(info.Process))); }
        finally { CloseHandle(info.Process); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window;
        public string? Verb, File, Parameters, Directory;
        public int Show; public IntPtr Instance, IdList;
        public string? Class; public IntPtr ClassKey; public uint HotKey;
        public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ExecuteInfo info);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(IntPtr process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
