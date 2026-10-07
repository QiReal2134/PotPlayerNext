using Microsoft.UI.Xaml;
using System.Diagnostics;
using Microsoft.Win32;

namespace PotPlayerNext.Services;

/// <summary>A single per-user, hidden WinUI host; no catalog scan or polling for keys.</summary>
public sealed class BackgroundPreviewHost
{
    private static string Identity
    {
        get
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var scope = Environment.GetEnvironmentVariable("PPN_TEST_BACKGROUND_SCOPE");
            if (Environment.GetEnvironmentVariable("PPN_TEST_EVENTS") is not null && Guid.TryParse(scope, out var testId))
                identity += ".Test." + testId.ToString("N");
            return identity;
        }
    }
    private static string MutexName => @"Local\PotPlayerNext.Preview." + Identity;
    private static string StopName => @"Local\PotPlayerNext.Preview.Stop." + Identity;
    private readonly Window host = null!; // Duplicate instance exits before allocating any UI/hook.
    private readonly Mutex mutex;
    private readonly EventWaitHandle stop = null!;
    private readonly ExplorerSpacePreview bridge = null!;
    private readonly RegisteredWaitHandle shutdownWait = null!;
    private PreviewWindow? preview;
    private int generation;
    private bool closed;

    public BackgroundPreviewHost(Application application)
    {
        mutex = new Mutex(false, MutexName);
        var acquired = false;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); RuntimeEvidence.Write("background-duplicate"); application.Exit(); return; }
        host = new Window();
        try { host.AppWindow.IsShownInSwitchers = false; } catch { }
        host.AppWindow.Hide();
        stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopName);
        bridge = new ExplorerSpacePreview(action => host.DispatcherQueue.TryEnqueue(() => action()), async path => await OpenAsync(path));
        shutdownWait = ThreadPool.RegisterWaitForSingleObject(stop, (_, _) => host.DispatcherQueue.TryEnqueue(() => Shutdown(application)), null, Timeout.Infinite, executeOnlyOnce: true);
        RuntimeEvidence.Write("background-ready", new { shownInSwitchers = false, visible = host.AppWindow.IsVisible });
    }

    private async Task OpenAsync(string path)
    {
        var version = ++generation;
        try
        {
            var probe = await NativeCatalog.ProbeAsync(path);
            if (closed || version != generation || probe.Error is not null || probe.Items.Count != 1) return;
            preview?.Close();
            var opened = new PreviewWindow(probe.Items[0], PlaybackSettings.Load(), message => RuntimeEvidence.Write("preview-error", new { message }));
            preview = opened;
            opened.Closed += (_, _) => { if (preview == opened) preview = null; };
            opened.Activate();
            RuntimeEvidence.Write("preview-activated", new { fileToken = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))), mode = "background-preview" });
        }
        catch (Exception error) { RuntimeEvidence.Write("preview-error", new { message = error.Message }); }
    }

    private void Shutdown(Application application)
    {
        closed = true; ++generation; shutdownWait.Unregister(null); bridge.Dispose(); preview?.Close();
        stop.Dispose(); mutex.ReleaseMutex(); mutex.Dispose(); host.Close(); application.Exit();
    }

    public static void Configure(bool enabled)
    {
        // User-visible toggle is the only writer. No UserChoice/default-app changes.
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        using var preferences = Registry.CurrentUser.CreateSubKey(@"Software\PotPlayerNext\Preferences");
        preferences.SetValue("ExplorerPreviewEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new IOException("无法获取应用路径。");
            run.SetValue("PotPlayerNextPreview", $"\"{executable}\" --background");
            Process.Start(new ProcessStartInfo(executable, "--background") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        else
        {
            run.DeleteValue("PotPlayerNextPreview", false);
            SignalStop();
        }
    }

    private static void SignalStop()
    {
        try { using var signal = EventWaitHandle.OpenExisting(StopName); signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }
    // MSI maintenance helper: stop only this per-user host, never a media/library window.
    public static void StopAndWait()
    {
        // Preserve the stop signal if a just-starting host has acquired its mutex
        // but has not opened the event yet. Keep this handle alive while waiting.
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, StopName);
        signal.Set();
        using var running = new Mutex(false, MutexName);
        var acquired = false;
        try { acquired = running.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (acquired) running.ReleaseMutex();
        else Environment.ExitCode = 1;
    }
}
