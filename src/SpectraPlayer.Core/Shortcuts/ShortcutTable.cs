using System.Text.Json;

namespace SpectraPlayer.Core.Shortcuts;

/// <summary>
/// Which keys do what. Starts from a set of defaults; the user's changes are kept separately and saved as JSON that lists
/// only the actions they changed, so a new default key added in a later version still reaches everyone who didn't touch it.
/// A key belongs to one action at a time: giving a key to an action takes it away from whoever had it.
/// </summary>
public sealed class ShortcutTable
{
    private readonly List<string> _actions = [];
    private readonly Dictionary<string, Gesture[]> _defaults = [];
    private readonly Dictionary<string, List<Gesture>> _current = [];

    public ShortcutTable(IEnumerable<(string Action, Gesture Gesture)> defaults)
    {
        foreach (var (action, gesture) in defaults)
        {
            if (!_defaults.ContainsKey(action))
            {
                _actions.Add(action);
                _defaults[action] = [];
                _current[action] = [];
            }
            if (!gesture.IsAllowed) throw new ArgumentException($"Default shortcut for {action} is not usable: {gesture.Serialize()}");
            if (ActionFor(gesture) is { } owner) throw new ArgumentException($"{gesture.Serialize()} is the default for both {owner} and {action}.");
            _current[action].Add(gesture);
            _defaults[action] = [.. _current[action]];
        }
    }

    /// <summary>Raised after every change, including <see cref="LoadJson"/>.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<string> Actions => _actions;

    public IReadOnlyList<Gesture> BindingsOf(string action) =>
        _current.TryGetValue(action, out var list) ? list : [];

    public IReadOnlyList<Gesture> DefaultsOf(string action) =>
        _defaults.TryGetValue(action, out var list) ? list : [];

    public IEnumerable<(string Action, Gesture Gesture)> All =>
        _actions.SelectMany(a => _current[a].Select(g => (a, g)));

    /// <summary>The action a key press triggers, or null.</summary>
    public string? ActionFor(Gesture gesture)
    {
        foreach (var action in _actions)
            if (_current[action].Contains(gesture)) return action;
        return null;
    }

    public bool IsCustomised(string action) =>
        _current.TryGetValue(action, out var list) && !SameSet(list, _defaults[action]);

    public bool AnyCustomised => _actions.Any(IsCustomised);

    // ---- changes ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives <paramref name="gesture"/> to <paramref name="action"/>, in place of <paramref name="replacing"/> if that is
    /// one of its keys, otherwise as an extra key. Returns the actions that lost the key to it.
    /// </summary>
    public IReadOnlyList<string> Assign(string action, Gesture gesture, Gesture? replacing = null)
    {
        if (!_current.TryGetValue(action, out var list)) throw new ArgumentException($"Unknown action {action}.", nameof(action));
        if (!gesture.IsAllowed) throw new ArgumentException($"{gesture.Serialize()} can't be used as a shortcut.", nameof(gesture));

        var displaced = new List<string>();
        var owner = ActionFor(gesture);

        if (owner == action)
        {
            if (replacing is { } merged && merged != gesture) list.Remove(merged); // it already had this key: just drop the old one
        }
        else
        {
            if (owner is not null)
            {
                _current[owner].Remove(gesture);
                displaced.Add(owner);
            }
            var at = replacing is { } old ? list.IndexOf(old) : -1;
            if (at >= 0) list[at] = gesture;
            else list.Add(gesture);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return displaced;
    }

    public void Remove(string action, Gesture gesture)
    {
        if (_current.TryGetValue(action, out var list) && list.Remove(gesture))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the default keys for one action (taking them back from any action that has since been given them).</summary>
    public IReadOnlyList<string> Reset(string action)
    {
        if (!_current.ContainsKey(action)) return [];
        var displaced = RestoreDefaults(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return displaced;
    }

    public void ResetAll()
    {
        foreach (var action in _actions) _current[action] = [.. _defaults[action]];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<string> RestoreDefaults(string action)
    {
        var displaced = new List<string>();
        _current[action] = [];
        foreach (var gesture in _defaults[action])
        {
            if (ActionFor(gesture) is { } owner)
            {
                _current[owner].Remove(gesture);
                if (!displaced.Contains(owner)) displaced.Add(owner);
            }
            _current[action].Add(gesture);
        }
        return displaced;
    }

    // ---- saving ----------------------------------------------------------------------------------------------

    /// <summary>Only what differs from the defaults; "{}" when nothing was changed. An action with no key is an empty list.</summary>
    public string ToJson()
    {
        var changed = _actions.Where(IsCustomised)
            .ToDictionary(a => a, a => _current[a].Select(g => g.Serialize()).ToArray());
        return JsonSerializer.Serialize(changed);
    }

    /// <summary>
    /// Starts again from the defaults and applies saved changes. Anything unreadable, unknown or no longer allowed is
    /// ignored, and a key claimed by two changed actions goes to the first.
    /// </summary>
    public void LoadJson(string? json)
    {
        foreach (var action in _actions) _current[action] = [.. _defaults[action]];

        Dictionary<string, string[]?>? saved = null;
        try { if (!string.IsNullOrWhiteSpace(json)) saved = JsonSerializer.Deserialize<Dictionary<string, string[]?>>(json); }
        catch (JsonException) { }

        if (saved is not null)
        {
            var claimed = new HashSet<Gesture>();
            var changedActions = new List<string>();

            foreach (var action in _actions) // in the table's order, so "the first" is the same on every run
            {
                if (!saved.TryGetValue(action, out var keys)) continue;
                var list = new List<Gesture>();
                foreach (var text in keys ?? [])
                    if (Gesture.TryParse(text, out var g) && claimed.Add(g)) list.Add(g);
                _current[action] = list;
                changedActions.Add(action);
            }

            foreach (var action in _actions.Except(changedActions)) // unchanged actions give way to a key someone else now has
                _current[action].RemoveAll(claimed.Contains);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameSet(IEnumerable<Gesture> a, IEnumerable<Gesture> b) =>
        a.ToHashSet().SetEquals(b);
}
