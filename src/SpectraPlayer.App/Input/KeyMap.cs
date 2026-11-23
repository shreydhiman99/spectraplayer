using System.Windows.Input;

namespace SpectraPlayer.App.Input;

public enum PlayerAction
{
    TogglePause, SeekBack5, SeekForward5, SeekBack30, SeekForward30,
    VolumeUp, VolumeDown, ToggleMute,
    ToggleFullscreen, ExitFullscreen,
    AddBookmark, ToggleBookmarks, PreviousBookmark, NextBookmark,
    TogglePlaylist, PreviousVideo, NextVideo, NewNote, MarkSectionStart, MarkSectionEnd,
    NewTab, CloseTab, NextTab, PreviousTab,
    OpenFile, SpeedUp, SpeedDown, SpeedReset, FrameStep, FrameBackStep, ShowShortcuts,

    // Keep contiguous: GoToTab(n) is computed as GoToTab1 + (n - 1).
    GoToTab1, GoToTab2, GoToTab3, GoToTab4, GoToTab5, GoToTab6, GoToTab7, GoToTab8, GoToTab9,
}

/// <param name="Key">WPF key, used when the app window has keyboard focus.</param>
/// <param name="MpvKey">The same key in mpv's naming, used when the video surface has focus (it eats key presses).</param>
public sealed record KeyBinding(Key Key, ModifierKeys Modifiers, string MpvKey, PlayerAction Action);

/// <summary>
/// One table drives both input paths, so shortcuts behave identically whether the focus is on the video or on a control.
/// PotPlayer conventions where it has them: C/X/Z speed, F/D frame step, Enter fullscreen, Ctrl+B bookmark,
/// F6 playlist, PgUp/PgDn previous/next video. Tabs follow browsers: Ctrl+T/W/Tab/1–9.
/// </summary>
public static class KeyMap
{
    public static readonly IReadOnlyList<KeyBinding> All = Build();

    private static List<KeyBinding> Build()
    {
        var list = new List<KeyBinding>
        {
            new(Key.Space, ModifierKeys.None, "SPACE", PlayerAction.TogglePause),
            new(Key.Left, ModifierKeys.None, "LEFT", PlayerAction.SeekBack5),
            new(Key.Right, ModifierKeys.None, "RIGHT", PlayerAction.SeekForward5),
            new(Key.Left, ModifierKeys.Control, "ctrl+LEFT", PlayerAction.SeekBack30),
            new(Key.Right, ModifierKeys.Control, "ctrl+RIGHT", PlayerAction.SeekForward30),
            new(Key.Up, ModifierKeys.None, "UP", PlayerAction.VolumeUp),
            new(Key.Down, ModifierKeys.None, "DOWN", PlayerAction.VolumeDown),
            new(Key.M, ModifierKeys.None, "m", PlayerAction.ToggleMute),
            new(Key.Enter, ModifierKeys.None, "ENTER", PlayerAction.ToggleFullscreen),
            new(Key.F11, ModifierKeys.None, "F11", PlayerAction.ToggleFullscreen),
            new(Key.Escape, ModifierKeys.None, "ESC", PlayerAction.ExitFullscreen),

            new(Key.B, ModifierKeys.Control, "ctrl+b", PlayerAction.AddBookmark),
            new(Key.H, ModifierKeys.None, "h", PlayerAction.ToggleBookmarks),
            new(Key.OemOpenBrackets, ModifierKeys.None, "[", PlayerAction.PreviousBookmark),
            new(Key.OemCloseBrackets, ModifierKeys.None, "]", PlayerAction.NextBookmark),

            new(Key.N, ModifierKeys.None, "n", PlayerAction.NewNote),
            new(Key.I, ModifierKeys.None, "i", PlayerAction.MarkSectionStart),
            new(Key.O, ModifierKeys.None, "o", PlayerAction.MarkSectionEnd),
            new(Key.F6, ModifierKeys.None, "F6", PlayerAction.TogglePlaylist),
            new(Key.PageUp, ModifierKeys.None, "PGUP", PlayerAction.PreviousVideo),
            new(Key.PageDown, ModifierKeys.None, "PGDWN", PlayerAction.NextVideo),

            new(Key.T, ModifierKeys.Control, "ctrl+t", PlayerAction.NewTab),
            new(Key.W, ModifierKeys.Control, "ctrl+w", PlayerAction.CloseTab),
            new(Key.Tab, ModifierKeys.Control, "ctrl+TAB", PlayerAction.NextTab),
            new(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, "ctrl+shift+TAB", PlayerAction.PreviousTab),

            new(Key.O, ModifierKeys.Control, "ctrl+o", PlayerAction.OpenFile),
            new(Key.C, ModifierKeys.None, "c", PlayerAction.SpeedUp),
            new(Key.X, ModifierKeys.None, "x", PlayerAction.SpeedDown),
            new(Key.Z, ModifierKeys.None, "z", PlayerAction.SpeedReset),
            new(Key.OemPeriod, ModifierKeys.Shift, ">", PlayerAction.SpeedUp),   // the "faster / slower" keys of most players
            new(Key.OemComma, ModifierKeys.Shift, "<", PlayerAction.SpeedDown),
            new(Key.F1, ModifierKeys.None, "F1", PlayerAction.ShowShortcuts),
            new(Key.F, ModifierKeys.None, "f", PlayerAction.FrameStep),
            new(Key.D, ModifierKeys.None, "d", PlayerAction.FrameBackStep),
        };

        for (var n = 1; n <= 9; n++)
            list.Add(new(Key.D1 + (n - 1), ModifierKeys.Control, $"ctrl+{n}", PlayerAction.GoToTab1 + (n - 1)));

        return list;
    }

    public static bool TryGetAction(Key key, ModifierKeys modifiers, out PlayerAction action)
    {
        foreach (var b in All)
            if (b.Key == key && b.Modifiers == modifiers) { action = b.Action; return true; }
        action = default;
        return false;
    }
}
