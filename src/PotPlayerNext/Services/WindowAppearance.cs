using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace PotPlayerNext.Services;

/// <summary>Native WinUI acrylic with live system light/dark tracking.</summary>
public sealed class WindowAppearance : IDisposable
{
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly UISettings settings = new();
    private readonly bool acrylic;
    private bool disposed;
    public WindowAppearance(Window window, FrameworkElement root)
    {
        this.window = window; this.root = root;
        acrylic = DesktopAcrylicController.IsSupported();
        if (acrylic) window.SystemBackdrop = new DesktopAcrylicBackdrop();
        root.UseLayoutRounding = true;
        Apply(); settings.ColorValuesChanged += ColorValuesChanged;
    }
    private void ColorValuesChanged(UISettings sender, object args) => window.DispatcherQueue.TryEnqueue(() => { if (!disposed) Apply(); });
    private void Apply()
    {
        var foreground = settings.GetColorValue(UIColorType.Foreground);
        root.RequestedTheme = foreground.R + foreground.G + foreground.B > 384 ? ElementTheme.Dark : ElementTheme.Light;
        if (root is Microsoft.UI.Xaml.Controls.Panel panel)
            panel.Background = new SolidColorBrush(acrylic ? Colors.Transparent : settings.GetColorValue(UIColorType.Background));
        var titleBar = window.AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
    }
    public void Dispose() { disposed = true; settings.ColorValuesChanged -= ColorValuesChanged; }
}
