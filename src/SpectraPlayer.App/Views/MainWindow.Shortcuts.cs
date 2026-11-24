using System.Windows;
using System.Windows.Controls;
using SpectraPlayer.App.Input;

namespace SpectraPlayer.App.Views;

// Keyboard shortcuts that the user can change (see SettingsDialog): saving them, giving the new keys to mpv, and keeping
// every hint that mentions a key (tooltips, menus, empty-list messages, the welcome screen) in step.
public partial class MainWindow
{
    private const string ShortcutsSetting = "shortcuts";

    private readonly HashSet<string> _mpvKeys = [];
    private bool _mpvKeysReady;

    /// <summary>Loads the user's shortcuts. Called once, before anything shows a key.</summary>
    private void InitializeShortcuts()
    {
        KeyMap.Load(_library.GetSetting(ShortcutsSetting));
        KeyMap.Changed += OnShortcutsChanged;
        RefreshShortcutHints();
    }

    private void OnShortcutsChanged(object? sender, EventArgs e)
    {
        try { _library.SetSetting(ShortcutsSetting, KeyMap.Save()); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException)
        {
            ShowOsd("Could not save the shortcuts: " + ex.Message, 4);
        }

        if (_mpvKeysReady) SyncMpvKeys();
        RefreshShortcutHints();
    }

    // ---- mpv gets keys that reach the video surface ----------------------------------------------------------

    /// <summary>Makes mpv's key bindings match the table: new keys bound, keys that are no longer used released.</summary>
    private void SyncMpvKeys()
    {
        var wanted = new Dictionary<string, PlayerAction>();
        foreach (var (mpvKey, action) in KeyMap.MpvBindings()) wanted[mpvKey] = action;

        foreach (var key in _mpvKeys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            _engine.UnbindKey(key);
            _mpvKeys.Remove(key);
        }
        foreach (var (key, action) in wanted)
        {
            _engine.BindKey(key, $"spectra action {action}");
            _mpvKeys.Add(key);
        }
        _mpvKeysReady = true;
    }

    // ---- hints -----------------------------------------------------------------------------------------------

    /// <summary>" (Ctrl+B)" for an action's first key, or nothing if it has none.</summary>
    private static string Paren(PlayerAction action) => KeyMap.HintFor(action) is { } key ? $" ({key})" : string.Empty;

    private static string NoTabsText() =>
        $"No tabs yet. Add a folder with the + button{Paren(PlayerAction.NewTab)}, for example one folder per subject.";

    private void RefreshShortcutHints()
    {
        NewTabButton.ToolTip = "Add folder as tab" + Paren(PlayerAction.NewTab);
        OpenButton.ToolTip = "Open file" + Paren(PlayerAction.OpenFile);
        PrevButton.ToolTip = "Previous video" + Paren(PlayerAction.PreviousVideo);
        PlayButton.ToolTip = "Play / Pause" + Paren(PlayerAction.TogglePause);
        NextButton.ToolTip = "Next video" + Paren(PlayerAction.NextVideo);
        BookmarkButton.ToolTip = "Add bookmark" + Paren(PlayerAction.AddBookmark);
        PlaylistToggleButton.ToolTip = $"Playlist{Paren(PlayerAction.TogglePlaylist)} / Bookmarks{Paren(PlayerAction.ToggleBookmarks)}";
        SpeedButton.ToolTip = "Playback speed: click for presets" +
            string.Concat(new[] { (PlayerAction.SpeedUp, "faster"), (PlayerAction.SpeedDown, "slower"), (PlayerAction.SpeedReset, "normal") }
                .Where(x => KeyMap.HintFor(x.Item1) is not null)
                .Select((x, i) => $"{(i == 0 ? ". " : ", ")}{KeyMap.HintFor(x.Item1)} {x.Item2}"));
        MuteButton.ToolTip = "Mute" + Paren(PlayerAction.ToggleMute);
        FullscreenButton.ToolTip = "Fullscreen" + Paren(PlayerAction.ToggleFullscreen);
        SettingsButton.ToolTip = "Settings" + Paren(PlayerAction.OpenSettings);
        PanelFocusButton.ToolTip = "Focus timer" + Paren(PlayerAction.ShowFocus);
        FocusPill.ToolTip = "Focus timer: click to open" + Paren(PlayerAction.ShowFocus);
        PanelHideButton.ToolTip = $"Hide the panel{Paren(PlayerAction.TogglePlaylist)}. Move the mouse to the right edge to bring it back";

        SetStartButton.Content = "Set start" + (KeyMap.HintFor(PlayerAction.MarkSectionStart) is { } s ? $"  ({s})" : string.Empty);
        SetEndButton.Content = "Set end" + (KeyMap.HintFor(PlayerAction.MarkSectionEnd) is { } e ? $"  ({e})" : string.Empty);
        SelectionText.ToolTip = "Mark the section with the start/end keys, or hold Shift and drag on the seek bar";

        WelcomeOpenHint.Text = KeyMap.HintFor(PlayerAction.OpenFile) ?? " ";
        WelcomeTabHint.Text = "One tab per subject" + (KeyMap.HintFor(PlayerAction.NewTab) is { } t ? $"  ·  {t}" : string.Empty);
        WelcomeShortcuts.ItemsSource = ShortcutGuide.Welcome();

        NoteEmpty.Text = KeyMap.HintFor(PlayerAction.NewNote) is { } note
            ? $"No notes on this video yet. Press {note} while watching to write one at the current time."
            : "No notes on this video yet. Choose “Take note” from the right-click menu while watching to write one.";
        BookmarkEmpty.Text = KeyMap.HintFor(PlayerAction.AddBookmark) is { } bookmark
            ? $"No bookmarks yet. Press {bookmark} while playing to add one."
            : "No bookmarks yet. Use the flag button below while playing to add one.";
        if (PlaylistEmpty.Text.StartsWith("No tabs yet", StringComparison.Ordinal)) PlaylistEmpty.Text = NoTabsText();

        // the key shown on the right of each menu entry
        foreach (var menu in new[] { "PlayerMenu", "TabMenu" })
            foreach (var item in ((ContextMenu)FindResource(menu)).Items.OfType<MenuItem>())
                if (item.Tag is string id && Enum.TryParse<PlayerAction>(id, out var action))
                    item.InputGestureText = KeyMap.HintFor(action) ?? string.Empty;
    }

    // ---- opening Settings ------------------------------------------------------------------------------------

    private void ShowSettings() => new SettingsDialog { Owner = this }.ShowDialog();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();
}
