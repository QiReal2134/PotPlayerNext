using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotPlayerNext.Services;

namespace PotPlayerNext;

public sealed partial class MainWindow
{
    private DataTemplate? originalMediaTemplate;
    private string appliedUiStyle = LibraryUiStyles.Default;
    private bool libraryUiTestOverride;

    private void InitializeLibraryUi()
    {
        originalMediaTemplate = MediaList.ItemTemplate;
        SyncLibraryUiSettings();
    }

    // Called only while settingsReady is false, so loading never saves or configures the shell.
    private void SyncLibraryUiSettings()
    {
        var overridden = LibraryUiStyles.TestOverride(playbackOptions,
            Environment.GetEnvironmentVariable("PPN_TEST_EVENTS"), Environment.GetEnvironmentVariable("PPN_TEST_UI_STYLE"));
        libraryUiTestOverride = !ReferenceEquals(overridden, playbackOptions);
        playbackOptions = overridden;
        ExperimentalUi.IsOn = playbackOptions.ExperimentalUiEnabled;
        UiStyle.SelectedIndex = playbackOptions.UiStyle == LibraryUiStyles.Cinema ? 1 : 0;
        UiStyle.IsEnabled = playbackOptions.ExperimentalUiEnabled;
        ApplyLibraryUiStyle();
    }

    private void ExperimentalUi_Toggled(object sender, RoutedEventArgs e) => SaveLibraryUiSettings();
    private void UiStyle_SelectionChanged(object sender, SelectionChangedEventArgs e) => SaveLibraryUiSettings();

    private void SaveLibraryUiSettings()
    {
        if (!settingsReady || closed) return;
        var desired = (playbackOptions with
        {
            ExperimentalUiEnabled = ExperimentalUi.IsOn,
            UiStyle = UiStyle.SelectedIndex == 1 ? LibraryUiStyles.Cinema : LibraryUiStyles.Compact
        }).Normalize();
        try
        {
            // Merge only the fields the user changed; preview and background processes own
            // independent settings and may have updated them since this flyout was opened.
            if (!libraryUiTestOverride)
                PlaybackSettings.Update(current => current with
                {
                    ExperimentalUiEnabled = desired.ExperimentalUiEnabled != playbackOptions.ExperimentalUiEnabled ? desired.ExperimentalUiEnabled : current.ExperimentalUiEnabled,
                    UiStyle = desired.UiStyle != playbackOptions.UiStyle ? desired.UiStyle : current.UiStyle
                });
            playbackOptions = desired;
            UiStyle.IsEnabled = desired.ExperimentalUiEnabled;
            ApplyLibraryUiStyle();
        }
        catch (Exception error)
        {
            settingsReady = false;
            try { SyncLibraryUiSettings(); }
            finally { settingsReady = true; }
            Report(error);
        }
    }

    private void ApplyLibraryUiStyle()
    {
        var style = LibraryUiStyles.Effective(playbackOptions);
        if (style == appliedUiStyle) { RecordLibraryUiStyle(style); return; }

        // Re-template the same ListView rather than rebuilding the collection or changing
        // its ItemsStackPanel: selection, filtering, and virtualization remain intact.
        CancelThumbnails();
        MediaList.ItemTemplate = style switch
        {
            LibraryUiStyles.Compact => (DataTemplate)Root.Resources["CompactMediaTemplate"],
            LibraryUiStyles.Cinema => (DataTemplate)Root.Resources["CinemaMediaTemplate"],
            _ => originalMediaTemplate
        };
        var compact = style == LibraryUiStyles.Compact;
        HeaderRow.Height = new GridLength(compact ? 68 : 76);
        HeaderLayout.Padding = compact ? new Thickness(24, 12, 144, 6) : new Thickness(24, 16, 144, 8);
        FilterLayout.Padding = compact ? new Thickness(24, 4, 24, 10) : new Thickness(24, 8, 24, 16);
        LibraryLayout.Margin = style == LibraryUiStyles.Cinema ? new Thickness(24, 0, 24, 0) : new Thickness(20, 0, 20, 0);
        FooterLayout.Padding = compact ? new Thickness(24, 8, 24, 8) : new Thickness(24, 12, 24, 12);
        BetaStyleBadge.Visibility = style == LibraryUiStyles.Default ? Visibility.Collapsed : Visibility.Visible;
        appliedUiStyle = style;
        // WindowAppearance continues to own theme/acrylic; quick previews have no beta chrome.
        RecordLibraryUiStyle(style);
    }

    private void RecordLibraryUiStyle(string style) => RuntimeEvidence.Write("library-ui-style", new
    {
        effective = style,
        experimental = playbackOptions.ExperimentalUiEnabled,
        template = style switch { LibraryUiStyles.Compact => "CompactMediaTemplate", LibraryUiStyles.Cinema => "CinemaMediaTemplate", _ => "original" },
        testOverride = libraryUiTestOverride
    });
}
