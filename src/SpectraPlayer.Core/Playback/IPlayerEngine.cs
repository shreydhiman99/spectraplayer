namespace SpectraPlayer.Core.Playback;

public enum SeekMode
{
    /// <summary>Land exactly on the requested time.</summary>
    Exact,
    /// <summary>Snap to the nearest keyframe: fast, used while scrubbing.</summary>
    Fast,
}

public sealed class EngineMessageEventArgs(IReadOnlyList<string> args) : EventArgs
{
    public IReadOnlyList<string> Args { get; } = args;
}

/// <summary>
/// The player backend. The UI only talks to this, so libmpv can be swapped (e.g. for LibVLC) without touching it.
/// Events are raised on a background thread; marshal to the UI thread before touching controls.
/// </summary>
public interface IPlayerEngine : IDisposable
{
    /// <summary>Starts the engine rendering into the given native window (HWND).</summary>
    void Initialize(IntPtr videoWindowHandle);

    bool HasMedia { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    bool IsPaused { get; }
    bool IsMuted { get; set; }
    /// <summary>0–130 (percent).</summary>
    double Volume { get; set; }
    double Speed { get; set; }

    event EventHandler? FileLoaded;
    event EventHandler? EndReached;
    /// <summary>Messages sent by input bindings registered with <see cref="BindKey"/>.</summary>
    event EventHandler<EngineMessageEventArgs>? MessageReceived;

    /// <summary>Loads a file, optionally starting at <paramref name="start"/> and optionally leaving it paused on that frame.</summary>
    void Load(string path, TimeSpan? start = null, bool paused = false);
    void Play();
    void Pause();
    void TogglePause();
    void Stop();
    void Seek(TimeSpan position, SeekMode mode = SeekMode.Exact);
    void SeekRelative(TimeSpan delta);
    /// <summary>Repeats the section between the two times, or turns the loop off when either is null.</summary>
    void SetSectionLoop(TimeSpan? start, TimeSpan? end);

    void FrameStep();
    void FrameBackStep();

    /// <summary>
    /// Routes an engine-side input (a key like "SPACE" or "ctrl+LEFT", or a mouse event like "MBTN_LEFT")
    /// to <see cref="MessageReceived"/> as the given space-separated words.
    /// </summary>
    void BindKey(string key, string message);

    /// <summary>Stops a key bound with <see cref="BindKey"/> from sending anything (the engine has no other bindings).</summary>
    void UnbindKey(string key);

    void ShowText(string text, TimeSpan? duration = null);
}
