using System.Globalization;
using System.Runtime.InteropServices;
using static SpectraPlayer.Core.Playback.Mpv.MpvNative;

namespace SpectraPlayer.Core.Playback.Mpv;

/// <summary>
/// <see cref="IPlayerEngine"/> backed by libmpv. mpv renders into a child window of the supplied HWND; a background
/// thread drains mpv's event queue and keeps cached copies of position/duration/pause/volume so the UI can poll cheaply.
/// </summary>
public sealed class MpvEngine : IPlayerEngine
{
    private const ulong UserdataBase = 1;

    private IntPtr _ctx;
    private Thread? _eventThread;
    private volatile bool _disposed;

    private double _position, _duration, _volume = 100, _speed = 1;
    private volatile bool _paused, _muted, _hasMedia;
    private double _pendingStart; // seconds; applied again right after the file loads (0 = none)

    public event EventHandler? FileLoaded;
    public event EventHandler? EndReached;
    public event EventHandler<EngineMessageEventArgs>? MessageReceived;

    public bool HasMedia => _hasMedia;
    public TimeSpan Position => TimeSpan.FromSeconds(Volatile.Read(ref _position));
    public TimeSpan Duration => TimeSpan.FromSeconds(Volatile.Read(ref _duration));
    public bool IsPaused => _paused;

    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; SetProperty("mute", value ? "yes" : "no"); }
    }

    public double Volume
    {
        get => Volatile.Read(ref _volume);
        set
        {
            var v = Math.Clamp(value, 0, 130);
            Volatile.Write(ref _volume, v);
            SetProperty("volume", v.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    public double Speed
    {
        get => Volatile.Read(ref _speed);
        set
        {
            var v = Math.Clamp(value, 0.25, 4);
            Volatile.Write(ref _speed, v);
            SetProperty("speed", v.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    public void Initialize(IntPtr videoWindowHandle)
    {
        if (_ctx != IntPtr.Zero) throw new InvalidOperationException("Already initialized.");

        try
        {
            _ctx = mpv_create();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new MpvNotFoundException(
                "libmpv could not be loaded. Place a 64-bit libmpv-2.dll next to the executable " +
                "(run tools\\Get-LibMpv.ps1).", ex);
        }
        if (_ctx == IntPtr.Zero) throw new MpvException("mpv_create failed", -1);

        // Render into our window, and handle all input ourselves: mpv's default bindings and OSC are off,
        // and the keys/mouse we care about are routed back to the app through BindKey (see below).
        SetOption("wid", videoWindowHandle.ToInt64().ToString(CultureInfo.InvariantCulture), required: true);
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "yes");
        SetOption("osc", "no");
        SetOption("load-scripts", "no");
        SetOption("ytdl", "no");
        SetOption("idle", "yes");
        SetOption("force-window", "yes");
        SetOption("keep-open", "yes");
        SetOption("hwdec", "auto-safe");
        SetOption("sub-auto", "fuzzy");
        SetOption("cursor-autohide", "2000");
        SetOption("volume-max", "130");

        Check(mpv_initialize(_ctx), "mpv_initialize");

        Observe("time-pos", FormatDouble);
        Observe("duration", FormatDouble);
        Observe("pause", FormatFlag);
        Observe("volume", FormatDouble);
        Observe("mute", FormatFlag);
        Observe("speed", FormatDouble);
        Observe("eof-reached", FormatFlag);

        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _eventThread.Start();
    }

    public void Load(string path, TimeSpan? start = null, bool paused = false)
    {
        Volatile.Write(ref _position, start?.TotalSeconds ?? 0);
        Volatile.Write(ref _duration, 0);
        _paused = paused;
        Volatile.Write(ref _pendingStart, start is { } p && p > TimeSpan.Zero ? p.TotalSeconds : 0);
        SetProperty("pause", paused ? "yes" : "no");

        if (start is { } s && s > TimeSpan.Zero)
            // loadfile <file> <flags> <index> <options>; index -1 = append position is ignored for "replace" (mpv 0.38+).
            Command("loadfile", path, "replace", "-1", "start=" + s.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        else
            Command("loadfile", path, "replace");
    }

    public void Play() => SetProperty("pause", "no");
    public void Pause() => SetProperty("pause", "yes");
    public void TogglePause() => Command("cycle", "pause");
    public void Stop() { Command("stop"); _hasMedia = false; Volatile.Write(ref _position, 0); Volatile.Write(ref _duration, 0); }

    public void Seek(TimeSpan position, SeekMode mode = SeekMode.Exact)
    {
        var seconds = Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        Command("seek", seconds, mode == SeekMode.Exact ? "absolute+exact" : "absolute+keyframes");
    }

    public void SeekRelative(TimeSpan delta) =>
        Command("seek", delta.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "relative");

    public void SetSectionLoop(TimeSpan? start, TimeSpan? end)
    {
        if (start is { } a && end is { } b && b > a)
        {
            SetProperty("ab-loop-a", a.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            SetProperty("ab-loop-b", b.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        else
        {
            SetProperty("ab-loop-a", "no");
            SetProperty("ab-loop-b", "no");
        }
    }

    public void FrameStep() => Command("frame-step");
    public void FrameBackStep() => Command("frame-back-step");

    public void BindKey(string key, string message) => Command("keybind", key, "script-message " + message);

    public void ShowText(string text, TimeSpan? duration = null) =>
        Command("show-text", text, ((int)(duration ?? TimeSpan.FromSeconds(1.5)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ctx == IntPtr.Zero) return;

        mpv_wakeup(_ctx);
        _eventThread?.Join(TimeSpan.FromSeconds(2));
        mpv_terminate_destroy(_ctx);
        _ctx = IntPtr.Zero;
    }

    // ---- event loop ---------------------------------------------------------------------------------------

    private void EventLoop()
    {
        try
        {
            while (!_disposed)
            {
                var ptr = mpv_wait_event(_ctx, 0.25);
                if (ptr == IntPtr.Zero) continue;
                var ev = Marshal.PtrToStructure<MpvEvent>(ptr);
                if (ev.EventId == EventShutdown) break;
                Dispatch(ev);
            }
        }
        catch (Exception) when (_disposed)
        {
            // Teardown race: the context disappeared while we were waiting. Nothing to report.
        }
    }

    private void Dispatch(MpvEvent ev)
    {
        switch (ev.EventId)
        {
            case EventFileLoaded:
                _hasMedia = true;
                // A file that loads paused at a start offset doesn't always report its time until it moves.
                // Seeking to the same spot explicitly makes mpv publish the position (and show that exact frame).
                var pending = Volatile.Read(ref _pendingStart);
                if (pending > 0)
                {
                    Volatile.Write(ref _pendingStart, 0);
                    Command("seek", pending.ToString("0.###", CultureInfo.InvariantCulture), "absolute+exact");
                }
                FileLoaded?.Invoke(this, EventArgs.Empty);
                break;
            case EventEndFile:
                _hasMedia = false;
                break;
            case EventPropertyChange when ev.Data != IntPtr.Zero:
                OnProperty(Marshal.PtrToStructure<MpvEventProperty>(ev.Data));
                break;
            case EventClientMessage when ev.Data != IntPtr.Zero:
                OnClientMessage(Marshal.PtrToStructure<MpvEventClientMessage>(ev.Data));
                break;
        }
    }

    private void OnProperty(MpvEventProperty p)
    {
        var name = Marshal.PtrToStringUTF8(p.Name);
        var available = p.Format != FormatNone && p.Data != IntPtr.Zero;

        double AsDouble() => available ? BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p.Data)) : 0;
        bool AsFlag() => available && Marshal.ReadInt32(p.Data) != 0;

        switch (name)
        {
            case "time-pos": Volatile.Write(ref _position, AsDouble()); break;
            case "duration": Volatile.Write(ref _duration, AsDouble()); break;
            case "pause": _paused = AsFlag(); break;
            case "mute": _muted = AsFlag(); break;
            case "volume": if (available) Volatile.Write(ref _volume, AsDouble()); break;
            case "speed": if (available) Volatile.Write(ref _speed, AsDouble()); break;
            case "eof-reached": if (AsFlag()) EndReached?.Invoke(this, EventArgs.Empty); break;
        }
    }

    private void OnClientMessage(MpvEventClientMessage msg)
    {
        var args = new string[Math.Max(0, msg.NumArgs)];
        for (var i = 0; i < args.Length; i++)
            args[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(msg.Args, i * IntPtr.Size)) ?? string.Empty;
        MessageReceived?.Invoke(this, new EngineMessageEventArgs(args));
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private void SetOption(string name, string value, bool required = false)
    {
        var rc = mpv_set_option_string(_ctx, name, value);
        if (required) Check(rc, $"option '{name}'");
        // Optional options are skipped silently: older/newer libmpv builds drop or rename some of them.
    }

    private void SetProperty(string name, string value)
    {
        if (_ctx != IntPtr.Zero && !_disposed) mpv_set_property_string(_ctx, name, value);
    }

    private void Observe(string name, int format) =>
        Check(mpv_observe_property(_ctx, UserdataBase, name, format), $"observe '{name}'");

    private int Command(params string[] args)
    {
        if (_ctx == IntPtr.Zero || _disposed) return -1;
        return RunCommand(_ctx, args);
    }

    private static void Check(int code, string what)
    {
        if (code >= 0) return;
        var text = Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? "unknown error";
        throw new MpvException($"{what}: {text} ({code})", code);
    }
}
