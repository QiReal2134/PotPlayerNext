using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace PotPlayerNext.Controls;

/// <summary>Approved vector mark, animated with compositor-friendly transforms and opacity only.</summary>
public sealed partial class BrandLogo : UserControl
{
    public const int EntranceDurationMs = 850;
    private Storyboard? entrance;
    private TaskCompletionSource<bool>? finished;
    public BrandLogo() => InitializeComponent();

    public Task<bool> PlayEntranceAsync()
    {
        StopAnimation();
        finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        entrance = new Storyboard();
        Disk.Opacity = Play.Opacity = Next.Opacity = 0;
        Add(Disk, "Opacity", 0, 1, 0, 230);
        Add(Disk, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", .88, 1, 0, 330);
        Add(Disk, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)", .88, 1, 0, 330);
        Add(Play, "Opacity", 0, 1, 160, 180);
        Add(Play, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", .82, 1, 160, 260);
        Add(Next, "Opacity", 0, 1, 380, 180);
        Add(Next, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)", 7, 0, 380, 240);
        // Brief hold, without polling or decoding a GIF/video.
        entrance.Duration = new Duration(TimeSpan.FromMilliseconds(EntranceDurationMs));
        entrance.Completed += OnCompleted;
        entrance.Begin();
        return finished.Task;
    }
    private void Add(DependencyObject target, string property, double from, double to, int delay, int duration)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, BeginTime = TimeSpan.FromMilliseconds(delay),
            Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        entrance!.Children.Add(animation);
    }
    private void OnCompleted(object? sender, object args) => finished?.TrySetResult(true);
    public void StopAnimation()
    {
        if (entrance is not null)
        {
            entrance.Completed -= OnCompleted;
            entrance.Stop(); entrance = null;
        }
        finished?.TrySetResult(false); finished = null;
    }
}
