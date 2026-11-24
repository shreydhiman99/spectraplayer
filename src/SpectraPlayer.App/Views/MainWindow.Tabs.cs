using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.App.Views;

// Tabs (one folder per subject), the playlist of the active tab, resume positions, and the side panel switcher.
public partial class MainWindow
{
    private enum PanelView { Playlist, Notes, Transcript, Bookmarks, Focus }

    private static readonly IReadOnlyList<MediaEntry> NoEntries = [];

    private readonly LibraryStore _library;
    private readonly ObservableCollection<TabViewModel> _tabs = [];
    private TabViewModel? _activeTab;
    private PlaylistItem? _currentItem;
    private bool _suppressTabSelection;
    private PanelView _panelView = PanelView.Playlist;
    private int _progressTicks;
    private long _saveAllowedAfter;
    private string? _libraryWarning;

    // ---- startup ---------------------------------------------------------------------------------------------

    private LibraryStore CreateLibrary()
    {
        try
        {
            return new LibraryStore(AppPaths.LibraryDatabasePath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _libraryWarning = $"Could not open your library ({ex.Message}). Tabs and progress won't be saved this session.";
            return new LibraryStore(":memory:");
        }
    }

    private void LoadTabs()
    {
        foreach (var record in _library.GetTabs()) _tabs.Add(new TabViewModel(record));
        TabList.ItemsSource = _tabs;

        var savedId = _library.GetSetting("activeTab");
        _activeTab = _tabs.FirstOrDefault(t => t.Id.ToString() == savedId) ?? _tabs.FirstOrDefault();
        SetSelectedTab(_activeTab);
        UpdateTabBarHint();
        UpdatePlaylistHeader(_activeTab, scanning: _activeTab is not null);
        PlaylistList.ItemsSource = _activeTab?.Items;
    }

    /// <summary>Called once the engine is ready (or has failed): restore the last session or open the requested file.</summary>
    private async Task StartSessionAsync()
    {
        if (_libraryWarning is not null) ShowOsd(_libraryWarning, 6);

        if (_startupFile is not null) await OpenPathAsync(_startupFile);
        else if (_activeTab is not null) await ActivateTabAsync(_activeTab, autoPlay: false); // resume paused: no surprise sound at launch
    }

    /// <summary>Opens a file: inside a tab's folder it plays as part of that tab's playlist, otherwise standalone.</summary>
    private async Task OpenPathAsync(string file)
    {
        var owner = _tabs.FirstOrDefault(t => IsInside(t.Folder, file));
        if (owner is not null)
        {
            await ActivateTabAsync(owner, autoPlay: true, preferredPath: file);
        }
        else
        {
            SetCurrentItem(null);
            OpenFile(file);
        }
    }

    private static bool IsInside(string folder, string file) =>
        Path.GetFullPath(file).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private async void Forget(Task task)
    {
        try { await task; }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ShowOsd("Error: " + ex.Message, 5);
        }
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    private async Task ActivateTabAsync(TabViewModel tab, bool autoPlay, string? preferredPath = null)
    {
        SaveCurrentProgress();

        _activeTab = tab;
        _library.SetSetting("activeTab", tab.Id.ToString());
        SetSelectedTab(tab);
        PlaylistList.ItemsSource = tab.Items;
        UpdatePlaylistHeader(tab, scanning: tab.Items.Count == 0);
        UpdateWindowTitle();

        await RefreshPlaylistAsync(tab);
        if (_activeTab != tab) return; // the user moved on to another tab while the folder was being scanned

        var item = FindItem(tab, preferredPath) ?? FindItem(tab, tab.LastVideo)
                   ?? tab.Items.FirstOrDefault(i => !i.IsWatched) ?? tab.Items.FirstOrDefault();
        if (item is null)
        {
            if (autoPlay) StopPlayback(); // don't leave another subject's lecture playing under an empty tab
            return;
        }
        PlayItem(item, paused: !autoPlay);
    }

    private async Task RefreshPlaylistAsync(TabViewModel tab)
    {
        var folder = tab.Folder;
        var (entries, exists) = await Task.Run(() =>
        {
            try
            {
                return Directory.Exists(folder) ? (PlaylistScanner.Scan(folder), true) : (NoEntries, false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (NoEntries, false);
            }
        });

        tab.FolderMissing = !exists;
        var progress = _library.GetProgressUnder(folder);
        tab.Items.Clear();
        foreach (var entry in entries)
        {
            var item = new PlaylistItem(entry);
            item.ApplyProgress(progress.GetValueOrDefault(item.Path));
            tab.Items.Add(item);
        }

        if (tab != _activeTab) return;
        SetCurrentItem(FindItem(tab, _currentPath));
        UpdatePlaylistHeader(tab);
        Forget(MeasureTabAsync(tab)); // the lengths for the header fill in as they become known
    }

    private void SetSelectedTab(TabViewModel? tab)
    {
        _suppressTabSelection = true;
        try
        {
            TabList.SelectedItem = tab;
            if (tab is not null) TabList.ScrollIntoView(tab);
        }
        finally { _suppressTabSelection = false; }
    }

    private void UpdateTabBarHint()
    {
        TabHint.Visibility = _tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateWelcomeSubjects();
    }

    private void StepTab(int delta)
    {
        if (_tabs.Count < 2) return;
        var index = _activeTab is null ? 0 : _tabs.IndexOf(_activeTab);
        var next = _tabs[(index + delta + _tabs.Count) % _tabs.Count];
        Forget(ActivateTabAsync(next, autoPlay: true));
    }

    private void GoToTabNumber(int index)
    {
        if (index < 0 || index >= _tabs.Count || _tabs[index] == _activeTab) return;
        Forget(ActivateTabAsync(_tabs[index], autoPlay: true));
    }

    private void PromptAddTabs()
    {
        var dialog = new OpenFolderDialog { Title = "Add folders as tabs (one tab per subject)", Multiselect = true };
        if (dialog.ShowDialog(this) == true) Forget(AddTabsAsync(dialog.FolderNames));
    }

    private async Task AddTabsAsync(IEnumerable<string> folders)
    {
        TabViewModel? last = null;
        foreach (var folder in folders)
        {
            var record = _library.AddTab(folder);
            var tab = _tabs.FirstOrDefault(t => t.Id == record.Id);
            if (tab is null)
            {
                tab = new TabViewModel(record);
                _tabs.Add(tab);
            }
            last = tab;
        }

        UpdateTabBarHint();
        if (last is not null) await ActivateTabAsync(last, autoPlay: true);
    }

    private async Task CloseTabAsync(TabViewModel tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        var wasActive = tab == _activeTab;
        if (wasActive) SaveCurrentProgress();

        _library.RemoveTab(tab.Id);
        _suppressTabSelection = true;
        _tabs.Remove(tab);
        _suppressTabSelection = false;
        UpdateTabBarHint();

        if (!wasActive)
        {
            SetSelectedTab(_activeTab);
            return;
        }

        _activeTab = null;
        if (_tabs.Count == 0)
        {
            StopPlayback();
            PlaylistList.ItemsSource = null;
            UpdatePlaylistHeader(null);
            UpdateWindowTitle();
            return;
        }
        await ActivateTabAsync(_tabs[Math.Min(index, _tabs.Count - 1)], autoPlay: true);
    }

    private void MoveTab(TabViewModel tab, int delta)
    {
        var from = _tabs.IndexOf(tab);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= _tabs.Count) return;

        _suppressTabSelection = true;
        _tabs.Move(from, to);
        _suppressTabSelection = false;
        SetSelectedTab(_activeTab);
        _library.ReorderTabs(_tabs.Select(t => t.Id));
    }

    // ---- playlist --------------------------------------------------------------------------------------------

    private static PlaylistItem? FindItem(TabViewModel? tab, string? path) =>
        path is null ? null : tab?.Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

    private void SetCurrentItem(PlaylistItem? item)
    {
        _currentItem?.SetCurrent(false);
        _currentItem = item;
        item?.SetCurrent(true);
        if (item is not null)
        {
            PlaylistList.SelectedItem = item;
            PlaylistList.ScrollIntoView(item);
        }
        UpdateWindowTitle();
    }

    private void PlayItem(PlaylistItem item, bool paused = false)
    {
        if (_activeTab is null) return;

        var start = ResumePoint(item.Path, rewind: !paused);
        if (!OpenFile(item.Path, start, paused)) return; // engine unavailable

        SetCurrentItem(item);
        _activeTab.LastVideo = item.Path;
        _library.SetLastVideo(_activeTab.Id, item.Path);
    }

    /// <summary>
    /// Where to resume a video, or null to start from the beginning (finished or barely begun).
    /// When playback starts straight away it backs up 2 s for context; a paused restore stays exact, otherwise every
    /// open-and-close without playing would walk the saved position backwards.
    /// </summary>
    private TimeSpan? ResumePoint(string path, bool rewind)
    {
        var record = _library.GetProgress(path);
        if (record is null || record.Fraction >= 0.95 || record.Position < TimeSpan.FromSeconds(5)) return null;
        return rewind ? record.Position - TimeSpan.FromSeconds(2) : record.Position;
    }

    private void PlayNext(bool auto)
    {
        if (_activeTab is null || _currentItem is null)
        {
            if (!auto) ShowOsd("No playlist");
            return;
        }

        var index = _activeTab.Items.IndexOf(_currentItem);
        if (index >= 0 && index + 1 < _activeTab.Items.Count) PlayItem(_activeTab.Items[index + 1]);
        else ShowOsd(auto ? "End of playlist" : "Last video");
    }

    private void PlayPrevious()
    {
        if (_activeTab is null || _currentItem is null)
        {
            ShowOsd("No playlist");
            return;
        }

        // Like most players: a few seconds in, "previous" restarts the current video first.
        if (_engine.HasMedia && _engine.Position > TimeSpan.FromSeconds(3))
        {
            _engine.Seek(TimeSpan.Zero);
            return;
        }

        var index = _activeTab.Items.IndexOf(_currentItem);
        if (index > 0) PlayItem(_activeTab.Items[index - 1]);
        else ShowOsd("First video");
    }

    private void OnEndReached()
    {
        if (_currentPath is not { } path) return;

        var duration = _engine.Duration;
        if (duration > TimeSpan.Zero)
        {
            try { _library.SaveProgress(path, duration, duration); } catch (SqliteException) { }
            if (FindItem(_activeTab, path) is { } item) item.ApplyProgress(new ProgressRecord(path, duration, duration));
            if (_activeTab is not null) UpdatePlaylistHeader(_activeTab);
        }

        PlayNext(auto: true); // lecture finished: go on to the next one in the tab
    }

    private void SaveCurrentProgress()
    {
        if (_currentPath is not { } path || !_engine.HasMedia || Environment.TickCount64 < _saveAllowedAfter) return;

        var duration = _engine.Duration;
        if (duration <= TimeSpan.Zero) return;

        var record = new ProgressRecord(path, _engine.Position, duration);
        try { _library.SaveProgress(path, record.Position, record.Duration); }
        catch (SqliteException) { return; }

        if (FindItem(_activeTab, path) is not { } item) return;
        LearnDuration(item, duration);
        var wasWatched = item.IsWatched;
        item.ApplyProgress(record);
        if (wasWatched != item.IsWatched && _activeTab is not null) UpdatePlaylistHeader(_activeTab);
    }

    private void UpdatePlaylistHeader(TabViewModel? tab, bool scanning = false)
    {
        if (tab is null)
        {
            PlaylistTitle.Text = string.Empty;
            PlaylistSummary.Text = string.Empty;
            PlaylistTotalBox.Visibility = Visibility.Collapsed;
            PlaylistEmpty.Text = NoTabsText();
            PlaylistEmpty.Visibility = Visibility.Visible;
            return;
        }

        var count = tab.Items.Count;
        PlaylistTitle.Text = tab.Title;
        ShowPlaylistSummary(tab);
        PlaylistEmpty.Text = scanning ? "Scanning folder…"
            : tab.FolderMissing ? $"This folder can't be found:\n{tab.Folder}"
            : "No video files in this folder.";
        PlaylistEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWindowTitle()
    {
        var parts = new List<string>();
        if (_currentPath is not null) parts.Add(Path.GetFileName(_currentPath));
        if (_currentItem is not null && _activeTab is not null) parts.Add(_activeTab.Title);
        parts.Add("Spectraplayer");
        Title = string.Join(" — ", parts);

        // The title bar has no room for the file name any more, so it is shown in the control bar.
        NowPlaying.Text = _currentPath is null ? string.Empty
            : _currentItem is not null && _activeTab is not null ? $"{_currentItem.Name}   ·   {_activeTab.Title}"
            : Path.GetFileNameWithoutExtension(_currentPath);
    }

    // ---- events: panel switcher, tabs ------------------------------------------------------------------------

    private void OnPanelPlaylistClick(object sender, RoutedEventArgs e) => SwitchPanelView(PanelView.Playlist);
    private void OnPanelBookmarksClick(object sender, RoutedEventArgs e) => SwitchPanelView(PanelView.Bookmarks);
    private void OnNewTabClick(object sender, RoutedEventArgs e) => PromptAddTabs();
    private void OnPreviousClick(object sender, RoutedEventArgs e) => PlayPrevious();
    private void OnNextClick(object sender, RoutedEventArgs e) => PlayNext(auto: false);

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection || TabList.SelectedItem is not TabViewModel tab || tab == _activeTab) return;
        Forget(ActivateTabAsync(tab, autoPlay: true));
    }

    private void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button && sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            Forget(CloseTabAsync(tab));
            e.Handled = true;
        }
    }

    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Middle-click closes a tab, as in a browser.
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            Forget(CloseTabAsync(tab));
            e.Handled = true;
        }
    }

    private void OnTabWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(TabList) is not { } viewer) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>The tab a tab-strip context menu was opened on (right-click doesn't switch tabs).</summary>
    private static TabViewModel? TabFromMenu(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: FrameworkElement { DataContext: TabViewModel tab } } ? tab : null;

    private void OnTabRename(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is not { } tab) return;
        var name = InputDialog.Ask(this, "Rename tab", "Tab name", tab.Title);
        if (string.IsNullOrWhiteSpace(name)) return;

        tab.Title = name;
        _library.RenameTab(tab.Id, name);
        if (tab == _activeTab) { UpdatePlaylistHeader(tab); UpdateWindowTitle(); }
    }

    private void OnTabRescan(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is { } tab) Forget(RefreshPlaylistAsync(tab));
    }

    private void OnTabShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is { } tab) OpenInExplorer($"\"{tab.Folder}\"");
    }

    private void OnTabMoveLeft(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is { } tab) MoveTab(tab, -1);
    }

    private void OnTabMoveRight(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is { } tab) MoveTab(tab, +1);
    }

    private void OnTabClose(object sender, RoutedEventArgs e)
    {
        if (TabFromMenu(sender) is { } tab) Forget(CloseTabAsync(tab));
    }

    // ---- events: playlist ------------------------------------------------------------------------------------

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var onItem = e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(PlaylistList, d) is ListBoxItem;
        if (onItem && PlaylistList.SelectedItem is PlaylistItem item) PlayItem(item);
    }

    private void OnPlaylistRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem container) container.IsSelected = true;
    }

    private void OnPlaylistPlay(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistItem item) PlayItem(item);
    }

    private void OnPlaylistMarkWatched(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is not PlaylistItem item) return;
        _library.MarkWatched(item.Path);
        item.ApplyProgress(_library.GetProgress(item.Path));
        if (_activeTab is not null) UpdatePlaylistHeader(_activeTab);
    }

    private void OnPlaylistMarkUnwatched(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is not PlaylistItem item) return;
        _library.ClearProgress(item.Path);
        item.ApplyProgress(null);
        if (_activeTab is not null) UpdatePlaylistHeader(_activeTab);
    }

    private void OnPlaylistShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistItem item) OpenInExplorer($"/select,\"{item.Path}\"");
    }

    private static void OpenInExplorer(string arguments)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
