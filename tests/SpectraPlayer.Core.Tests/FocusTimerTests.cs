using SpectraPlayer.Core.Focus;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class FocusTimerTests
{
    private DateOnly _day = new(2026, 11, 24);
    private readonly List<FocusPhase> _ended = [];

    private FocusTimer NewTimer()
    {
        var t = new FocusTimer(() => _day);
        t.PartEnded += (_, phase) => _ended.Add(phase);
        return t;
    }

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Starts_stopped_at_the_full_length_of_the_first_preset()
    {
        var t = NewTimer();
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.False(t.Running);
        Assert.Equal(Min(25), t.Remaining);
        Assert.Equal(1500, t.SecondsLeft);
        Assert.Equal(1.0, t.FractionLeft);
        Assert.False(t.IsActive);
        Assert.Equal("Session 1 of 4", t.SessionText);
    }

    [Fact]
    public void Time_only_passes_while_it_is_running()
    {
        var t = NewTimer();
        t.Tick(Min(3));
        Assert.Equal(Min(25), t.Remaining);

        t.Start();
        t.Tick(Min(3));
        Assert.Equal(Min(22), t.Remaining);
        Assert.True(t.IsActive);

        t.Pause();
        t.Tick(Min(3));
        Assert.Equal(Min(22), t.Remaining);
        Assert.True(t.IsActive); // paused part-way: still worth showing
    }

    [Fact]
    public void Seconds_left_round_up_so_the_display_hits_zero_at_the_end()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(TimeSpan.FromSeconds(0.4));
        Assert.Equal(1500, t.SecondsLeft);              // 24:59.6 shows as 25:00
        t.Tick(TimeSpan.FromSeconds(0.7));
        Assert.Equal(1499, t.SecondsLeft);
    }

    [Fact]
    public void The_ring_shrinks_as_time_passes()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(5));
        Assert.Equal(0.8, t.FractionLeft, precision: 6);
    }

    [Fact]
    public void A_finished_focus_session_counts_and_starts_the_break_which_keeps_running()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(25));

        Assert.Equal([FocusPhase.Focus], _ended);
        Assert.Equal(FocusPhase.Break, t.Phase);
        Assert.True(t.Running);
        Assert.Equal(Min(5), t.Remaining);
        Assert.Equal(1, t.CompletedSessions);
        Assert.Equal(1, t.SessionsToday);
        Assert.Equal("Break after session 1 of 4", t.SessionText);
    }

    [Fact]
    public void Only_focus_time_is_credited_to_today_not_break_time()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(25));   // the focus session
        t.Tick(Min(5));    // the break
        t.Tick(Min(10));   // into the next focus session
        Assert.Equal(Min(35), t.FocusedToday);
    }

    [Fact]
    public void A_tick_that_overshoots_credits_only_the_time_that_was_left()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(24));
        t.Tick(Min(10)); // only 1 minute of focus was left
        Assert.Equal(Min(25), t.FocusedToday);
    }

    [Fact]
    public void After_the_break_comes_focus_again()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(25));
        t.Tick(Min(5));
        Assert.Equal([FocusPhase.Focus, FocusPhase.Break], _ended);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(Min(25), t.Remaining);
        Assert.Equal("Session 2 of 4", t.SessionText);
    }

    [Fact]
    public void A_round_is_four_sessions_then_it_starts_over()
    {
        var t = NewTimer();
        t.Start();
        for (var i = 1; i <= 4; i++)
        {
            t.Tick(Min(25));
            Assert.Equal(i, t.CompletedSessions);
            if (i < 4) t.Tick(Min(5));
        }
        Assert.Equal("Break after session 4 of 4", t.SessionText);

        t.Tick(Min(5)); // the last break ends
        Assert.Equal(0, t.CompletedSessions);
        Assert.Equal("Session 1 of 4", t.SessionText);
        Assert.Equal(4, t.SessionsToday); // today's total keeps counting across rounds
    }

    [Fact]
    public void Skipping_does_not_count_a_session()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(10));
        t.Skip();

        Assert.Equal(FocusPhase.Break, t.Phase);
        Assert.Equal(0, t.CompletedSessions);
        Assert.Equal(0, t.SessionsToday);
        Assert.Equal(Min(5), t.Remaining);
        Assert.True(t.Running);          // skipping keeps the timer going
        Assert.Empty(_ended);            // and is not "a part ended"
        Assert.Equal(Min(10), t.FocusedToday); // the ten minutes really were focused
    }

    [Fact]
    public void Skipping_a_break_goes_back_to_focus()
    {
        var t = NewTimer();
        t.Skip();                         // focus → break
        t.Skip();                         // break → focus
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(Min(25), t.Remaining);
    }

    [Fact]
    public void Reset_goes_back_to_the_full_length_and_stops()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(7));
        t.Reset();
        Assert.False(t.Running);
        Assert.Equal(Min(25), t.Remaining);
        Assert.False(t.IsActive);
        Assert.Equal(Min(7), t.FocusedToday); // what was done stays done
    }

    [Fact]
    public void Choosing_a_length_starts_a_fresh_stopped_focus_session()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(25)); // now in a break
        t.SelectPreset(1);

        Assert.Equal(1, t.PresetIndex);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.False(t.Running);
        Assert.Equal(Min(50), t.Remaining);

        t.Start();
        t.Tick(Min(50));
        Assert.Equal(Min(10), t.Remaining); // the break of this preset
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(99, 3)]
    public void A_length_out_of_range_is_clamped(int asked, int expected)
    {
        var t = NewTimer();
        t.SelectPreset(asked);
        Assert.Equal(expected, t.PresetIndex);
    }

    [Fact]
    public void Today_starts_again_after_midnight()
    {
        var t = NewTimer();
        t.Start();
        t.Tick(Min(25));
        Assert.Equal(1, t.SessionsToday);

        _day = _day.AddDays(1);
        t.Tick(Min(2));
        Assert.Equal(0, t.SessionsToday);
        Assert.Equal(TimeSpan.Zero, t.FocusedToday); // the break after midnight is not focus time
        t.Tick(Min(3));
        t.Tick(Min(1));
        Assert.Equal(Min(1), t.FocusedToday);
    }

    [Fact]
    public void Changed_is_raised_for_every_change()
    {
        var t = NewTimer();
        var count = 0;
        t.Changed += (_, _) => count++;
        t.Start();
        t.Tick(TimeSpan.FromSeconds(1));
        t.Pause();
        t.Reset();
        t.Skip();
        t.SelectPreset(2);
        Assert.Equal(6, count);
    }

    [Fact]
    public void Starting_when_already_running_changes_nothing()
    {
        var t = NewTimer();
        t.Start();
        var count = 0;
        t.Changed += (_, _) => count++;
        t.Start();
        Assert.Equal(0, count);
    }

    // ---- saving ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_choices_and_todays_totals_survive_a_restart()
    {
        var t = NewTimer();
        t.SelectPreset(2);
        t.PauseVideoOnBreaks = false;
        t.ChimeWhenPartEnds = false;
        t.CountOnlyWhilePlaying = true;
        t.Start();
        t.Tick(Min(90));

        var again = NewTimer();
        again.LoadJson(t.ToJson());
        Assert.Equal(2, again.PresetIndex);
        Assert.False(again.PauseVideoOnBreaks);
        Assert.False(again.ChimeWhenPartEnds);
        Assert.True(again.CountOnlyWhilePlaying);
        Assert.Equal(Min(90), again.FocusedToday);
        Assert.Equal(1, again.SessionsToday);
        // but the timer itself starts stopped, at the start of a session
        Assert.False(again.Running);
        Assert.Equal(FocusPhase.Focus, again.Phase);
        Assert.Equal(Min(90), again.Remaining);
        Assert.Equal(0, again.CompletedSessions);
    }

    [Fact]
    public void Totals_from_another_day_are_not_restored_but_the_choices_are()
    {
        var t = NewTimer();
        t.SelectPreset(1);
        t.Start();
        t.Tick(Min(50));
        var json = t.ToJson();

        _day = _day.AddDays(3);
        var later = NewTimer();
        later.LoadJson(json);
        Assert.Equal(1, later.PresetIndex);
        Assert.Equal(TimeSpan.Zero, later.FocusedToday);
        Assert.Equal(0, later.SessionsToday);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"Preset\":\"x\"}")]
    public void Unreadable_saved_data_means_the_defaults(string? json)
    {
        var t = NewTimer();
        t.SelectPreset(3);
        t.LoadJson(json);
        Assert.Equal(0, t.PresetIndex);
        Assert.True(t.PauseVideoOnBreaks);
        Assert.True(t.ChimeWhenPartEnds);
        Assert.False(t.CountOnlyWhilePlaying);
        Assert.Equal(TimeSpan.Zero, t.FocusedToday);
    }

    [Fact]
    public void Nonsense_numbers_in_saved_data_are_ignored()
    {
        var t = NewTimer();
        t.LoadJson("{\"Preset\":99,\"PauseVideo\":true,\"Chime\":true,\"OnlyWhilePlaying\":false,\"Day\":\"2026-11-24\",\"Seconds\":-50,\"Sessions\":-2}");
        Assert.Equal(3, t.PresetIndex);   // clamped
        Assert.Equal(TimeSpan.Zero, t.FocusedToday);
        Assert.Equal(0, t.SessionsToday);
    }
}
