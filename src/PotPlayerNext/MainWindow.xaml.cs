using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PotPlayerNext.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace PotPlayerNext;

public sealed partial class MainWindow : Window
{
    private readonly ThumbnailService thumbnails = new();
    private readonly Dictionary<ListViewItem, CancellationTokenSource> requests = new();
    private List<MediaItem> allItems = new();
    private IReadOnlyList<MediaItem>? filteredCatalog;
    private string? filteredKind, filteredQuery;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListViewItem, ThumbnailTarget> targets = new();
    private sealed record ThumbnailTarget(DependencyObject Root, Image? Image);
    private PreviewWindow? preview;
    private CancellationTokenSource? filterDebounce;
    private int scanVersion;
    private readonly LatestAsyncRequest previewRequests = new();
    private bool closed;
    private readonly WindowAppearance appearance;
    private PlaybackOptions playbackOptions = PlaybackSettings.Load();
    private bool settingsReady;

    public async Task HandleLaunchAsync(LaunchRequest request)
    {
        RuntimeEvidence.Write("launch-request", new { hasPath = request.Path is not null, hasError = request.Error is not null });
        if (request.Error is not null) { Report(new ArgumentException(request.Error)); return; }
        if (request.Path is null || closed) return;
        try
        {
            if (Directory.Exists(request.Path)) await LoadFolderAsync(request.Path);
            else await OpenPathAsync(request.Path);
        }
        catch (Exception error) { Report(error); }
    }

    public MainWindow()
    {
        InitializeComponent();
        appearance = new WindowAppearance(this, Root);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        SeekStep.Value = playbackOptions.SeekSeconds;
        HoldSpeed.IsOn = playbackOptions.HoldDoubleSpeed;
        PreviewProgress.IsOn = playbackOptions.ShowPreviewControls;
        ExplorerPreview.IsOn = playbackOptions.ExplorerPreviewEnabled;
        InitializeLibraryUi();
        settingsReady = true;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), true);
        Closed += (_, _) =>
        {
            closed = true; ++scanVersion; previewRequests.Invalidate();
            filterDebounce?.Cancel();
            appearance.Dispose(); CancelThumbnails(); thumbnails.Dispose(); preview?.Close();
        };
    }

    private void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

    private async void FolderPath_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        try
        {
            var path = System.IO.Path.GetFullPath(FolderLabel.Text.Trim().Trim('"'));
            if (Directory.Exists(path)) await LoadFolderAsync(path);
            else if (File.Exists(path)) await OpenPathAsync(path);
            else Status.Text = "指定的文件或文件夹不存在。";
        }
        catch (Exception error) { Report(error); }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
            var selected = await picker.PickSingleFolderAsync();
            if (selected is not null) await LoadFolderAsync(selected.Path);
        }
        catch (Exception error) { Report(error); }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await OpenPathAsync(file.Path);
        }
        catch (Exception error) { Report(error); }
    }

    private Task OpenPathAsync(string path)
    {
        // Probe the requested file independently: a shell launch must work beyond the scan limit.
        return previewRequests.RunAsync(() => NativeCatalog.ProbeAsync(path), probe =>
        {
            if (closed) return;
            if (probe.Error is not null) throw new IOException(probe.Error);
            if (probe.Items.Count != 1) throw new IOException("该文件不是已支持的图片或视频类型。");
            ShowPreview(probe.Items[0], quickPreview: false);
        });
    }

    private async Task<bool> LoadFolderAsync(string path)
    {
        var version = ++scanVersion;
        filterDebounce?.Cancel();
        previewRequests.Invalidate();
        preview?.Close();
        CancelThumbnails(); thumbnails.Clear();
        Loading.IsActive = true; Loading.Visibility = Visibility.Visible;
        Status.Text = "Rust 正在扫描目录…";
        try
        {
            var result = await NativeCatalog.ScanAsync(path);
            if (closed || version != scanVersion) return false;
            if (result.Error is not null) throw new IOException(result.Error);
            allItems = result.Items; FolderLabel.Text = path;
            ApplyFilter();
            if (RuntimeEvidence.Enabled) RuntimeEvidence.Write("library-catalog-ready", new { count = allItems.Count });
            Status.Text += $" · 跳过 {result.Skipped} 项" + (result.Truncated ? " · 达到 20,000 项限制，请拆分目录" : "");
            return true;
        }
        catch (Exception error) { if (!closed && version == scanVersion) Report(error); return false; }
        finally
        {
            if (!closed && version == scanVersion) { Loading.IsActive = false; Loading.Visibility = Visibility.Collapsed; }
        }
    }

    private void ApplyFilter()
    {
        if (MediaList is null || Search is null || KindFilter is null) return;
        var kind = KindFilter.SelectedIndex switch { 1 => "image", 2 => "video", _ => null };
        var query = Search.Text.Trim();
        if (ReferenceEquals(filteredCatalog, allItems) && kind == filteredKind && query == filteredQuery) return;
        filteredCatalog = allItems; filteredKind = kind; filteredQuery = query;
        CancelThumbnails();
        var filtered = MediaFilter.Apply(allItems, kind, query);
        MediaList.ItemsSource = filtered;
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count == 0)
        {
            var hasMedia = allItems.Count > 0;
            EmptyTitle.Text = hasMedia ? "没有匹配的媒体" : "文件夹中没有媒体";
            EmptyHint.Text = hasMedia ? "清除搜索，或切换媒体类型后重试。" : "选择其他文件夹，或拖入图片 / 视频。";
        }
        Status.Text = $"{filtered.Count:N0} 个媒体文件";
    }

    private async void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        filterDebounce?.Cancel();
        var cts = filterDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            if (!cts.IsCancellationRequested && !closed) ApplyFilter();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(filterDebounce, cts)) filterDebounce = null;
            cts.Dispose();
        }
    }
    private void KindFilter_Changed(object sender, SelectionChangedEventArgs e) { filterDebounce?.Cancel(); ApplyFilter(); }
    private void MediaList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        for (DependencyObject? element = e.OriginalSource as DependencyObject; element is not null && element != MediaList; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem)
            {
                ShowPreview(quickPreview: false);
                return;
            }
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Do not steal spaces or Enter from text entry and filter controls.
        for (DependencyObject? control = e.OriginalSource as DependencyObject; control is not null; control = VisualTreeHelper.GetParent(control))
            if (control is TextBox or ComboBox or Button or NumberBox or ToggleSwitch) return;
        if (e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            if (MediaList.SelectedItem is null) return;
            e.Handled = true;
            if (preview is not null && e.Key == VirtualKey.Space) preview.Close(); else ShowPreview(quickPreview: e.Key == VirtualKey.Space);
        }
        else if (e.Key == VirtualKey.Escape && preview is not null) { e.Handled = true; preview.Close(); }
    }

    private void ShowPreview(bool quickPreview = true)
    {
        if (MediaList.SelectedItem is not MediaItem item) return;
        ShowPreview(item, quickPreview);
    }

    private void ShowPreview(MediaItem item, bool quickPreview = true)
    {
        if (closed) return;
        previewRequests.Invalidate();
        preview?.Close();
        var opened = new PreviewWindow(item, PlaybackSettings.Load(), message => { if (!closed) Status.Text = message; }, quickPreview);
        preview = opened;
        opened.Closed += (_, _) => { if (ReferenceEquals(preview, opened)) preview = null; };
        opened.Activate();
        if (RuntimeEvidence.Enabled) RuntimeEvidence.Write("preview-activated", new { kind = item.Kind, fileToken = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.Path))) });
    }

    private async void MediaList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = (ListViewItem)args.ItemContainer;
        if (args.InRecycleQueue || args.Phase == 0)
        {
            if (requests.Remove(container, out var previous)) previous.Cancel();
            if (ThumbnailImage(container) is { } oldImage) oldImage.Source = null;
            // Phase 0 can still expose the recycled template's old Content. A cache hit
            // completes synchronously, so defer loading until WinUI assigns the new item.
            if (!args.InRecycleQueue) args.RegisterUpdateCallback(MediaList_ContainerContentChanging);
            return;
        }
        var image = ThumbnailImage(container);
        if (args.Item is not MediaItem item || image is null || closed) return;
        if (requests.Remove(container, out var replaced)) replaced.Cancel();
        var request = new CancellationTokenSource(); requests[container] = request;
        try
        {
            var thumbnail = await thumbnails.GetAsync(item, request.Token);
            if (!closed && !request.IsCancellationRequested &&
                requests.TryGetValue(container, out var current) && ReferenceEquals(current, request) &&
                ReferenceEquals(sender.ItemFromContainer(container), item))
            {
                image.Source = thumbnail;
                if (RuntimeEvidence.Enabled)
                {
                    var usage = thumbnails.CacheUsage;
                    RuntimeEvidence.Write("thumbnail-bound", new { available = thumbnail is not null, kind = item.Kind, entries = usage.Entries, estimatedBytes = usage.EstimatedBytes });
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (requests.TryGetValue(container, out var active) && ReferenceEquals(active, request)) requests.Remove(container);
            request.Dispose();
        }
    }

    private static Image? FindImage(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is Image image) return image;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            if (FindImage(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private Image? ThumbnailImage(ListViewItem container)
    {
        var root = container.ContentTemplateRoot;
        if (root is null) return null;
        if (targets.TryGetValue(container, out var target) && ReferenceEquals(root, target.Root)) return target.Image;
        targets.Remove(container);
        var image = FindImage(root); targets.Add(container, new(root, image));
        return image;
    }

    private void CancelThumbnails()
    {
        foreach (var request in requests.Values) request.Cancel();
        requests.Clear();
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.FirstOrDefault() is StorageFolder directory) await LoadFolderAsync(directory.Path);
            else if (items.FirstOrDefault() is StorageFile file) await OpenPathAsync(file.Path);
        }
        catch (Exception error) { Report(error); }
    }

    private void Report(Exception error)
    {
        if (closed) return;
        Status.Text = $"操作失败：{error.Message}";
        RuntimeEvidence.Write("operation-error", new { type = error.GetType().Name });
        Notice.Message = error.Message; Notice.Severity = InfoBarSeverity.Error; Notice.IsOpen = true;
    }

    private async void DefaultApps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            if (registration?.GetValue("PotPlayerNext") is null)
            {
                Notice.Message = "请先安装 MSI 安装包。安装后可在 Windows 默认应用中选择 PotPlayerNext。";
                Notice.Severity = InfoBarSeverity.Informational; Notice.IsOpen = true; return;
            }
            if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps?registeredAppUser=PotPlayerNext")))
                throw new IOException("未能打开 Windows 默认应用设置。");
        }
        catch (Exception error) { Report(error); }
    }

    private void SeekStep_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => SavePlaybackSettings();
    private void PlaybackSetting_Toggled(object sender, RoutedEventArgs e) => SavePlaybackSettings();
    private void SavePlaybackSettings()
    {
        if (!settingsReady) return;
        var desired = (playbackOptions with
        {
            SeekSeconds = SeekStep.Value,
            HoldDoubleSpeed = HoldSpeed.IsOn,
            ShowPreviewControls = PreviewProgress.IsOn,
            ExplorerPreviewEnabled = ExplorerPreview.IsOn
        }).Normalize();
        try
        {
            var saved = PlaybackSettings.Update(current => current with
            {
                SeekSeconds = desired.SeekSeconds != playbackOptions.SeekSeconds ? desired.SeekSeconds : current.SeekSeconds,
                HoldDoubleSpeed = desired.HoldDoubleSpeed != playbackOptions.HoldDoubleSpeed ? desired.HoldDoubleSpeed : current.HoldDoubleSpeed,
                ShowPreviewControls = desired.ShowPreviewControls != playbackOptions.ShowPreviewControls ? desired.ShowPreviewControls : current.ShowPreviewControls,
                ExplorerPreviewEnabled = desired.ExplorerPreviewEnabled != playbackOptions.ExplorerPreviewEnabled ? desired.ExplorerPreviewEnabled : current.ExplorerPreviewEnabled
            });
            playbackOptions = desired; preview?.UpdateOptions(saved);
        }
        catch (Exception error) { Report(error); }
    }

    private void Settings_Opened(object sender, object args)
    {
        settingsReady = false;
        playbackOptions = PlaybackSettings.Load();
        SeekStep.Value = playbackOptions.SeekSeconds; HoldSpeed.IsOn = playbackOptions.HoldDoubleSpeed;
        PreviewProgress.IsOn = playbackOptions.ShowPreviewControls; ExplorerPreview.IsOn = playbackOptions.ExplorerPreviewEnabled;
        SyncLibraryUiSettings();
        settingsReady = true;
    }

    private void ExplorerPreview_Toggled(object sender, RoutedEventArgs e)
    {
        if (!settingsReady) return;
        previewRequests.Invalidate();
        try
        {
            BackgroundPreviewHost.Configure(ExplorerPreview.IsOn);
            SavePlaybackSettings();
            Status.Text = ExplorerPreview.IsOn ? "资源管理器空格预览已启用：后台运行，关闭主界面后仍可预览；登录时自动启动。" : "后台空格预览已关闭。";
        }
        catch (Exception error) { Report(error); ExplorerPreview.IsOn = false; }
    }
}
