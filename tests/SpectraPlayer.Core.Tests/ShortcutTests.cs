using SpectraPlayer.Core.Shortcuts;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class GestureTests
{
    [Theory]
    [InlineData("Space", "SPACE")]
    [InlineData("Left", "LEFT")]
    [InlineData("Ctrl+Left", "ctrl+LEFT")]
    [InlineData("M", "m")]
    [InlineData("Enter", "ENTER")]
    [InlineData("Escape", "ESC")]
    [InlineData("F11", "F11")]
    [InlineData("Ctrl+B", "ctrl+b")]
    [InlineData("OemOpenBrackets", "[")]
    [InlineData("OemCloseBrackets", "]")]
    [InlineData("PageUp", "PGUP")]
    [InlineData("PageDown", "PGDWN")]
    [InlineData("Ctrl+Tab", "ctrl+TAB")]
    [InlineData("Ctrl+Shift+Tab", "ctrl+shift+TAB")]
    [InlineData("Shift+OemPeriod", ">")]
    [InlineData("Shift+OemComma", "<")]
    [InlineData("Ctrl+D1", "ctrl+1")]
    [InlineData("Shift+A", "A")]
    [InlineData("Alt+X", "alt+x")]
    [InlineData("Shift+Left", "shift+LEFT")]
    [InlineData("Shift+D1", "!")]
    [InlineData("NumPad3", "KP3")]
    public void Keys_are_named_the_way_mpv_names_them(string gesture, string expected)
    {
        Assert.True(Gesture.TryParse(gesture, out var g));
        Assert.True(g.TryGetMpvKey(out var mpv));
        Assert.Equal(expected, mpv);
    }

    [Theory]
    [InlineData("Ctrl+Shift+D1")]       // the character depends on the keyboard layout
    [InlineData("Alt+Shift+OemPeriod")]
    public void Combinations_mpv_cannot_express_are_refused_by_the_mpv_mapping_only(string gesture)
    {
        Assert.True(Gesture.TryParse(gesture, out var g));
        Assert.True(g.IsAllowed);
        Assert.False(g.TryGetMpvKey(out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]                 // a modifier alone is not a shortcut
    [InlineData("Ctrl+")]
    [InlineData("Banana")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Win+A")]
    [InlineData("Alt+F4")]               // closes windows
    [InlineData("Alt+Space")]            // window menu
    [InlineData("A+B")]
    public void Unusable_text_does_not_parse(string text) => Assert.False(Gesture.TryParse(text, out _));

    [Fact]
    public void Parsing_ignores_case_and_accepts_the_usual_spellings()
    {
        Assert.True(Gesture.TryParse("control + shift + return", out var g));
        Assert.Equal(new Gesture("Enter", Ctrl: true, Shift: true), g);
        Assert.True(Gesture.TryParse("next", out var pageDown));
        Assert.Equal("PageDown", pageDown.Key);
        Assert.True(Gesture.TryParse("esc", out var esc));
        Assert.Equal("Escape", esc.Key);
    }

    [Theory]
    [InlineData("Oem1", "OemSemicolon")]
    [InlineData("Oem2", "OemQuestion")]
    [InlineData("Oem3", "OemTilde")]
    [InlineData("Oem4", "OemOpenBrackets")]
    [InlineData("Oem5", "OemPipe")]
    [InlineData("Oem6", "OemCloseBrackets")]   // what WPF prints for the ] key
    [InlineData("Oem7", "OemQuotes")]
    public void The_oem_numbers_wpf_prints_map_to_the_named_keys(string wpfName, string canonical) =>
        Assert.Equal(canonical, Gesture.CanonicalKey(wpfName));

    [Theory]
    [InlineData("Ctrl+Alt+Shift+K")]
    [InlineData("Space")]
    [InlineData("Ctrl+OemComma")]
    [InlineData("Shift+PageDown")]
    public void Serialising_and_parsing_round_trip(string text)
    {
        Assert.True(Gesture.TryParse(text, out var g));
        Assert.Equal(text, g.Serialize());
        Assert.True(Gesture.TryParse(g.Serialize(), out var again));
        Assert.Equal(g, again);
    }

    [Fact]
    public void Modifiers_are_always_written_in_the_same_order()
    {
        Assert.True(Gesture.TryParse("Shift+Alt+Ctrl+Q", out var g));
        Assert.Equal("Ctrl+Alt+Shift+Q", g.Serialize());
    }

    [Theory]
    [InlineData("Ctrl+B", "Ctrl+B")]
    [InlineData("Left", "←")]
    [InlineData("Ctrl+Right", "Ctrl+→")]
    [InlineData("PageDown", "PgDn")]
    [InlineData("D5", "5")]
    [InlineData("OemOpenBrackets", "[")]
    [InlineData("Shift+OemPeriod", "Shift+.")]
    [InlineData("Escape", "Esc")]
    public void Keys_are_displayed_the_way_people_read_them(string gesture, string display)
    {
        Assert.True(Gesture.TryParse(gesture, out var g));
        Assert.Equal(display, g.Display);
    }
}

public class ShortcutTableTests
{
    private static Gesture G(string text)
    {
        Assert.True(Gesture.TryParse(text, out var g), text);
        return g;
    }

    private static ShortcutTable NewTable() => new(
    [
        ("Play", G("Space")),
        ("Mute", G("M")),
        ("Fullscreen", G("Enter")),
        ("Fullscreen", G("F11")),
        ("Faster", G("C")),
        ("Faster", G("Shift+OemPeriod")),
        ("Slower", G("X")),
    ]);

    [Fact]
    public void Starts_with_the_defaults_and_nothing_customised()
    {
        var t = NewTable();
        Assert.Equal(["Play", "Mute", "Fullscreen", "Faster", "Slower"], t.Actions);
        Assert.Equal([G("Enter"), G("F11")], t.BindingsOf("Fullscreen"));
        Assert.False(t.AnyCustomised);
        Assert.Equal("{}", t.ToJson());
        Assert.Equal("Mute", t.ActionFor(G("M")));
        Assert.Null(t.ActionFor(G("Q")));
    }

    [Fact]
    public void Replacing_a_key_keeps_its_place_and_the_other_keys()
    {
        var t = NewTable();
        var displaced = t.Assign("Fullscreen", G("Q"), replacing: G("Enter"));
        Assert.Empty(displaced);
        Assert.Equal([G("Q"), G("F11")], t.BindingsOf("Fullscreen"));
        Assert.Null(t.ActionFor(G("Enter")));
        Assert.True(t.IsCustomised("Fullscreen"));
        Assert.False(t.IsCustomised("Play"));
    }

    [Fact]
    public void Adding_a_key_gives_the_action_another_one()
    {
        var t = NewTable();
        t.Assign("Play", G("P"));
        Assert.Equal([G("Space"), G("P")], t.BindingsOf("Play"));
    }

    [Fact]
    public void A_key_that_is_taken_moves_to_the_new_action_and_says_who_lost_it()
    {
        var t = NewTable();
        var displaced = t.Assign("Play", G("M"));
        Assert.Equal(["Mute"], displaced);
        Assert.Empty(t.BindingsOf("Mute"));
        Assert.Equal("Play", t.ActionFor(G("M")));
        Assert.True(t.IsCustomised("Mute"));
    }

    [Fact]
    public void Choosing_a_key_the_action_already_has_changes_nothing_but_merges_a_replaced_key()
    {
        var t = NewTable();
        Assert.Empty(t.Assign("Fullscreen", G("F11")));
        Assert.Equal([G("Enter"), G("F11")], t.BindingsOf("Fullscreen"));

        Assert.Empty(t.Assign("Fullscreen", G("F11"), replacing: G("Enter")));
        Assert.Equal([G("F11")], t.BindingsOf("Fullscreen"));
    }

    [Fact]
    public void A_replaced_key_that_is_not_the_actions_own_is_added_instead()
    {
        var t = NewTable();
        t.Assign("Mute", G("Q"), replacing: G("Z"));
        Assert.Equal([G("M"), G("Q")], t.BindingsOf("Mute"));
    }

    [Fact]
    public void Removing_the_last_key_leaves_the_action_without_one()
    {
        var t = NewTable();
        t.Remove("Mute", G("M"));
        Assert.Empty(t.BindingsOf("Mute"));
        Assert.True(t.IsCustomised("Mute"));
        Assert.Null(t.ActionFor(G("M")));
    }

    [Fact]
    public void Reset_restores_the_defaults_and_takes_them_back_from_whoever_has_them()
    {
        var t = NewTable();
        t.Assign("Play", G("M"));
        var displaced = t.Reset("Mute");
        Assert.Equal(["Play"], displaced);
        Assert.Equal([G("M")], t.BindingsOf("Mute"));
        Assert.Equal([G("Space")], t.BindingsOf("Play"));
        Assert.False(t.AnyCustomised);
    }

    [Fact]
    public void Reset_all_restores_everything()
    {
        var t = NewTable();
        t.Assign("Play", G("M"));
        t.Assign("Slower", G("Ctrl+K"), replacing: G("X"));
        t.Remove("Fullscreen", G("F11"));
        t.ResetAll();
        Assert.False(t.AnyCustomised);
        Assert.Equal("{}", t.ToJson());
        Assert.Equal([G("Enter"), G("F11")], t.BindingsOf("Fullscreen"));
    }

    [Fact]
    public void Unusable_gestures_and_unknown_actions_are_refused()
    {
        var t = NewTable();
        Assert.Throws<ArgumentException>(() => t.Assign("Play", new Gesture("Banana")));
        Assert.Throws<ArgumentException>(() => t.Assign("Play", new Gesture("F4", Alt: true)));
        Assert.Throws<ArgumentException>(() => t.Assign("Nope", G("Q")));
    }

    [Fact]
    public void Changes_raise_the_changed_event()
    {
        var t = NewTable();
        var count = 0;
        t.Changed += (_, _) => count++;
        t.Assign("Play", G("P"));
        t.Remove("Play", G("P"));
        t.Reset("Play");
        t.ResetAll();
        Assert.Equal(4, count);
    }

    [Fact]
    public void Only_changed_actions_are_saved_and_they_load_back()
    {
        var t = NewTable();
        t.Assign("Play", G("M"));                 // Mute loses M, so both are saved
        t.Assign("Slower", G("Ctrl+K"), replacing: G("X"));
        var json = t.ToJson();
        Assert.DoesNotContain("Fullscreen", json);
        Assert.DoesNotContain("Faster", json);

        var loaded = NewTable();
        loaded.LoadJson(json);
        foreach (var action in t.Actions) Assert.Equal(t.BindingsOf(action), loaded.BindingsOf(action));
        Assert.Equal(json, loaded.ToJson());
    }

    [Fact]
    public void An_action_with_no_key_is_saved_and_stays_without_one()
    {
        var t = NewTable();
        t.Remove("Mute", G("M"));
        var loaded = NewTable();
        loaded.LoadJson(t.ToJson());
        Assert.Empty(loaded.BindingsOf("Mute"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Play\": 5}")]
    public void Unreadable_saved_data_means_the_defaults(string? json)
    {
        var t = NewTable();
        t.Assign("Play", G("P")); // something to be undone by loading
        t.LoadJson(json);
        Assert.False(t.AnyCustomised);
        Assert.Equal([G("Space")], t.BindingsOf("Play"));
    }

    [Fact]
    public void Unknown_actions_and_bad_keys_in_saved_data_are_ignored()
    {
        var t = NewTable();
        t.LoadJson("{\"Gone\":[\"Q\"],\"Play\":[\"P\",\"Banana\",\"Alt+F4\",\"P\"]}");
        Assert.Equal([G("P")], t.BindingsOf("Play"));
        Assert.Equal([G("M")], t.BindingsOf("Mute"));
    }

    [Fact]
    public void A_saved_key_beats_the_default_of_an_action_that_was_not_changed()
    {
        // e.g. a later version gave "Slower" the key Q by default, but the user had saved Q for Play
        var t = NewTable();
        t.LoadJson("{\"Play\":[\"X\"]}");
        Assert.Equal("Play", t.ActionFor(G("X")));
        Assert.Empty(t.BindingsOf("Slower"));
    }

    [Fact]
    public void A_key_saved_for_two_actions_goes_to_the_first_in_table_order()
    {
        var t = NewTable();
        t.LoadJson("{\"Mute\":[\"Q\"],\"Play\":[\"Q\"]}");
        Assert.Equal("Play", t.ActionFor(G("Q")));   // Play comes first in the table
        Assert.Empty(t.BindingsOf("Mute"));
    }

    [Fact]
    public void Two_defaults_on_one_key_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => new ShortcutTable([("A", G("Q")), ("B", G("Q"))]));
    }
}
