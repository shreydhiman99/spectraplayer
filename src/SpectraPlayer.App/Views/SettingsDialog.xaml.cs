using System.Windows;
using System.Windows.Input;
using SpectraPlayer.App.Input;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core.Shortcuts;

namespace SpectraPlayer.App.Views;

/// <summary>
/// Settings: for now the keyboard shortcuts. Click a key, press the new one. Every change goes straight into
/// <see cref="KeyMap"/>, which saves it and re-registers the keys, so there is nothing to confirm.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly List<ShortcutActionRow> _rows = [];
    private ShortcutActionRow? _recordingRow;
    private Gesture? _replacing; // the key being changed; null when adding another key

    public SettingsDialog()
    {
        InitializeComponent();
        DialogChrome.Attach(this);

        // Never taller than the usable screen (small laptop screens).
        Height = Math.Min(Height, SystemParameters.WorkArea.Height * 0.92);

        var groups = ActionCatalog.Groups
            .Select(g => new ShortcutActionGroup(g.Title, g.Actions.Select(a => new ShortcutActionRow(a.Action, a.Title)).ToList()))
            .ToList();
        _rows.AddRange(groups.SelectMany(g => g.Rows));
        Groups.ItemsSource = groups;

        RefreshAll();
        KeyMap.Changed += OnKeysChanged;
    }

    private void OnClosed(object? sender, EventArgs e) => KeyMap.Changed -= OnKeysChanged;

    private void OnKeysChanged(object? sender, EventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        foreach (var row in _rows) row.Refresh();
        ResetAllButton.IsEnabled = KeyMap.Current.AnyCustomised;
    }

    // ---- the buttons in each row -----------------------------------------------------------------------------

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutChip chip }) BeginRecording(chip.Row, chip.Gesture);
    }

    private void OnAddKey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutActionRow row }) BeginRecording(row, null);
    }

    private void OnChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutChip chip }) return;
        StopRecording();
        KeyMap.Current.Remove(chip.Row.Action.ToString(), chip.Gesture);
        ShowNotice(KeyMap.BindingsOf(chip.Row.Action).Count == 0
            ? $"“{chip.Row.Title}” now has no key. It is still in the right-click menu."
            : null);
    }

    private void OnResetRow(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutActionRow row }) return;
        StopRecording();
        var displaced = KeyMap.Current.Reset(row.Action.ToString());
        ShowNotice(displaced.Count == 0 ? null : TookKeysMessage(displaced));
    }

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        StopRecording();
        if (!ConfirmDialog.Ask(this, "Reset shortcuts", "Put every shortcut back to the key it had when Spectraplayer was installed?", "Reset all"))
            return;
        KeyMap.Current.ResetAll();
        ShowNotice(null);
    }

    // ---- recording a key -------------------------------------------------------------------------------------

    private void BeginRecording(ShortcutActionRow row, Gesture? replacing)
    {
        StopRecording();
        _recordingRow = row;
        _replacing = replacing;
        row.IsRecording = true;
        ShowNotice(null);
        RecordText.Text = replacing is null
            ? $"Press the extra key for “{row.Title}”."
            : $"Press the new key for “{row.Title}”.";
        RecordText.Text += "  Esc cancels.";
        RecordBanner.Visibility = Visibility.Visible;
    }

    private void StopRecording()
    {
        if (_recordingRow is not null) _recordingRow.IsRecording = false;
        _recordingRow = null;
        _replacing = null;
        RecordBanner.Visibility = Visibility.Collapsed;
    }

    private void OnCancelRecording(object sender, RoutedEventArgs e) => StopRecording();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingRow is null) return; // not recording: Esc closes the dialog as usual
        e.Handled = true;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        // Ctrl, Shift or Alt on their own are the start of a combination: keep waiting for the real key.
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
            return;

        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return;
        }

        if (!KeyMap.TryGetGesture(key, modifiers, out var gesture))
        {
            ShowNotice("That key can't be used as a shortcut. Try a letter, number, function or arrow key, with Ctrl, Shift or Alt if you like.");
            return;
        }
        if (!gesture.TryGetMpvKey(out _))
        {
            // It would work with the window focused but not after a click on the video, which would be confusing.
            ShowNotice($"{gesture.Display} can't be used: it depends on the keyboard layout. Try a different combination.");
            return;
        }

        var row = _recordingRow;
        var replacing = _replacing;
        StopRecording();

        var displaced = KeyMap.Current.Assign(row.Action.ToString(), gesture, replacing);
        ShowNotice(displaced.Count == 0 ? null
            : $"{gesture.Display} was used for {NamesOf(displaced)}, which no longer has it.");
    }

    // ---- messages --------------------------------------------------------------------------------------------

    private static string NamesOf(IEnumerable<string> actionIds) =>
        string.Join(" and ", actionIds.Select(id => $"“{ActionCatalog.TitleOf(id)}”"));

    private static string TookKeysMessage(IEnumerable<string> displaced) =>
        $"Its default key was in use by {NamesOf(displaced)}, which no longer has it.";

    private void ShowNotice(string? text)
    {
        Notice.Text = text ?? string.Empty;
        Notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
