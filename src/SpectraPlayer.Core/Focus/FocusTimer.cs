using System.Globalization;
using System.Text.Json;

namespace SpectraPlayer.Core.Focus;

public enum FocusPhase { Focus, Break }

/// <param name="FocusMinutes">Length of a focus session.</param>
/// <param name="BreakMinutes">Length of the break after it.</param>
public sealed record FocusPreset(int FocusMinutes, int BreakMinutes)
{
    public string Label => $"{FocusMinutes} / {BreakMinutes}";
}

/// <summary>
/// A study-session timer: focus for a while, rest for a while, four sessions to a round. It knows nothing about clocks or
/// windows: whoever owns it calls <see cref="Tick"/> with the time that has passed (and can simply stop calling it while the
/// video is paused, if the student wants only watched time to count), which also makes it easy to test.
/// </summary>
public sealed class FocusTimer
{
    public const int SessionsPerRound = 4;

    public static readonly IReadOnlyList<FocusPreset> Presets = [new(25, 5), new(50, 10), new(90, 15), new(40, 8)];

    private readonly Func<DateOnly> _today;
    private DateOnly _day;
    private TimeSpan _remaining;

    public FocusTimer(Func<DateOnly>? today = null)
    {
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _day = _today();
        _remaining = PartLength;
    }

    // ---- state -----------------------------------------------------------------------------------------------

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;
    public bool Running { get; private set; }
    public int PresetIndex { get; private set; }
    public FocusPreset Preset => Presets[PresetIndex];

    /// <summary>Focus sessions finished in the current round of four.</summary>
    public int CompletedSessions { get; private set; }

    public TimeSpan Remaining => _remaining;
    public TimeSpan PartLength => TimeSpan.FromMinutes(Phase == FocusPhase.Focus ? Preset.FocusMinutes : Preset.BreakMinutes);

    /// <summary>How much of the current part is left, 1 down to 0 (the ring's length).</summary>
    public double FractionLeft => Math.Clamp(_remaining / PartLength, 0, 1);

    /// <summary>Whole seconds left, rounded up, so the display reaches 00:00 exactly when the part ends.</summary>
    public int SecondsLeft => (int)Math.Ceiling(_remaining.TotalSeconds - 1e-9);

    /// <summary>Started and not back at the full length: worth showing in the corner of the video.</summary>
    public bool IsActive => Running || _remaining < PartLength;

    public string SessionText => Phase == FocusPhase.Focus
        ? $"Session {Math.Min(SessionsPerRound, CompletedSessions + 1)} of {SessionsPerRound}"
        : $"Break after session {CompletedSessions} of {SessionsPerRound}";

    // today
    public TimeSpan FocusedToday { get; private set; }
    public int SessionsToday { get; private set; }

    // options
    public bool PauseVideoOnBreaks { get; set; } = true;
    public bool ChimeWhenPartEnds { get; set; } = true;
    public bool CountOnlyWhilePlaying { get; set; }

    /// <summary>Raised when a part runs out (with the phase that just ended); the next part has already begun, still running.</summary>
    public event EventHandler<FocusPhase>? PartEnded;

    /// <summary>Raised after every change, ticks included.</summary>
    public event EventHandler? Changed;

    // ---- doing things ----------------------------------------------------------------------------------------

    public void Start() => SetRunning(true);
    public void Pause() => SetRunning(false);
    public void Toggle() => SetRunning(!Running);

    private void SetRunning(bool running)
    {
        if (Running == running) return;
        Running = running;
        Raise();
    }

    /// <summary>Back to the full length of the current part, stopped.</summary>
    public void Reset()
    {
        Running = false;
        _remaining = PartLength;
        Raise();
    }

    /// <summary>Jumps to the next part (focus → break → focus) without counting the one left behind as a finished session.</summary>
    public void Skip()
    {
        NextPart(finished: false);
        Raise();
    }

    /// <summary>Picks a length; starts a fresh, stopped focus session with it.</summary>
    public void SelectPreset(int index)
    {
        PresetIndex = Math.Clamp(index, 0, Presets.Count - 1);
        Phase = FocusPhase.Focus;
        Running = false;
        _remaining = PartLength;
        Raise();
    }

    /// <summary>Time has passed. Counts down while running; credits focus time to today; moves on when a part ends.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (!Running || elapsed <= TimeSpan.Zero) return;
        RollDay();

        var used = elapsed < _remaining ? elapsed : _remaining;
        if (Phase == FocusPhase.Focus) FocusedToday += used;
        _remaining -= elapsed;

        if (_remaining <= TimeSpan.Zero)
        {
            var ended = Phase;
            NextPart(finished: true);
            PartEnded?.Invoke(this, ended);
        }
        Raise();
    }

    private void NextPart(bool finished)
    {
        if (Phase == FocusPhase.Focus)
        {
            if (finished)
            {
                CompletedSessions = Math.Min(SessionsPerRound, CompletedSessions + 1);
                RollDay();
                SessionsToday++;
            }
            Phase = FocusPhase.Break;
        }
        else
        {
            if (CompletedSessions >= SessionsPerRound) CompletedSessions = 0; // a round is done: start the next one
            Phase = FocusPhase.Focus;
        }
        _remaining = PartLength;
    }

    private void RollDay()
    {
        var today = _today();
        if (today == _day) return;
        _day = today;
        FocusedToday = TimeSpan.Zero;
        SessionsToday = 0;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    // ---- saving ----------------------------------------------------------------------------------------------

    private sealed record Saved(int Preset, bool PauseVideo, bool Chime, bool OnlyWhilePlaying, string? Day, double Seconds, int Sessions);

    /// <summary>The length chosen, the options, and today's totals. (Whether it is running is not kept.)</summary>
    public string ToJson() => JsonSerializer.Serialize(new Saved(PresetIndex, PauseVideoOnBreaks, ChimeWhenPartEnds,
        CountOnlyWhilePlaying, _day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), FocusedToday.TotalSeconds, SessionsToday));

    /// <summary>Restores what <see cref="ToJson"/> saved. Anything unreadable is ignored; totals from another day are dropped.</summary>
    public void LoadJson(string? json)
    {
        Saved? saved = null;
        try { if (!string.IsNullOrWhiteSpace(json)) saved = JsonSerializer.Deserialize<Saved>(json); }
        catch (JsonException) { }

        Running = false;
        Phase = FocusPhase.Focus;
        CompletedSessions = 0;
        PresetIndex = 0;
        PauseVideoOnBreaks = true;
        ChimeWhenPartEnds = true;
        CountOnlyWhilePlaying = false;
        _day = _today();
        FocusedToday = TimeSpan.Zero;
        SessionsToday = 0;

        if (saved is not null)
        {
            PresetIndex = Math.Clamp(saved.Preset, 0, Presets.Count - 1);
            PauseVideoOnBreaks = saved.PauseVideo;
            ChimeWhenPartEnds = saved.Chime;
            CountOnlyWhilePlaying = saved.OnlyWhilePlaying;
            if (DateOnly.TryParseExact(saved.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day == _day
                && double.IsFinite(saved.Seconds) && saved.Seconds >= 0 && saved.Sessions >= 0)
            {
                FocusedToday = TimeSpan.FromSeconds(saved.Seconds);
                SessionsToday = saved.Sessions;
            }
        }

        _remaining = PartLength;
        Raise();
    }
}
