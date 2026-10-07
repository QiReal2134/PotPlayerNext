using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PotPlayerNext.Services;
using Windows.Graphics;

namespace PotPlayerNext;

public sealed partial class SplashWindow : Window
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowAppearance appearance;
    private bool closed;
    public Task<bool> Completion => completion.Task;
    public SplashWindow()
    {
        InitializeComponent();
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
        }
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(360, work.Width); var height = Math.Min(300, work.Height);
        var center = PreviewGeometry.Center(work.X, work.Y, work.Width, work.Height, width, height);
        AppWindow.MoveAndResize(new RectInt32(center.X, center.Y, width, height));
        appearance = new WindowAppearance(this, Root);
        Root.Loaded += Animate;
        Closed += (_, _) =>
        {
            closed = true; Root.Loaded -= Animate;
            Logo.StopAnimation(); appearance.Dispose(); completion.TrySetResult(false);
        };
    }
    private async void Animate(object sender, RoutedEventArgs args)
    {
        Root.Loaded -= Animate;
        try
        {
            // AppWindow sizes are physical pixels; XAML dimensions are DIPs.
            var scale = Root.XamlRoot.RasterizationScale;
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)Math.Round(360 * scale), work.Width);
            var height = Math.Min((int)Math.Round(300 * scale), work.Height);
            var center = PreviewGeometry.Center(work.X, work.Y, work.Width, work.Height, width, height);
            AppWindow.MoveAndResize(new RectInt32(center.X, center.Y, width, height));
            RuntimeEvidence.Write("splash-started", new { durationMs = Controls.BrandLogo.EntranceDurationMs, author = "qireal", shownInSwitchers = false });
            Logo.Opacity = 1;
            if (!await Logo.PlayEntranceAsync() || closed) return;
            RuntimeEvidence.Write("splash-completed");
            completion.TrySetResult(true);
        }
        catch (Exception error)
        {
            RuntimeEvidence.Write("splash-error", new { message = error.Message });
            completion.TrySetResult(true);
        }
    }
}
