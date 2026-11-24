using System.Windows.Input;
using SpectraPlayer.Core.Shortcuts;

namespace SpectraPlayer.App.Input;

public enum PlayerAction
{
    TogglePause, SeekBack5, SeekForward5, SeekBack30, SeekForward30,
    VolumeUp, VolumeDown, ToggleMute,
    ToggleFullscreen, ExitFullscreen,
    AddBookmark, ToggleBookmarks, PreviousBookmark, NextBookmark,
    TogglePlaylist, PreviousVideo, NextVideo, NewNote, MarkSectionStart, MarkSectionEnd,
    NewTab, CloseTab, NextTab, PreviousTab,
    OpenFile, SpeedUp, SpeedDown, SpeedReset, FrameStep, FrameBackStep, ShowShortcuts, OpenSettings, ShowFocus, ToggleFocusTimer,

    // Keep contiguous: GoToTab(n) is computed as GoToTab1 + (n - 1).
    GoToTab1, GoToTab2, GoToTab3, GoToTab4, GoToTab5, GoToTab6, GoToTab7, GoToTab8, GoToTab9,
}

/// <summary>
/// Which key does what. One table drives both input paths (the WPF window, and mpv for when the video surface has the
/// keyboard), so shortcuts behave identically wherever the focus is, and the user can change any of them in Settings.
/// The defaults follow PotPlayer where it has a convention: C/X/Z speed, F/D frame step, Enter fullscreen, Ctrl+B
/// bookmark (B), P playlist, PgUp/PgDn previous/next video. Tabs follow browsers: Ctrl+T/W/Tab/1–9.
/// </summary>
public static class KeyMap
{
    private static readonly ShortcutTable Table = new(BuildDefaults());

    /// <summary>Raised whenever a shortcut changes (the app saves the table, re-registers keys with mpv and refreshes hints).</summary>
    public static event EventHandler? Changed
    {
        add => Table.Changed += value;
        remove => Table.Changed -= value;
    }

    private static IEnumerable<(string, Gesture)> BuildDefaults()
    {
        (string, Gesture) D(PlayerAction action, string gesture)
        {
            if (!Gesture.TryParse(gesture, out var g)) throw new InvalidOperationException($"Bad default shortcut {gesture}");
            return (action.ToString(), g);
        }

        var list = new List<(string, Gesture)>
        {
            D(PlayerAction.TogglePause, "Space"),
            D(PlayerAction.SeekBack5, "Left"),
            D(PlayerAction.SeekForward5, "Right"),
            D(PlayerAction.SeekBack30, "Ctrl+Left"),
            D(PlayerAction.SeekForward30, "Ctrl+Right"),
            D(PlayerAction.VolumeUp, "Up"),
            D(PlayerAction.VolumeDown, "Down"),
            D(PlayerAction.ToggleMute, "M"),
            D(PlayerAction.ToggleFullscreen, "Enter"),
            D(PlayerAction.ToggleFullscreen, "F11"),
            D(PlayerAction.ExitFullscreen, "Escape"),

            D(PlayerAction.AddBookmark, "B"),
            D(PlayerAction.ToggleBookmarks, "H"),
            D(PlayerAction.PreviousBookmark, "OemOpenBrackets"),
            D(PlayerAction.NextBookmark, "OemCloseBrackets"),

            D(PlayerAction.NewNote, "N"),
            D(PlayerAction.MarkSectionStart, "I"),
            D(PlayerAction.MarkSectionEnd, "O"),
            D(PlayerAction.TogglePlaylist, "P"),
            D(PlayerAction.PreviousVideo, "PageUp"),
            D(PlayerAction.NextVideo, "PageDown"),

            D(PlayerAction.NewTab, "Ctrl+T"),
            D(PlayerAction.CloseTab, "Ctrl+W"),
            D(PlayerAction.NextTab, "Ctrl+Tab"),
            D(PlayerAction.PreviousTab, "Ctrl+Shift+Tab"),

            D(PlayerAction.OpenFile, "Ctrl+O"),
            D(PlayerAction.SpeedUp, "C"),
            D(PlayerAction.SpeedDown, "X"),
            D(PlayerAction.SpeedReset, "Z"),
            D(PlayerAction.SpeedUp, "Shift+OemPeriod"),   // the "faster / slower" keys of most players
            D(PlayerAction.SpeedDown, "Shift+OemComma"),
            D(PlayerAction.ShowShortcuts, "F1"),
            D(PlayerAction.OpenSettings, "Ctrl+OemComma"),
            D(PlayerAction.ShowFocus, "T"),
            D(PlayerAction.ToggleFocusTimer, "Shift+T"),
            D(PlayerAction.FrameStep, "F"),
            D(PlayerAction.FrameBackStep, "D"),
        };

        for (var n = 1; n <= 9; n++) list.Add(D(PlayerAction.GoToTab1 + (n - 1), $"Ctrl+D{n}"));
        return list;
    }

    // ---- looking keys up -------------------------------------------------------------------------------------

    public static bool TryGetAction(Key key, ModifierKeys modifiers, out PlayerAction action)
    {
        action = default;
        if (!TryGetGesture(key, modifiers, out var gesture)) return false;
        return Table.ActionFor(gesture) is { } id && Enum.TryParse(id, out action);
    }

    /// <summary>The keys currently assigned to an action (first one is the one shown in hints).</summary>
    public static IReadOnlyList<Gesture> BindingsOf(PlayerAction action) => Table.BindingsOf(action.ToString());

    /// <summary>"Ctrl+B", or null if the action has no key.</summary>
    public static string? HintFor(PlayerAction action) => BindingsOf(action) is [var first, ..] ? first.Display : null;

    /// <summary>What to register with mpv: its name for each key, and the action. Keys mpv can't express are left out.</summary>
    public static IEnumerable<(string MpvKey, PlayerAction Action)> MpvBindings()
    {
        foreach (var (id, gesture) in Table.All)
            if (gesture.TryGetMpvKey(out var mpv) && Enum.TryParse<PlayerAction>(id, out var action))
                yield return (mpv, action);
    }

    // ---- changing them ---------------------------------------------------------------------------------------

    public static ShortcutTable Current => Table;

    public static void Load(string? savedJson) => Table.LoadJson(savedJson);

    public static string Save() => Table.ToJson();

    // ---- WPF key <-> gesture ---------------------------------------------------------------------------------

    /// <summary>
    /// A WPF key press as a gesture. False for keys shortcuts can't use (a modifier on its own, media keys…), and for
    /// combinations that are reserved (Alt+F4).
    /// </summary>
    public static bool TryGetGesture(Key key, ModifierKeys modifiers, out Gesture gesture)
    {
        // Several WPF keys share a number; say which spelling we mean.
        var name = key switch
        {
            Key.Return => "Enter",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            _ => key.ToString(),
        };

        gesture = default;
        if (Gesture.CanonicalKey(name) is not { } canonical) return false;

        gesture = new Gesture(canonical,
            Ctrl: modifiers.HasFlag(ModifierKeys.Control),
            Shift: modifiers.HasFlag(ModifierKeys.Shift),
            Alt: modifiers.HasFlag(ModifierKeys.Alt));
        return gesture.IsAllowed;
    }
}
