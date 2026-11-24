using SpectraPlayer.Core.Shortcuts;

namespace SpectraPlayer.App.Input;

/// <param name="Caps">The keys to press together, each drawn as a key cap (e.g. ["Ctrl", "B"]).</param>
/// <param name="IsAlternative">True when this is another key for the same thing as the one before it: drawn after a "/".</param>
public sealed record ShortcutCombo(IReadOnlyList<string> Caps, bool IsAlternative = false);

public sealed record ShortcutRow(IReadOnlyList<ShortcutCombo> Combos, string Description);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);

public sealed record ActionInfo(PlayerAction Action, string Title);

public sealed record ActionGroup(string Title, IReadOnlyList<ActionInfo> Actions);

/// <summary>Every action a shortcut can trigger, in the order Settings and the F1 list show them.</summary>
public static class ActionCatalog
{
    private static ActionInfo A(PlayerAction action, string title) => new(action, title);

    public static readonly IReadOnlyList<ActionGroup> Groups =
    [
        new("Playback",
        [
            A(PlayerAction.TogglePause, "Play / pause"),
            A(PlayerAction.SeekBack5, "Skip 5 seconds back"),
            A(PlayerAction.SeekForward5, "Skip 5 seconds forward"),
            A(PlayerAction.SeekBack30, "Skip 30 seconds back"),
            A(PlayerAction.SeekForward30, "Skip 30 seconds forward"),
            A(PlayerAction.FrameStep, "One frame forward"),
            A(PlayerAction.FrameBackStep, "One frame back"),
            A(PlayerAction.SpeedUp, "Speed up"),
            A(PlayerAction.SpeedDown, "Slow down"),
            A(PlayerAction.SpeedReset, "Normal speed"),
            A(PlayerAction.VolumeUp, "Volume up (the mouse wheel works too)"),
            A(PlayerAction.VolumeDown, "Volume down"),
            A(PlayerAction.ToggleMute, "Mute"),
            A(PlayerAction.ToggleFullscreen, "Fullscreen"),
            A(PlayerAction.ExitFullscreen, "Leave fullscreen"),
            A(PlayerAction.OpenFile, "Open a file"),
        ]),
        new("Bookmarks, notes and sections",
        [
            A(PlayerAction.AddBookmark, "Add a bookmark at this moment"),
            A(PlayerAction.PreviousBookmark, "Previous bookmark"),
            A(PlayerAction.NextBookmark, "Next bookmark"),
            A(PlayerAction.ToggleBookmarks, "Show bookmarks"),
            A(PlayerAction.NewNote, "Take a note at this moment"),
            A(PlayerAction.MarkSectionStart, "Mark the start of a section"),
            A(PlayerAction.MarkSectionEnd, "Mark the end of a section"),
        ]),
        new("Playlist and tabs",
        [
            A(PlayerAction.PreviousVideo, "Previous video in the tab"),
            A(PlayerAction.NextVideo, "Next video in the tab"),
            A(PlayerAction.TogglePlaylist, "Show / hide the side panel"),
            A(PlayerAction.NewTab, "New tab from a folder"),
            A(PlayerAction.CloseTab, "Close the tab"),
            A(PlayerAction.NextTab, "Next tab"),
            A(PlayerAction.PreviousTab, "Previous tab"),
            .. Enumerable.Range(1, 9).Select(n => A(PlayerAction.GoToTab1 + (n - 1), $"Go to tab {n}")),
        ]),
        new("Focus timer",
        [
            A(PlayerAction.ShowFocus, "Show the focus timer"),
            A(PlayerAction.ToggleFocusTimer, "Start / pause the focus timer"),
        ]),
        new("Help and settings",
        [
            A(PlayerAction.ShowShortcuts, "Show the shortcut list"),
            A(PlayerAction.OpenSettings, "Open settings"),
        ]),
    ];

    private static readonly Dictionary<PlayerAction, string> Titles =
        Groups.SelectMany(g => g.Actions).ToDictionary(a => a.Action, a => a.Title);

    public static string TitleOf(PlayerAction action) => Titles.TryGetValue(action, out var t) ? t : action.ToString();

    public static string TitleOf(string actionId) =>
        Enum.TryParse<PlayerAction>(actionId, out var action) ? TitleOf(action) : actionId;
}

/// <summary>
/// What the welcome screen and the shortcut list (F1) show. Built from the current key table each time, so it always
/// shows the keys the user has actually set.
/// </summary>
public static class ShortcutGuide
{
    /// <summary>
    /// One row. With a single action it shows all of that action's keys; with several (like "Skip 5 seconds" = back and
    /// forward) it shows the first key of each. Null when none of them has a key.
    /// </summary>
    private static ShortcutRow? Row(string description, params PlayerAction[] actions)
    {
        var combos = actions.Length == 1
            ? KeyMap.BindingsOf(actions[0]).Select((g, i) => new ShortcutCombo(g.Caps, IsAlternative: i > 0)).ToList()
            : actions.Select(a => KeyMap.BindingsOf(a)).Where(b => b.Count > 0).Select(b => new ShortcutCombo(b[0].Caps)).ToList();
        return combos.Count == 0 ? null : new ShortcutRow(combos, description);
    }

    private static ShortcutGroup Group(string title, params ShortcutRow?[] rows) =>
        new(title, rows.OfType<ShortcutRow>().ToList());

    /// <summary>A short list for the welcome screen.</summary>
    public static IReadOnlyList<ShortcutGroup> Welcome() =>
    [
        Group("Playback",
            Row("Play / pause", PlayerAction.TogglePause),
            Row("Skip 5 seconds", PlayerAction.SeekBack5, PlayerAction.SeekForward5),
            Row("Volume", PlayerAction.VolumeUp, PlayerAction.VolumeDown),
            Row("Faster / slower", PlayerAction.SpeedUp, PlayerAction.SpeedDown),
            Row("Normal speed", PlayerAction.SpeedReset),
            Row("Fullscreen", PlayerAction.ToggleFullscreen)),
        Group("Study",
            Row("Add a bookmark", PlayerAction.AddBookmark),
            Row("Take a note", PlayerAction.NewNote),
            Row("Mark a section", PlayerAction.MarkSectionStart, PlayerAction.MarkSectionEnd),
            Row("Previous / next video", PlayerAction.PreviousVideo, PlayerAction.NextVideo),
            Row("New tab (a folder)", PlayerAction.NewTab),
            Row("Settings", PlayerAction.OpenSettings)),
    ];

    /// <summary>Every shortcut, for the F1 dialog.</summary>
    public static IReadOnlyList<ShortcutGroup> All()
    {
        var groups = new List<ShortcutGroup>();
        foreach (var group in ActionCatalog.Groups)
        {
            var rows = group.Actions
                .Select(a => Row(a.Title, a.Action) ?? new ShortcutRow([new ShortcutCombo(["not set"])], a.Title))
                .ToList();
            if (group.Actions.Any(a => a.Action == PlayerAction.MarkSectionEnd))
                rows.Add(new ShortcutRow([new ShortcutCombo(["Shift", "drag"])], "Mark a section with the mouse (on the seek bar)"));
            groups.Add(new ShortcutGroup(group.Title, rows));
        }
        return groups;
    }
}
