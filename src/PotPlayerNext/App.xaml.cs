using Microsoft.UI.Xaml;

namespace PotPlayerNext;

public partial class App : Application
{
    private Window? window;
    private Services.BackgroundPreviewHost? background;
    public App()
    {
        // Evidence only when explicitly enabled; never suppress an unknown UI failure.
        UnhandledException += (_, args) => Services.RuntimeEvidence.Write("ui-unhandled-error",
            new { type = args.Exception.GetType().Name, hresult = args.Exception.HResult, message = args.Message, stack = args.Exception.StackTrace });
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var request = Services.LaunchRequest.Parse(Environment.GetCommandLineArgs().Skip(1));
        if (request.StopBackground) { Services.BackgroundPreviewHost.StopAndWait(); Exit(); return; }
        if (request.Background)
        {
            background = new Services.BackgroundPreviewHost(this);
            return;
        }
        if (request.Error is null && request.Path is not null && !System.IO.Directory.Exists(request.Path))
        {
            _ = OpenMediaAsync(request);
            return;
        }
        _ = OpenLibraryAsync(request);
    }
    private async Task OpenLibraryAsync(Services.LaunchRequest request)
    {
        SplashWindow? splash = null;
        try
        {
            // File-open, quick preview and the background host bypass this path entirely.
            var animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            Services.RuntimeEvidence.Write("library-startup-policy", new { animationsEnabled });
            if (request.Error is null && animationsEnabled)
            {
                splash = new SplashWindow(); window = splash; splash.Activate();
                if (!await splash.Completion) return;
            }
            var main = new MainWindow(); window = main; main.Activate();
            // Keep a live window before closing the splash to avoid last-window shutdown.
            splash?.Close(); splash = null;
            Services.RuntimeEvidence.Write("library-window-created");
            main.DispatcherQueue.TryEnqueue(async () => await main.HandleLaunchAsync(request));
        }
        catch (Exception error) { ShowError(error.Message); splash?.Close(); }
    }
    private async Task OpenMediaAsync(Services.LaunchRequest request)
    {
        try
        {
            Services.RuntimeEvidence.Write("launch-request", new { hasPath = true, hasError = false, mode = request.Preview ? "preview" : "open" });
            var probe = await Services.NativeCatalog.ProbeAsync(request.Path!);
            if (probe.Error is not null || probe.Items.Count != 1) throw new IOException(probe.Error ?? "不支持的媒体文件。");
            window = new PreviewWindow(probe.Items[0], Services.PlaybackSettings.Load(), ShowError, request.Preview);
            window.Activate();
            if (Services.RuntimeEvidence.Enabled) Services.RuntimeEvidence.Write("preview-activated", new { fileToken = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Path!))), mode = request.Preview ? "preview" : "open" });
        }
        catch (Exception error) { ShowError(error.Message); }
    }
    private void ShowError(string message)
    {
        Services.RuntimeEvidence.Write("operation-error", new { message });
        var error = new Window { Title = "PotPlayerNext · 打开失败", Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) } };
        error.AppWindow.Resize(new Windows.Graphics.SizeInt32(460, 180)); window = error; error.Activate();
    }
}
