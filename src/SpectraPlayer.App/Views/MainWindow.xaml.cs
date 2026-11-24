using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SpectraPlayer.App.Controls;
using SpectraPlayer.App.Input;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Bookmarks;
using SpectraPlayer.Core.Playback;
using SpectraPlayer.Core.Playback.Mpv;

namespace SpectraPlayer.App.Views;

public partial class MainWindow : Window
{
    private const string MediaFilter =
        "Video and audio|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.ts;*.m2ts;*.mts;*.mpg;*.mpeg;*.3gp;*.ogv;*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus" +
        "|All files|*.*";

    private readonly string? _startupFile;
    private readonly IPlayerEngine _engine = new MpvEngine();
    private readonly ObservableCollection<Bookmark> _bookmarks = [];

    private readonly DispatcherTimer _ticker = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private string? _currentPath;
    private bool _engineReady, _updatingUi;
    private long _holdPositionUntil; // after a seek, keep showing the target until the player catches up

    // fullscreen state
    private bool _fullscreen;
    private WindowState _restoreState;
    private Rect _restoreBounds;
    private FullscreenOverlay? _overlay;

    public MainWindow(string? startupFile = null)
    {
        _startupFile = startupFile;
        _splashActive = startupFile is null; // launched with a video: no welcome hold, just play
        InitializeComponent();

        // Never open taller/wider than the usable screen area (small laptop screens would hide the control bar).
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width * 0.9);
        Height = Math.Min(Height, work.Height * 0.9);

        BookmarkList.ItemsSource = _bookmarks;
        _bookmarks.CollectionChanged += (_, _) => RefreshBookmarkUi();
        RefreshBookmarkUi();

        _library = CreateLibrary();
        InitializeNotes();
        InitializeSection();
        InitializeTranscript();
        InitializeWelcome();
        InitializeFocus();
        InitializeShortcuts();
        ContentRendered += (_, _) => BeginSplash(); // the welcome hold counts from when the window is really on screen
        LoadTabs();
        RestorePanelState();
        InitializePanel();
        _engine.EndReached += (_, _) => Dispatcher.BeginInvoke(OnEndReached);

        _ticker.Tick += OnTick;
        _overlayTimer.Tick += OnOverlayTimer;
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            Execute(PlayerAction.TogglePause);
        };

        _engine.MessageReceived += (_, e) => Dispatcher.BeginInvoke(() => OnEngineMessage(e.Args));
        Video.HandleCreated += (_, _) => Dispatcher.BeginInvoke(InitializeEngine, DispatcherPriority.Loaded);
    }

    // ---- engine ----------------------------------------------------------------------------------------------

    private void InitializeEngine()
    {
        try
        {
            _engine.Initialize(Video.Handle);

            // Key presses and mouse clicks on the video surface go to mpv, not WPF. Route them back as messages.
            SyncMpvKeys();
            _engine.BindKey("MBTN_LEFT", "spectra click");
            _engine.BindKey("MBTN_LEFT_DBL", "spectra dblclick");
            _engine.BindKey("MBTN_RIGHT", "spectra rclick");
            _engine.BindKey("WHEEL_UP", "spectra wheel up");
            _engine.BindKey("WHEEL_DOWN", "spectra wheel down");
            _engine.BindKey("MOUSE_MOVE", "spectra move");

            _engineReady = true;
            _ticker.Start();
        }
        catch (Exception ex) when (ex is MpvNotFoundException or MpvException)
        {
            Video.Visibility = Visibility.Collapsed;
            ShowWelcome(true);
            SetWelcomeNotice(ex.Message);
        }

        // Tabs and playlists work (and are shown) even if playback itself is unavailable.
        Forget(StartSessionAsync());
    }

    private void OnEngineMessage(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[0] != "spectra") return;

        switch (args[1])
        {
            case "action" when args.Count > 2 && Enum.TryParse<PlayerAction>(args[2], out var action):
                Execute(action);
                break;
            case "click": VideoClicked(); break;
            case "dblclick": VideoDoubleClicked(); break;
            case "rclick": ShowPlayerMenu(); break;
            case "wheel" when args.Count > 2:
                Execute(args[2] == "up" ? PlayerAction.VolumeUp : PlayerAction.VolumeDown);
                break;
            case "move": VideoPointerMoved(); break;
        }
    }

    // Mouse input on the video area. The embedded mpv window may or may not claim these events (it depends on how the
    // native windows hit-test), so both paths are wired to the same handlers: whichever window gets the event, one action runs.

    private void VideoClicked()
    {
        // Delay the pause toggle so a double-click (fullscreen) doesn't also flip pause.
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void VideoDoubleClicked()
    {
        _clickTimer.Stop();
        Execute(PlayerAction.ToggleFullscreen);
    }

    private void VideoPointerMoved()
    {
        if (!_fullscreen) return;
        VideoArea.Cursor = null;
        ShowOverlay();
    }

    private void OnVideoMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) VideoClicked();
        else if (e.ClickCount == 2) VideoDoubleClicked();
        e.Handled = true;
    }

    private void OnVideoRightClick(object sender, MouseButtonEventArgs e)
    {
        ShowPlayerMenu();
        e.Handled = true;
    }

    private void OnVideoWheel(object sender, MouseWheelEventArgs e) =>
        Execute(e.Delta > 0 ? PlayerAction.VolumeUp : PlayerAction.VolumeDown);

    private void OnVideoMouseMove(object sender, MouseEventArgs e) => VideoPointerMoved();

    private void OnTick(object? sender, EventArgs e)
    {
        var duration = _engine.Duration.TotalSeconds;
        var position = _engine.Position.TotalSeconds;
        var holding = Environment.TickCount64 < _holdPositionUntil;

        _updatingUi = true;
        try
        {
            Seek.Duration = duration;
            if (!holding) Seek.Position = position;
            TimeText.Text = $"{TimeFormat.Format(holding ? Seek.Position : position)} / {TimeFormat.Format(duration)}";

            PlayButton.Content = _engine.HasMedia && !_engine.IsPaused ? "" : "";
            MuteButton.Content = _engine.IsMuted ? "" : "";
            if (!VolumeSlider.IsMouseCaptureWithin) VolumeSlider.Value = Math.Min(100, _engine.Volume);
        }
        finally { _updatingUi = false; }

        TickNoteStamp();
        TickTranscript();
        UpdateSpeedButton();

        // Remember where the student is, every ~5 s of playback, so a crash or power cut loses almost nothing.
        if (++_progressTicks >= 50)
        {
            _progressTicks = 0;
            if (!_engine.IsPaused) SaveCurrentProgress();
        }
    }

    // ---- commands --------------------------------------------------------------------------------------------

    private void Execute(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.TogglePause:
                if (_engine.HasMedia) _engine.TogglePause(); else PromptOpenFile();
                break;
            case PlayerAction.SeekBack5: Skip(-5); break;
            case PlayerAction.SeekForward5: Skip(5); break;
            case PlayerAction.SeekBack30: Skip(-30); break;
            case PlayerAction.SeekForward30: Skip(30); break;
            case PlayerAction.VolumeUp: ChangeVolume(5); break;
            case PlayerAction.VolumeDown: ChangeVolume(-5); break;
            case PlayerAction.ToggleMute:
                _engine.IsMuted = !_engine.IsMuted;
                ShowOsd(_engine.IsMuted ? "Muted" : "Sound on");
                break;
            case PlayerAction.ToggleFullscreen: SetFullscreen(!_fullscreen); break;
            case PlayerAction.ExitFullscreen: if (_fullscreen) SetFullscreen(false); break;
            case PlayerAction.AddBookmark: AddBookmark(); break;
            case PlayerAction.ToggleBookmarks: TogglePanel(PanelView.Bookmarks); break;
            case PlayerAction.TogglePlaylist: TogglePanel(PanelView.Playlist); break;
            case PlayerAction.NewNote: BeginNote(); break;
            case PlayerAction.MarkSectionStart: MarkSection(start: true); break;
            case PlayerAction.MarkSectionEnd: MarkSection(start: false); break;
            case PlayerAction.PreviousBookmark: JumpToBookmark(forward: false); break;
            case PlayerAction.NextBookmark: JumpToBookmark(forward: true); break;
            case PlayerAction.PreviousVideo: PlayPrevious(); break;
            case PlayerAction.NextVideo: PlayNext(auto: false); break;
            case PlayerAction.NewTab: PromptAddTabs(); break;
            case PlayerAction.CloseTab: if (_activeTab is not null) Forget(CloseTabAsync(_activeTab)); break;
            case PlayerAction.NextTab: StepTab(+1); break;
            case PlayerAction.PreviousTab: StepTab(-1); break;
            case >= PlayerAction.GoToTab1 and <= PlayerAction.GoToTab9:
                GoToTabNumber(action - PlayerAction.GoToTab1);
                break;
            case PlayerAction.OpenFile: PromptOpenFile(); break;
            case PlayerAction.SpeedUp: ChangeSpeed(_engine.Speed + 0.25); break;
            case PlayerAction.SpeedDown: ChangeSpeed(_engine.Speed - 0.25); break;
            case PlayerAction.SpeedReset: ChangeSpeed(1); break;
            case PlayerAction.ShowShortcuts: ShowShortcuts(); break;
            case PlayerAction.OpenSettings: ShowSettings(); break;
            case PlayerAction.ShowFocus: TogglePanel(PanelView.Focus); break;
            case PlayerAction.ToggleFocusTimer: ToggleFocusFromKey(); break;
            case PlayerAction.FrameStep: if (_engine.HasMedia) _engine.FrameStep(); break;
            case PlayerAction.FrameBackStep: if (_engine.HasMedia) _engine.FrameBackStep(); break;
        }
    }

    private void Skip(double seconds)
    {
        if (_engine.HasMedia) _engine.SeekRelative(TimeSpan.FromSeconds(seconds));
    }

    private void ChangeVolume(double delta)
    {
        _engine.Volume = Math.Round(_engine.Volume + delta);
        ShowOsd($"Volume {_engine.Volume:0}%");
    }

    private void ChangeSpeed(double speed)
    {
        _engine.Speed = Math.Round(speed, 2);
        ShowOsd($"Speed {_engine.Speed:0.##}x");
    }

    private void ShowOsd(string text, double seconds = 1.5) => _engine.ShowText(text, TimeSpan.FromSeconds(seconds));

    private void PromptOpenFile()
    {
        if (!_engineReady) return;
        var dialog = new OpenFileDialog { Title = "Open video", Filter = MediaFilter };
        if (dialog.ShowDialog(this) == true) Forget(OpenPathAsync(dialog.FileName));
    }

    /// <returns>False if nothing was opened because the player engine isn't available.</returns>
    private bool OpenFile(string path, TimeSpan? start = null, bool paused = false)
    {
        if (!_engineReady) return false;

        ShowWelcome(false);
        SaveCurrentProgress(); // for whatever was playing before this
        ResetSection();
        _currentPath = path;
        _holdPositionUntil = 0;
        _saveAllowedAfter = Environment.TickCount64 + 1500; // don't record position 0 while the resume seek is still landing
        _engine.Load(path, start, paused);
        if (start is { } s) { Seek.Position = s.TotalSeconds; _holdPositionUntil = Environment.TickCount64 + 1000; }
        LoadBookmarks(path);
        LoadNotes(path);
        LoadTranscript(path);
        UpdateWindowTitle();
        return true;
    }

    private void StopPlayback()
    {
        SaveCurrentProgress();
        ResetSection();
        _engine.Stop();
        _currentPath = null;
        SetCurrentItem(null);
        _bookmarks.Clear();
        _notes.Clear();
        _subtitleFile = null;
        _transcript.Clear();
        _currentRow = null;
        UpdateWindowTitle();
        ShowWelcome(true);
    }

    // ---- bookmarks -------------------------------------------------------------------------------------------

    private void LoadBookmarks(string videoPath)
    {
        _bookmarks.Clear();
        var file = BookmarkLocator.FindExisting(videoPath);
        if (file is null) return;

        try
        {
            foreach (var b in PbfFile.Read(file)) _bookmarks.Add(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            ShowOsd($"Could not read bookmarks: {ex.Message}", 4);
        }
    }

    private void SaveBookmarks()
    {
        if (_currentPath is null) return;
        try
        {
            PbfFile.Write(BookmarkLocator.SidecarPath(_currentPath), _bookmarks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowOsd($"Could not save bookmarks: {ex.Message}", 4);
        }
    }

    private void InsertSorted(Bookmark bookmark)
    {
        var index = 0;
        while (index < _bookmarks.Count && _bookmarks[index].Position <= bookmark.Position) index++;
        _bookmarks.Insert(index, bookmark);
    }

    private void AddBookmark()
    {
        if (_currentPath is null || !_engine.HasMedia)
        {
            ShowOsd("Open a video first");
            return;
        }

        var position = TimeSpan.FromMilliseconds(Math.Round(_engine.Position.TotalMilliseconds));
        if (_bookmarks.Any(b => b.Position == position))
        {
            ShowOsd("Already bookmarked here");
            return;
        }

        var bookmark = new Bookmark(position);
        InsertSorted(bookmark);
        SaveBookmarks();
        ShowOsd($"Bookmark added  {bookmark.PositionText}");
    }

    private void JumpToBookmark(bool forward)
    {
        if (!_engine.HasMedia || _bookmarks.Count == 0) return;

        var now = _engine.Position;
        var target = forward
            ? _bookmarks.FirstOrDefault(b => b.Position > now + TimeSpan.FromSeconds(0.5))
            : _bookmarks.LastOrDefault(b => b.Position < now - TimeSpan.FromSeconds(1));

        if (target is null) ShowOsd(forward ? "No later bookmark" : "No earlier bookmark");
        else GoTo(target);
    }

    private void GoTo(Bookmark bookmark)
    {
        if (!_engine.HasMedia) return;
        _engine.Seek(bookmark.Position);
        Seek.Position = bookmark.Position.TotalSeconds;
        _holdPositionUntil = Environment.TickCount64 + 400;
        ShowOsd($"{bookmark.PositionText}  {bookmark.Name}");
    }

    private void RefreshBookmarkUi()
    {
        Seek.Markers = _bookmarks.Select(b => new SeekMarker(b.Position.TotalSeconds, b.Name)).ToList();
        BookmarkCount.Text = _bookmarks.Count == 0 ? string.Empty : _bookmarks.Count.ToString();
        BookmarkEmpty.Visibility = _bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenameBookmark(Bookmark bookmark)
    {
        var name = InputDialog.Ask(this, "Rename bookmark", $"Bookmark at {bookmark.PositionText}", bookmark.Name);
        if (name is null) return;
        bookmark.Name = name;
        RefreshBookmarkUi();
        SaveBookmarks();
    }

    private void DeleteBookmark(Bookmark bookmark)
    {
        _bookmarks.Remove(bookmark);
        SaveBookmarks();
    }

    // ---- fullscreen ------------------------------------------------------------------------------------------

    private void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        if (on && WelcomePanel.Visibility == Visibility.Visible) return; // nothing to show fullscreen
        _fullscreen = on;

        if (on)
        {
            _restoreState = WindowState;
            _restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            // The docked panel gives way; in fullscreen it slides in over the video instead (see MainWindow.Panel.cs).
            if (_peeking) EndPeek(dock: false);
            _panelGeneration++;
            SidePanel.BeginAnimation(WidthProperty, null);
            SidePanel.Width = PanelWidth;
            _slidingOut = false;
            SidePanel.Visibility = Visibility.Collapsed;
            TabBar.Visibility = Visibility.Collapsed;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            if (NativeMethods.TryGetMonitorBounds(this, out var bounds)) NativeMethods.MoveWindowPixels(this, bounds);

            // The control bar can't float over the native video surface, so it moves into its own window.
            Root.Children.Remove(ControlBar);
            _overlay = new FullscreenOverlay { Owner = this, Content = ControlBar };
            _overlay.PreviewKeyDown += OnPreviewKeyDown;
            FullscreenButton.Content = "";
        }
        else
        {
            _overlayTimer.Stop();
            VideoArea.Cursor = null;
            if (_peeking) EndPeek(dock: false);
            _focusHud?.HideNow();
            if (_overlay is not null)
            {
                _overlay.Content = null;
                _overlay.Close();
                _overlay = null;
            }
            Root.Children.Add(ControlBar);
            Grid.SetRow(ControlBar, 2);
            TabBar.Visibility = Visibility.Visible;

            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            if (_restoreState == WindowState.Maximized) WindowState = WindowState.Maximized;

            SidePanel.Visibility = _panelPinned ? Visibility.Visible : Visibility.Collapsed;
            FullscreenButton.Content = "";
        }
    }

    private void ShowOverlay()
    {
        if (_overlay is null) return;

        if (!_overlay.IsVisible && NativeMethods.TryGetMonitorBounds(this, out var bounds))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _overlay.Left = bounds.X / dpi.DpiScaleX;
            _overlay.Width = bounds.Width / dpi.DpiScaleX;
            _overlay.Show();
            _overlay.UpdateLayout();
            _overlay.Top = (bounds.Y + bounds.Height) / dpi.DpiScaleY - _overlay.ActualHeight;
            FitPeekToControlBar();
            UpdateFocusHud(); // the corner timer comes up with the control bar
        }

        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    private void OnOverlayTimer(object? sender, EventArgs e)
    {
        if (_overlay is not { IsVisible: true } overlay) { _overlayTimer.Stop(); return; }

        // Stay up while the pointer is on it or a control (e.g. the seek bar) holds the mouse.
        if (overlay.IsMouseOver || Mouse.Captured is not null) return;

        overlay.Hide();
        FitPeekToControlBar();
        UpdateFocusHud(); // ...and fades away with it
        _overlayTimer.Stop();
        if (_fullscreen) VideoArea.Cursor = Cursors.None; // hide the pointer along with the controls
    }

    private void ShowPlayerMenu()
    {
        var menu = (ContextMenu)FindResource("PlayerMenu");
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ---- window / input events -------------------------------------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e) => NativeMethods.UseDarkTitleBar(this);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        EndSplash(fade: false); // a key press skips the welcome hold
        if (Keyboard.FocusedElement is TextBox) return; // typing in a dialog or field

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (KeyMap.TryGetAction(key, Keyboard.Modifiers, out var action))
        {
            Execute(action);
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _ticker.Stop();
        _overlayTimer.Stop();
        _edgeTimer.Stop();
        _focusTicker.Stop();
        _splashTimer?.Stop();
        SaveFocus();
        _measureCts?.Cancel();
        _clickTimer.Stop();
        _transcribeCts?.Cancel();
        SaveCurrentProgress();
        _engine.Dispose();
        _library.Dispose();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => PromptOpenFile();
    private void OnPlayClick(object sender, RoutedEventArgs e) => Execute(PlayerAction.TogglePause);
    private void OnStopClick(object sender, RoutedEventArgs e) => StopPlayback();
    private void OnMuteClick(object sender, RoutedEventArgs e) => Execute(PlayerAction.ToggleMute);
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => Execute(PlayerAction.ToggleFullscreen);
    private void OnAddBookmarkClick(object sender, RoutedEventArgs e) => AddBookmark();
    private void OnToggleBookmarksClick(object sender, RoutedEventArgs e) => TogglePanel(PanelView.Bookmarks);
    private void OnTogglePlaylistClick(object sender, RoutedEventArgs e) => TogglePanel(PanelView.Playlist);
    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi) _engine.Volume = e.NewValue;
    }

    private void OnSeekRequested(object? sender, SeekRequestedEventArgs e)
    {
        if (!_engine.HasMedia) return;
        _engine.Seek(TimeSpan.FromSeconds(e.Seconds), e.IsFinal ? SeekMode.Exact : SeekMode.Fast);
        if (e.IsFinal) _holdPositionUntil = Environment.TickCount64 + 400;
    }

    // ---- bookmark list events --------------------------------------------------------------------------------

    private void OnBookmarkDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var onItem = e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(BookmarkList, d) is ListBoxItem;
        if (onItem && BookmarkList.SelectedItem is Bookmark b) GoTo(b);
    }

    private void OnBookmarkListKeyDown(object sender, KeyEventArgs e)
    {
        if (BookmarkList.SelectedItem is not Bookmark b) return;
        if (e.Key == Key.F2) { RenameBookmark(b); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteBookmark(b); e.Handled = true; }
    }

    private void OnBookmarkRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item) item.IsSelected = true; // so the context menu acts on what was clicked
    }

    private void OnBookmarkGoTo(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is Bookmark b) GoTo(b);
    }

    private void OnBookmarkRename(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is Bookmark b) RenameBookmark(b);
    }

    private void OnBookmarkDelete(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is Bookmark b) DeleteBookmark(b);
    }

    private void OnImportBookmarks(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null) { ShowOsd("Open a video first"); return; }

        var dialog = new OpenFileDialog { Title = "Import PotPlayer bookmarks", Filter = "PotPlayer bookmarks (*.pbf)|*.pbf|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var added = 0;
            foreach (var b in PbfFile.Read(dialog.FileName))
            {
                if (_bookmarks.Any(x => x.Position == b.Position && x.Name == b.Name)) continue;
                InsertSorted(b);
                added++;
            }
            SaveBookmarks();
            ShowOsd($"Imported {added} bookmark(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            MessageBox.Show(this, ex.Message, "Import bookmarks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExportBookmarks(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null) { ShowOsd("Open a video first"); return; }

        var dialog = new SaveFileDialog
        {
            Title = "Export bookmarks",
            Filter = "PotPlayer bookmarks (*.pbf)|*.pbf",
            FileName = Path.GetFileNameWithoutExtension(_currentPath) + ".pbf",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            PbfFile.Write(dialog.FileName, _bookmarks);
            ShowOsd("Bookmarks exported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export bookmarks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
