using SpectraPlayer.Core.Playback.Mpv;
using static SpectraPlayer.Core.Playback.Mpv.MpvNative;

namespace SpectraPlayer.Core.Library;

/// <summary>Finds out how long a media file is without playing it.</summary>
public interface IDurationProbe
{
    /// <returns>The length, or null if the file can't be read or has no length.</returns>
    TimeSpan? Probe(string mediaPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens the file in a second, headless libmpv instance (no window, no sound, paused) just long enough to read its
/// duration, so every format the player can play can be measured, with no extra program.
/// </summary>
public sealed class MpvDurationProbe : IDurationProbe
{
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(20);

    public TimeSpan? Probe(string mediaPath, CancellationToken cancellationToken = default)
    {
        IntPtr ctx;
        try
        {
            ctx = mpv_create();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new MpvNotFoundException("libmpv could not be loaded (needed to measure video lengths).", ex);
        }
        if (ctx == IntPtr.Zero) throw new MpvException("mpv_create failed", -1);

        try
        {
            void Opt(string name, string value)
            {
                var rc = mpv_set_option_string(ctx, name, value);
                if (rc < 0 && name is "vo" or "ao") throw new MpvException($"option '{name}': {ErrorText(rc)}", rc);
            }

            Opt("vo", "null");
            Opt("ao", "null");
            Opt("pause", "yes");          // nothing needs to be decoded to know the length
            Opt("audio-display", "no");
            Opt("idle", "no");
            Opt("keep-open", "no");
            Opt("load-scripts", "no");
            Opt("ytdl", "no");
            Opt("osc", "no");
            Opt("terminal", "no");
            Opt("msg-level", "all=no");

            var rc2 = mpv_initialize(ctx);
            if (rc2 < 0) throw new MpvException($"mpv_initialize: {ErrorText(rc2)}", rc2);

            var rc3 = RunCommand(ctx, "loadfile", mediaPath, "replace");
            if (rc3 < 0) return null;

            var started = Environment.TickCount64;
            while (Environment.TickCount64 - started < GiveUpAfter.TotalMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ptr = mpv_wait_event(ctx, 0.25);
                if (ptr == IntPtr.Zero) continue;
                var ev = System.Runtime.InteropServices.Marshal.PtrToStructure<MpvEvent>(ptr);

                if (ev.EventId == EventFileLoaded)
                {
                    if (mpv_get_property(ctx, "duration", FormatDouble, out var seconds) < 0) return null;
                    return double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
                }
                if (ev.EventId is EventEndFile or EventShutdown) return null; // could not be opened
            }
            return null;
        }
        finally
        {
            mpv_terminate_destroy(ctx);
        }
    }
}

/// <summary>
/// How long each video of a playlist is. Answers come from the library database when the file is unchanged (same size and
/// modification time); the rest are measured one after another in the background and remembered, so a folder is only
/// ever measured once.
/// </summary>
public sealed class DurationService(LibraryStore store, IDurationProbe probe)
{
    /// <summary>
    /// What is already known for these files. A value of zero means the file was tried and has no readable length (it is
    /// not tried again until it changes). Files that are missing from the result still need measuring.
    /// </summary>
    public IReadOnlyDictionary<string, TimeSpan> LoadKnown(string folder, IEnumerable<string> paths)
    {
        var cached = store.GetDurationsUnder(folder);
        var known = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!cached.TryGetValue(Path.GetFullPath(path), out var record)) continue;
            var info = new FileInfo(path);
            if (info.Exists && info.Length == record.Size && info.LastWriteTimeUtc.Ticks == record.ModifiedTicks)
                known[path] = record.Duration;
        }
        return known;
    }

    /// <summary>
    /// Measures the files in order, storing each answer and reporting it as soon as it is known (on the calling thread's
    /// worker, not the UI thread). Stops quietly when cancelled. Throws if libmpv itself is unavailable.
    /// </summary>
    public Task MeasureAsync(IEnumerable<string> paths, Action<string, TimeSpan> measured, CancellationToken cancellationToken = default)
    {
        var list = paths.ToList();
        return Task.Run(() =>
        {
            foreach (var path in list)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var info = new FileInfo(path);
                if (!info.Exists) continue;

                TimeSpan? length;
                try { length = probe.Probe(path, cancellationToken); }
                catch (OperationCanceledException) { return; }

                var duration = length ?? TimeSpan.Zero;
                store.SaveDuration(path, info.Length, info.LastWriteTimeUtc.Ticks, duration);
                measured(path, duration);
            }
        }, cancellationToken);
    }

    /// <summary>The player has just learned a file's real length by playing it: keep it, no need to measure.</summary>
    public void Remember(string path, TimeSpan duration)
    {
        var info = new FileInfo(path);
        if (info.Exists && duration > TimeSpan.Zero) store.SaveDuration(path, info.Length, info.LastWriteTimeUtc.Ticks, duration);
    }
}
