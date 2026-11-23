using System.Windows;
using System.Windows.Input;
using SpectraPlayer.App.Input;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.App.Views;

// The welcome screen (shown when nothing is open) and dropping videos or folders onto the window.
public partial class MainWindow
{
    private void InitializeWelcome()
    {
        WelcomeTabs.ItemsSource = _tabs;
        WelcomeShortcuts.ItemsSource = ShortcutGuide.Welcome;
        UpdateWelcomeSubjects();

        // handledEventsToo: a child control that already looked at the drag must not stop it working window-wide
        AddHandler(DragOverEvent, new DragEventHandler(OnFileDragOver), handledEventsToo: true);
        AddHandler(DropEvent, new DragEventHandler(OnFileDrop), handledEventsToo: true);
    }

    /// <summary>The video and the welcome screen share one spot (a native video window can't be drawn under WPF content).</summary>
    private void ShowWelcome(bool show)
    {
        WelcomePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) SetWelcomeNotice(null);
        if (Video.Visibility != Visibility.Collapsed) // collapsed for good if the player could not start
            Video.Visibility = show ? Visibility.Hidden : Visibility.Visible;
    }

    private void UpdateWelcomeSubjects() =>
        WelcomeSubjects.Visibility = _tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void SetWelcomeNotice(string? text)
    {
        WelcomeNotice.Text = text ?? string.Empty;
        WelcomeNotice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // A click on the welcome screen is not a click on a video: don't let it pause, fullscreen or open a file.
    private void OnWelcomeMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnWelcomeSubjectClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab }) Forget(ActivateTabAsync(tab, autoPlay: true));
    }

    // ---- dropping files and folders --------------------------------------------------------------------------

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Forget(OpenDroppedAsync(paths));
    }

    /// <summary>Folders become tabs (one subject each); a single video just plays.</summary>
    private async Task OpenDroppedAsync(string[] paths)
    {
        var folders = paths.Where(Directory.Exists).ToList();
        var video = paths.FirstOrDefault(p => File.Exists(p) && MediaExtensions.IsMedia(p));

        if (folders.Count > 0) await AddTabsAsync(folders);
        else if (video is not null) await OpenPathAsync(video);
        else if (WelcomePanel.Visibility == Visibility.Visible) SetWelcomeNotice("That isn't a video or a folder. Drop a video file, or a folder of videos.");
        else ShowOsd("Drop a video file or a folder of videos", 3);
    }
}
