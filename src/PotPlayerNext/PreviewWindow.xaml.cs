using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using Windows.Foundation;
using PotPlayerNext.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace PotPlayerNext;

public sealed partial class PreviewWindow : Window
{
    private MediaPlayer? player;
    private MediaSource? source;
    private bool closed;
    private bool active;
    private readonly MediaItem item;
    private PlaybackOptions options;
    private readonly Action<string> report;
    private readonly WindowAppearance appearance;
    private readonly ForwardKeyGesture forward = new();
    private readonly HashSet<VirtualKey> forwardKeys = new();
    private readonly DispatcherQueueTimer holdTimer;
    private double previousRate = 1;
    private double mediaWidth = 16, mediaHeight = 9;
    private uint? panPointer;
    private Point panStart;
    private double panHorizontal, panVertical;
    private bool movingWindow;
    private Windows.Graphics.PointInt32 windowStart;
    private Windows.Graphics.SizeInt32 windowSize;
    private PreviewEdges resizeEdges;
    private readonly bool quickPreview;
    private int? borderColorResult;

    public PreviewWindow(MediaItem item, PlaybackOptions options, Action<string> report, bool quickPreview = true)
    {
        this.item = item; this.options = options.Normalize(); this.report = report; this.quickPreview = quickPreview;
        InitializeComponent();
        Title = quickPreview ? $"预览 · {item.Name}" : $"{item.Name} — PotPlayerNext";
        AppWindow.IsShownInSwitchers = !quickPreview;
        SetBorderless(); CenterWindow(); appearance = new WindowAppearance(this, Root);
        RuntimeEvidence.Write("media-window-mode", new { quickPreview, shownInSwitchers = AppWindow.IsShownInSwitchers, alwaysOnTop = ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop, titleBar = ((OverlappedPresenter)AppWindow.Presenter).HasTitleBar, hasBorder = ((OverlappedPresenter)AppWindow.Presenter).HasBorder, borderColorResult });
        holdTimer = DispatcherQueue.CreateTimer();
        holdTimer.Interval = TimeSpan.FromMilliseconds(250); holdTimer.IsRepeating = false;
        holdTimer.Tick += (_, _) =>
        {
            if (!closed && player is not null && forward.HoldElapsed())
            {
                try { player.PlaybackSession.PlaybackRate = 2; }
                catch (Exception error) { CancelForward(); report($"倍速不可用：{error.Message}"); }
            }
        };
        // Tunnel before MediaTransportControls consumes arrows/Space, so one gesture
        // cannot both invoke its default command and our configurable shortcut.
        Root.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Root_KeyDown), true);
        Root.AddHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler(Root_KeyUp), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Root_PointerMoved), true);
        Root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Root_PointerReleased), true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Root_PointerWheelChanged), true);
        Root.PointerCaptureLost += (_, _) => { panPointer = null; movingWindow = false; resizeEdges = PreviewEdges.None; };
        Activated += (_, args) =>
        {
            // WinUI restores its last focused child on activation. Do not synchronously
            // refocus or release captures inside WM_ACTIVATE's native focus transition.
            active = args.WindowActivationState != WindowActivationState.Deactivated;
            if (active) QueueKeyboardFocus();
            else
                DispatcherQueue.TryEnqueue(() => { if (!closed) { CancelForward(); EndPan(); } });
        };
        Root.Loaded += (_, _) => QueueKeyboardFocus();
        Closed += (_, _) =>
        {
            CancelForward(); EndPan(); closed = true; appearance.Dispose(); Video.SetMediaPlayer(null);
            if (player is not null) { player.MediaFailed -= Player_MediaFailed; player.MediaOpened -= Player_MediaOpened; player.Pause(); player.Dispose(); player = null; }
            source?.Dispose(); source = null; Picture.Source = null;
        };
        Root.SizeChanged += (_, _) => ResizeMedia();
        ApplyOptions(); _ = LoadAsync();
    }

    private void QueueKeyboardFocus() => DispatcherQueue.TryEnqueue(() =>
    {
        if (closed || !active || Root.XamlRoot is null) return;
        // Give the first frame a keyboard target, without waiting for media decode
        // or refocusing synchronously inside WM_ACTIVATE. Keep existing control focus.
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is not null
            || ImageScroll.Focus(FocusState.Programmatic);
        RuntimeEvidence.Write("preview-keyboard-focus", new { focused });
    });

    private void SetBorderless()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.SetBorderAndTitleBar(!quickPreview, !quickPreview);
            presenter.IsAlwaysOnTop = quickPreview;
            if (quickPreview && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                uint noBorderColor = 0xFFFFFFFE; // DWMWA_COLOR_NONE: keep shadow, omit DWM outline.
                borderColorResult = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 34, ref noBorderColor, sizeof(uint));
            }
        }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, uint size);

    private void FitImageWindow()
    {
        if (!quickPreview || AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) return;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = PreviewGeometry.ImageClientSize(mediaWidth, mediaHeight, area.Width, area.Height);
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(size.Width, size.Height));
        var outer = AppWindow.Size;
        var position = PreviewGeometry.Center(area.X, area.Y, area.Width, area.Height, outer.Width, outer.Height);
        AppWindow.Move(new Windows.Graphics.PointInt32(position.X, position.Y));
        RuntimeEvidence.Write("image-window-fit", new { width = AppWindow.ClientSize.Width, height = AppWindow.ClientSize.Height, mediaWidth, mediaHeight });
    }
    private void CenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(960, area.Width); var height = Math.Min(640, area.Height);
        var position = PreviewGeometry.Center(area.X, area.Y, area.Width, area.Height, width, height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(position.X, position.Y, width, height));
    }
    public void UpdateOptions(PlaybackOptions updated) { CancelForward(); options = updated.Normalize(); ApplyOptions(); }
    private void ApplyOptions() { Video.AreTransportControlsEnabled = !quickPreview || options.ShowPreviewControls; TransportToggle.IsChecked = Video.AreTransportControlsEnabled; }
    private void ResizeMedia()
    {
        var size = PreviewGeometry.Fit(mediaWidth, mediaHeight, Root.ActualWidth, Root.ActualHeight);
        MediaSurface.Width = size.Width; MediaSurface.Height = size.Height;
    }
    private void Player_MediaOpened(MediaPlayer sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (closed || player != sender) return;
        var session = sender.PlaybackSession;
        RuntimeEvidence.Write("video-opened", new { width = session.NaturalVideoWidth, height = session.NaturalVideoHeight });
        if (session.NaturalVideoWidth > 0 && session.NaturalVideoHeight > 0)
        { mediaWidth = session.NaturalVideoWidth; mediaHeight = session.NaturalVideoHeight; ResizeMedia(); }
    });

    private async Task LoadAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            if (closed) return;
            if (item.Kind == "video")
            {
                Picture.Visibility = Visibility.Collapsed; Video.Visibility = Visibility.Visible;
                ResizeMedia();
                player = new MediaPlayer { AutoPlay = true }; player.MediaFailed += Player_MediaFailed; player.MediaOpened += Player_MediaOpened;
                source = MediaSource.CreateFromStorageFile(file);
                player.Source = source; Video.SetMediaPlayer(player); QueueKeyboardFocus();
            }
            else
            {
                using var stream = await file.OpenReadAsync();
                if (closed) return;
                var properties = await file.Properties.GetImagePropertiesAsync();
                if (closed) return;
                if (properties.Width > 0 && properties.Height > 0) { mediaWidth = properties.Width; mediaHeight = properties.Height; }
                var size = ImageDecodeBudget.Calculate(properties.Width, properties.Height, 2560);
                var bitmap = new BitmapImage { DecodePixelWidth = size.Width, DecodePixelHeight = size.Height };
                await bitmap.SetSourceAsync(stream);
                if (closed) return;
                Picture.Source = bitmap; FitImageWindow(); ResizeMedia(); QueueKeyboardFocus();
                RuntimeEvidence.Write("image-decoded", new { width = bitmap.PixelWidth, height = bitmap.PixelHeight });
            }
        }
        catch (Exception error) { Fail($"无法预览：{error.Message}"); }
    }

    private void Fail(string message) { if (!closed) { RuntimeEvidence.Write("preview-error"); report(message); Close(); } }
    private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => DispatcherQueue.TryEnqueue(() => Fail($"播放失败：{args.ErrorMessage}"));

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if ((e.Key is VirtualKey.Right or VirtualKey.D) && player is not null)
        {
            e.Handled = true;
            if (forwardKeys.Add(e.Key) && forwardKeys.Count == 1 && forward.Press(options.HoldDoubleSpeed))
            {
                previousRate = player.PlaybackSession.PlaybackRate;
                if (options.HoldDoubleSpeed) holdTimer.Start();
            }
        }
        else if ((e.Key is VirtualKey.Left or VirtualKey.A) && player is not null)
        {
            e.Handled = true; if (!e.KeyStatus.WasKeyDown) Seek(-options.SeekSeconds);
        }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; Close(); }
        else if (e.Key == VirtualKey.Space && !e.KeyStatus.WasKeyDown)
        {
            e.Handled = true;
            if (quickPreview) Close();
            else if (player is not null)
            {
                if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) player.Pause();
                else player.Play();
            }
        }
        else if (e.Key == VirtualKey.F && !e.KeyStatus.WasKeyDown)
        {
            e.Handled = true; CancelForward();
            var full = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            AppWindow.SetPresenter(full ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
            if (full) SetBorderless();
        }
        else if (e.Key == VirtualKey.Add || (int)e.Key == 187) { e.Handled = true; Zoom(1.25f); }
        else if (e.Key == VirtualKey.Subtract || (int)e.Key == 189) { e.Handled = true; Zoom(0.8f); }
    }

    private void Root_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Right or VirtualKey.D)) return;
        e.Handled = true;
        if (!forwardKeys.Remove(e.Key) || forwardKeys.Count != 0) return;
        holdTimer.Stop();
        switch (forward.Release())
        {
            case ForwardKeyAction.Seek: Seek(options.SeekSeconds); break;
            case ForwardKeyAction.RestoreRate: RestoreRate(); break;
        }
    }
    private void Seek(double seconds)
    {
        if (player is null) return;
        try
        {
            var session = player.PlaybackSession;
            if (session.CanSeek) session.Position = PlaybackSeek.Target(session.Position, session.NaturalDuration, seconds);
        }
        catch (Exception error) { report($"跳转失败：{error.Message}"); }
    }
    private void CancelForward() { holdTimer?.Stop(); forwardKeys.Clear(); if (forward.Cancel() == ForwardKeyAction.RestoreRate) RestoreRate(); }
    private void RestoreRate()
    {
        try { if (player is not null) player.PlaybackSession.PlaybackRate = previousRate; }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error.Message); }
    }
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;
        var edges = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? PreviewEdges.None :
            PreviewGeometry.Edges(point.Position.X, point.Position.Y, Root.ActualWidth, Root.ActualHeight);
        if (edges != PreviewEdges.None)
        {
            EndPan();
            if (!Root.CapturePointer(e.Pointer)) return;
            resizeEdges = edges; movingWindow = true; panPointer = e.Pointer.PointerId;
            windowStart = AppWindow.Position; windowSize = AppWindow.Size;
            panStart = EventScreenPointer(point.Position);
            e.Handled = true; return;
        }
        var bounds = MediaSurface.TransformToVisual(Root).TransformBounds(new Rect(0, 0, MediaSurface.ActualWidth, MediaSurface.ActualHeight));
        var inside = bounds.Contains(point.Position);
        if (inside)
        {
            // Leave visible transport controls interactive; background-only video supports panning.
            if (Video.AreTransportControlsEnabled && item.Kind == "video") return;
            if (PreviewGeometry.CanPan(MediaSurface.Width, MediaSurface.Height, ImageScroll.ZoomFactor, Root.ActualWidth, Root.ActualHeight))
            {
                if (!Root.CapturePointer(e.Pointer)) return;
                panPointer = e.Pointer.PointerId; panStart = point.Position;
                panHorizontal = ImageScroll.HorizontalOffset; panVertical = ImageScroll.VerticalOffset;
                e.Handled = true;
            }
            return; // Clicking a fully visible image/video never moves the window.
        }
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) return;
        EndPan();
        if (!Root.CapturePointer(e.Pointer)) return;
        movingWindow = true; panPointer = e.Pointer.PointerId;
        windowStart = AppWindow.Position;
        panStart = EventScreenPointer(point.Position);
        e.Handled = true;
    }
    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (panPointer != e.Pointer.PointerId)
        {
            var edges = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? PreviewEdges.None :
                PreviewGeometry.Edges(point.Position.X, point.Position.Y, Root.ActualWidth, Root.ActualHeight);
            var mediaBounds = MediaSurface.TransformToVisual(Root).TransformBounds(new Rect(0, 0, MediaSurface.ActualWidth, MediaSurface.ActualHeight));
            Root.SetCursor(edges, mediaBounds.Contains(point.Position) && PreviewGeometry.CanPan(MediaSurface.Width, MediaSurface.Height, ImageScroll.ZoomFactor, Root.ActualWidth, Root.ActualHeight));
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) { EndPan(); return; }
        if (movingWindow)
        {
            var scale = Root.XamlRoot.RasterizationScale;
            var screen = ScreenPointer(point.Position);
            var dx = (int)Math.Round(screen.X - panStart.X);
            var dy = (int)Math.Round(screen.Y - panStart.Y);
            if (resizeEdges == PreviewEdges.None) AppWindow.Move(new Windows.Graphics.PointInt32(windowStart.X + dx, windowStart.Y + dy));
            else
            {
                var rect = PreviewGeometry.Resize(windowStart.X, windowStart.Y, windowSize.Width, windowSize.Height, dx, dy, resizeEdges, (int)(240 * scale), (int)(160 * scale));
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
            }
            e.Handled = true; return;
        }
        ImageScroll.ChangeView(Math.Clamp(panHorizontal - (point.Position.X - panStart.X), 0, ImageScroll.ScrollableWidth),
            Math.Clamp(panVertical - (point.Position.Y - panStart.Y), 0, ImageScroll.ScrollableHeight), null, true);
        e.Handled = true;
    }
    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    { if (panPointer == e.Pointer.PointerId) { EndPan(); e.Handled = true; } }
    private void EndPan() { panPointer = null; movingWindow = false; resizeEdges = PreviewEdges.None; Root.ReleasePointerCaptures(); }
    private Point ScreenPointer(Point clientPoint)
    {
        // Moving the HWND can outrun XAML's queued coordinate transforms. Anchor mouse drags
        // to the OS screen position rather than adding the new HWND origin to a stale event.
        if (GetCursorPos(out var position)) return new Point(position.X, position.Y);
        var scale = Root.XamlRoot.RasterizationScale;
        return new Point(AppWindow.Position.X + clientPoint.X * scale, AppWindow.Position.Y + clientPoint.Y * scale);
    }
    private Point EventScreenPointer(Point clientPoint)
    {
        // Press events may be queued while the OS cursor has already advanced. Preserve
        // the event's original point and translate the client origin exactly once.
        var scale = Root.XamlRoot.RasterizationScale;
        var point = new CursorPoint { X = (int)Math.Round(clientPoint.X * scale), Y = (int)Math.Round(clientPoint.Y * scale) };
        if (ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this), ref point)) return new Point(point.X, point.Y);
        return new Point(AppWindow.Position.X + clientPoint.X * scale, AppWindow.Position.Y + clientPoint.Y * scale);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref CursorPoint point);
    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsHorizontalMouseWheel) return;
        Zoom((float)Math.Pow(1.2, point.Properties.MouseWheelDelta / 120.0)); e.Handled = true;
    }
    private void Zoom(float factor)
    {
        EndPan();
        var current = ImageScroll.ZoomFactor; var zoom = Math.Clamp(current * factor, 0.1f, 8f);
        // Keep the visible media center stable, including transitions out of centered fit mode.
        var x = ImageScroll.ScrollableWidth > 0 ? (ImageScroll.HorizontalOffset + Root.ActualWidth / 2) / current : MediaSurface.Width / 2;
        var y = ImageScroll.ScrollableHeight > 0 ? (ImageScroll.VerticalOffset + Root.ActualHeight / 2) / current : MediaSurface.Height / 2;
        ImageScroll.ChangeView(Math.Max(0, x * zoom - Root.ActualWidth / 2), Math.Max(0, y * zoom - Root.ActualHeight / 2), zoom, true);
    }
    private void Fit_Click(object sender, RoutedEventArgs e) { EndPan(); ImageScroll.ChangeView(0, 0, 1, true); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TransportToggle_Click(object sender, RoutedEventArgs e)
    {
        Video.AreTransportControlsEnabled = TransportToggle.IsChecked;
        if (!quickPreview) return;
        try
        {
            options = PlaybackSettings.Update(saved => saved with { ShowPreviewControls = TransportToggle.IsChecked });
        }
        catch (Exception error) { report($"设置保存失败：{error.Message}"); }
    }
    private async void External_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var executable = await picker.PickSingleFileAsync();
            if (executable is null || closed) return;
            var start = new System.Diagnostics.ProcessStartInfo(executable.Path) { UseShellExecute = false };
            start.ArgumentList.Add(item.Path); System.Diagnostics.Process.Start(start); player?.Pause();
        }
        catch (Exception error) { if (!closed) report($"外部打开失败：{error.Message}"); }
    }
}
