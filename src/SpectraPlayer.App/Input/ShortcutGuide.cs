namespace SpectraPlayer.App.Input;

/// <param name="Keys">One entry per key cap, e.g. ["Ctrl", "B"] is drawn as two caps.</param>
public sealed record ShortcutRow(IReadOnlyList<string> Keys, string Description);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);

/// <summary>
/// What the welcome screen and the shortcuts dialog (F1) show. Keep it in step with <see cref="KeyMap"/>, which is what
/// actually reacts to the keys.
/// </summary>
public static class ShortcutGuide
{
    private static ShortcutRow Row(string description, params string[] keys) => new(keys, description);

    /// <summary>A short list for the welcome screen.</summary>
    public static readonly IReadOnlyList<ShortcutGroup> Welcome =
    [
        new("Playback",
        [
            Row("Play / pause", "Space"),
            Row("Skip 5 seconds", "←", "→"),
            Row("Volume", "↑", "↓"),
            Row("Faster / slower", "C", "X"),
            Row("Normal speed", "Z"),
            Row("Fullscreen", "Enter"),
        ]),
        new("Study",
        [
            Row("Add a bookmark", "Ctrl", "B"),
            Row("Take a note", "N"),
            Row("Mark a section", "I", "O"),
            Row("Previous / next video", "PgUp", "PgDn"),
            Row("New tab (a folder)", "Ctrl", "T"),
            Row("All shortcuts", "F1"),
        ]),
    ];

    /// <summary>Every shortcut, for the F1 dialog.</summary>
    public static readonly IReadOnlyList<ShortcutGroup> All =
    [
        new("Playback",
        [
            Row("Play / pause", "Space"),
            Row("Skip 5 seconds back / forward", "←", "→"),
            Row("Skip 30 seconds back / forward", "Ctrl", "←", "→"),
            Row("Volume up / down (mouse wheel works too)", "↑", "↓"),
            Row("Mute", "M"),
            Row("Speed up", "C"),
            Row("Slow down", "X"),
            Row("Speed up / slow down (same keys as many players)", ">", "<"),
            Row("Normal speed", "Z"),
            Row("One frame forward / back", "F", "D"),
            Row("Fullscreen (Esc leaves it)", "Enter"),
            Row("Open a file", "Ctrl", "O"),
        ]),
        new("Bookmarks, notes and sections",
        [
            Row("Add a bookmark at this moment", "Ctrl", "B"),
            Row("Previous / next bookmark", "[", "]"),
            Row("Show bookmarks", "H"),
            Row("Take a note at this moment (Ctrl+Enter saves)", "N"),
            Row("Mark the start / end of a section", "I", "O"),
            Row("Mark a section with the mouse", "Shift", "drag"),
        ]),
        new("Playlist and tabs",
        [
            Row("Previous / next video in the tab", "PgUp", "PgDn"),
            Row("Show the playlist", "F6"),
            Row("New tab from a folder", "Ctrl", "T"),
            Row("Close the tab", "Ctrl", "W"),
            Row("Next / previous tab", "Ctrl", "Tab"),
            Row("Go to tab 1 to 9", "Ctrl", "1–9"),
        ]),
        new("Help",
        [
            Row("Show this list", "F1"),
        ]),
    ];
}
