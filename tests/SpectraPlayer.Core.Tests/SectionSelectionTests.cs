using Xunit;

namespace SpectraPlayer.Core.Tests;

public class SectionSelectionTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Starts_empty_and_incomplete()
    {
        var sel = new SectionSelection();
        Assert.True(sel.IsEmpty);
        Assert.False(sel.IsComplete);
        Assert.Equal(TimeSpan.Zero, sel.Length);
    }

    [Fact]
    public void Marking_start_then_end_gives_a_complete_section()
    {
        var sel = new SectionSelection();
        sel.SetStart(S(10));
        Assert.False(sel.IsComplete);
        sel.SetEnd(S(70));

        Assert.True(sel.IsComplete);
        Assert.Equal(S(10), sel.Start);
        Assert.Equal(S(70), sel.End);
        Assert.Equal(S(60), sel.Length);
    }

    [Fact]
    public void Marking_end_first_leaves_the_start_unset()
    {
        var sel = new SectionSelection();
        sel.SetEnd(S(50));
        Assert.Null(sel.Start);
        Assert.Equal(S(50), sel.End);
        Assert.False(sel.IsComplete);
    }

    [Fact]
    public void Marking_end_before_start_swaps_them()
    {
        var sel = new SectionSelection();
        sel.SetStart(S(100));
        sel.SetEnd(S(40));
        Assert.Equal(S(40), sel.Start);
        Assert.Equal(S(100), sel.End);
    }

    [Fact]
    public void Marking_start_after_end_swaps_them()
    {
        var sel = new SectionSelection();
        sel.SetEnd(S(40));
        sel.SetStart(S(100));
        Assert.Equal(S(40), sel.Start);
        Assert.Equal(S(100), sel.End);
    }

    [Fact]
    public void Marking_the_same_point_twice_keeps_only_that_bound()
    {
        var sel = new SectionSelection();
        sel.SetStart(S(30));
        sel.SetEnd(S(30));
        Assert.Equal(S(30), sel.Start);
        Assert.Null(sel.End);
    }

    [Fact]
    public void Moving_one_end_keeps_the_other()
    {
        var sel = new SectionSelection();
        sel.Set(S(10), S(60));
        sel.SetStart(S(20));
        Assert.Equal((S(20), S(60)), (sel.Start!.Value, sel.End!.Value));
        sel.SetEnd(S(90));
        Assert.Equal((S(20), S(90)), (sel.Start!.Value, sel.End!.Value));
    }

    [Fact]
    public void Set_accepts_either_order_and_a_zero_length_drag_is_just_a_start()
    {
        var sel = new SectionSelection();
        sel.Set(S(80), S(20));
        Assert.Equal((S(20), S(80)), (sel.Start!.Value, sel.End!.Value));

        sel.Set(S(33), S(33));
        Assert.Equal(S(33), sel.Start);
        Assert.Null(sel.End);
    }

    [Fact]
    public void A_section_under_one_second_is_not_complete()
    {
        var sel = new SectionSelection();
        sel.Set(S(10), S(10.5));
        Assert.False(sel.IsComplete);
        sel.Set(S(10), S(11));
        Assert.True(sel.IsComplete);
    }

    [Fact]
    public void Values_are_clamped_to_the_video()
    {
        var sel = new SectionSelection();
        sel.Set(S(-5), S(500), duration: S(120));
        Assert.Equal((S(0), S(120)), (sel.Start!.Value, sel.End!.Value));

        sel.Clear();
        sel.SetStart(S(999), duration: S(120));
        Assert.Equal(S(120), sel.Start);
    }

    [Fact]
    public void Last_selects_the_stretch_before_the_playhead()
    {
        var sel = new SectionSelection();
        sel.SelectLast(S(120), S(600), S(1800));
        Assert.Equal((S(480), S(600)), (sel.Start!.Value, sel.End!.Value));
    }

    [Fact]
    public void Last_near_the_beginning_takes_the_first_stretch_instead()
    {
        var sel = new SectionSelection();
        sel.SelectLast(S(120), S(0.2), S(1800));
        Assert.Equal((S(0), S(120)), (sel.Start!.Value, sel.End!.Value));

        sel.SelectLast(S(120), S(30), S(1800)); // only 30 s available before the playhead: use all of it
        Assert.Equal((S(0), S(30)), (sel.Start!.Value, sel.End!.Value));
    }

    [Fact]
    public void Whole_covers_the_full_video_and_is_ignored_when_the_length_is_unknown()
    {
        var sel = new SectionSelection();
        sel.SelectWhole(TimeSpan.Zero);
        Assert.True(sel.IsEmpty);

        sel.SelectWhole(S(1800));
        Assert.Equal((S(0), S(1800)), (sel.Start!.Value, sel.End!.Value));
    }

    [Fact]
    public void Changed_fires_only_when_something_actually_changes()
    {
        var sel = new SectionSelection();
        var count = 0;
        sel.Changed += (_, _) => count++;

        sel.SetStart(S(10));
        sel.SetStart(S(10)); // no change
        sel.Clear();
        sel.Clear();         // already empty

        Assert.Equal(2, count);
    }
}
