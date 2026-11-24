namespace SpectraPlayer.Core.Shortcuts;

/// <summary>
/// One key press as a shortcut: a key plus Ctrl / Shift / Alt. The key is named like WPF's Key enum (so the app can convert
/// both ways), with a few canonical spellings: Enter, PageUp, PageDown, Escape. Everything here is plain text, so it is
/// testable without WPF.
/// </summary>
public readonly record struct Gesture(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    private static readonly string[] NamedKeys =
        ["Space", "Enter", "Escape", "Tab", "Back", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down"];

    // How mpv spells the same keys, for the key presses that reach the video surface instead of the window.
    private static readonly Dictionary<string, string> NamedMpv = new()
    {
        ["Space"] = "SPACE", ["Enter"] = "ENTER", ["Escape"] = "ESC", ["Tab"] = "TAB", ["Back"] = "BS", ["Delete"] = "DEL",
        ["Insert"] = "INS", ["Home"] = "HOME", ["End"] = "END", ["PageUp"] = "PGUP", ["PageDown"] = "PGDWN",
        ["Left"] = "LEFT", ["Right"] = "RIGHT", ["Up"] = "UP", ["Down"] = "DOWN",
    };

    // Punctuation keys: (plain, with Shift) on a US keyboard. mpv names a key by the character it types.
    private static readonly Dictionary<string, (string Plain, string Shifted)> Punctuation = new()
    {
        ["OemOpenBrackets"] = ("[", "{"), ["OemCloseBrackets"] = ("]", "}"), ["OemComma"] = (",", "<"), ["OemPeriod"] = (".", ">"),
        ["OemMinus"] = ("-", "_"), ["OemPlus"] = ("=", "+"), ["OemSemicolon"] = (";", ":"), ["OemQuotes"] = ("'", "\""),
        ["OemQuestion"] = ("/", "?"), ["OemPipe"] = ("\\", "|"), ["OemTilde"] = ("`", "~"),
    };

    private const string ShiftedDigits = ")!@#$%^&*(";

    private static readonly Dictionary<string, string> Canonical = BuildCanonical();

    private static Dictionary<string, string> BuildCanonical()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name) => map[name] = name;

        for (var c = 'A'; c <= 'Z'; c++) Add(c.ToString());
        for (var d = 0; d <= 9; d++) { Add($"D{d}"); Add($"NumPad{d}"); }
        for (var f = 1; f <= 12; f++) Add($"F{f}");
        foreach (var n in NamedKeys) Add(n);
        foreach (var p in Punctuation.Keys) Add(p);

        // spellings WPF and people use for the same keys
        map["Return"] = "Enter"; map["Esc"] = "Escape"; map["Next"] = "PageDown"; map["Prior"] = "PageUp";
        map["Backspace"] = "Back"; map["Del"] = "Delete"; map["Ins"] = "Insert";
        // WPF's Key.ToString() prints the OEM numbers for some punctuation keys (for example "Oem6" for the ] key)
        map["Oem1"] = "OemSemicolon"; map["Oem2"] = "OemQuestion"; map["Oem3"] = "OemTilde"; map["Oem4"] = "OemOpenBrackets";
        map["Oem5"] = "OemPipe"; map["Oem6"] = "OemCloseBrackets"; map["Oem7"] = "OemQuotes";
        return map;
    }

    /// <summary>The canonical name of a key, or null if shortcuts can't use it.</summary>
    public static string? CanonicalKey(string? name) =>
        name is not null && Canonical.TryGetValue(name, out var canonical) ? canonical : null;

    /// <summary>Is this a combination shortcuts may use? (A known key, and not one Windows reserves, like Alt+F4.)</summary>
    public bool IsAllowed =>
        Key is not null && CanonicalKey(Key) == Key && !(Alt && Key is "F4" or "Space");

    // ---- text form -------------------------------------------------------------------------------------------

    /// <summary>The form that is saved and parsed back: "Ctrl+Alt+Shift+Key".</summary>
    public string Serialize()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Gesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, shift = false, alt = false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: return false;
            }
        }

        var key = CanonicalKey(parts[^1]);
        if (key is null) return false;
        gesture = new Gesture(key, ctrl, shift, alt);
        return gesture.IsAllowed;
    }

    /// <summary>The key caps to draw, e.g. ["Ctrl", "Shift", "Tab"].</summary>
    public IReadOnlyList<string> Caps
    {
        get
        {
            var caps = new List<string>(4);
            if (Ctrl) caps.Add("Ctrl");
            if (Alt) caps.Add("Alt");
            if (Shift) caps.Add("Shift");
            caps.Add(KeyLabel(Key));
            return caps;
        }
    }

    /// <summary>For tooltips and lists: "Ctrl+B".</summary>
    public string Display => string.Join('+', Caps);

    private static string KeyLabel(string key)
    {
        if (Punctuation.TryGetValue(key, out var p)) return p.Plain;
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return key[1].ToString();
        if (key.StartsWith("NumPad", StringComparison.Ordinal)) return "Num " + key[6..];
        return key switch
        {
            "Left" => "←", "Right" => "→", "Up" => "↑", "Down" => "↓",
            "PageUp" => "PgUp", "PageDown" => "PgDn", "Escape" => "Esc", "Back" => "Backspace",
            "Delete" => "Del", "Insert" => "Ins",
            _ => key,
        };
    }

    // ---- mpv -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The same key as mpv names it ("ctrl+LEFT", "PGDWN", ">"). False for a combination mpv can't express, such as
    /// Ctrl+Shift+1 (the character depends on the keyboard layout); it then only works when the window has the focus.
    /// </summary>
    public bool TryGetMpvKey(out string mpvKey)
    {
        mpvKey = string.Empty;
        if (!IsAllowed) return false;

        string name;
        var shiftIsModifier = Shift;

        if (Key.Length == 1 && char.IsAsciiLetter(Key[0]))
        {
            name = Shift ? Key : Key.ToLowerInvariant();   // mpv: shift+a is "A"
            shiftIsModifier = false;
        }
        else if (Key.Length == 2 && Key[0] == 'D' && char.IsAsciiDigit(Key[1]))
        {
            if (Shift)
            {
                if (Ctrl || Alt) return false;
                name = ShiftedDigits[Key[1] - '0'].ToString();
                shiftIsModifier = false;
            }
            else name = Key[1].ToString();
        }
        else if (Punctuation.TryGetValue(Key, out var p))
        {
            if (Shift)
            {
                if (Ctrl || Alt) return false;
                name = p.Shifted;
                shiftIsModifier = false;
            }
            else name = p.Plain;
        }
        else if (Key.StartsWith("NumPad", StringComparison.Ordinal)) name = "KP" + Key[6..];
        else if (Key[0] == 'F' && Key.Length <= 3 && char.IsAsciiDigit(Key[1])) name = Key;
        else if (NamedMpv.TryGetValue(Key, out var named)) name = named;
        else return false;

        var prefix = (Ctrl ? "ctrl+" : "") + (Alt ? "alt+" : "") + (shiftIsModifier ? "shift+" : "");
        mpvKey = prefix + name;
        return true;
    }
}
