using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        UpdateWelcomeSubjects(); // (the shortcuts list is filled in by RefreshShortcutHints)

        // handledEventsToo: a child control that already looked at the drag must not stop it working window-wide
        AddHandler(DragOverEvent, new DragEventHandler(OnFileDragOver), handledEventsToo: true);
        AddHandler(DropEvent, new DragEventHandler(OnFileDrop), handledEventsToo: true);
    }

    // ---- start-up: the welcome screen is held for a few seconds, then fades into the playlist that was open ---------

    private const int SplashHoldMs = 3500;
    private static readonly TimeSpan SplashFade = TimeSpan.FromMilliseconds(600);

    private bool _splashActive = true;        // the welcome screen is being held at launch
    private bool _splashHidePending;          // a video was opened meanwhile: its picture waits for the hold to end
    private int _welcomeGeneration;           // a fade only finishes if nothing else has changed the welcome screen since
    private DispatcherTimer? _splashTimer;

    /// <summary>Starts the hold once the window is actually on screen. Not when launched with a video: that should just play.</summary>
    private void BeginSplash()
    {
        if (!_splashActive) return; // launched with a video (see the constructor), or already skipped

        _splashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SplashHoldMs) };
        _splashTimer.Tick += (_, _) => EndSplash(fade: true);
        _splashTimer.Start();
    }

    /// <summary>
    /// Ends the hold. If a session was restored behind it (or the user opened something), the welcome screen fades out to the
    /// video; if nothing is open it simply stays, as the normal welcome screen.
    /// </summary>
    private void EndSplash(bool fade)
    {
        if (!_splashActive) return;
        _splashActive = false;
        _splashTimer?.Stop();

        if (!_splashHidePending) return;
        _splashHidePending = false;
        HideWelcome(fade ? SplashFade : TimeSpan.Zero);
    }

    /// <summary>The first click or key press during the hold skips the rest of it.</summary>
    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) => EndSplash(fade: false);

    private void HideWelcome(TimeSpan fade)
    {
        var generation = ++_welcomeGeneration;
        if (fade <= TimeSpan.Zero)
        {
            WelcomePanel.BeginAnimation(OpacityProperty, null);
            WelcomePanel.Opacity = 1;
            ApplyWelcome(false);
            return;
        }

        // The video is a native window, so it can't be blended in underneath: the welcome screen fades to the black video
        // background and the picture takes its place.
        var animation = new DoubleAnimation(1, 0, fade) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        animation.Completed += (_, _) =>
        {
            if (generation != _welcomeGeneration) return;
            WelcomePanel.BeginAnimation(OpacityProperty, null);
            WelcomePanel.Opacity = 1;
            ApplyWelcome(false);
        };
        WelcomePanel.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>The video and the welcome screen share one spot (a native video window can't be drawn under WPF content).</summary>
    private void ShowWelcome(bool show)
    {
        if (!show && _splashActive)
        {
            _splashHidePending = true; // a session is ready: show it when the hold is over
            return;
        }

        // anything that changes the welcome screen cancels a fade that is still running
        _welcomeGeneration++;
        WelcomePanel.BeginAnimation(OpacityProperty, null);
        WelcomePanel.Opacity = 1;
        if (show) _splashHidePending = false;
        ApplyWelcome(show);
    }

    private void ApplyWelcome(bool show)
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
