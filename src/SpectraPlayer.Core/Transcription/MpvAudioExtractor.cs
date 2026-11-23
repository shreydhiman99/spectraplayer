using System.Globalization;
using SpectraPlayer.Core.Playback.Mpv;
using static SpectraPlayer.Core.Playback.Mpv.MpvNative;

namespace SpectraPlayer.Core.Transcription;

/// <summary>Cuts a stretch of audio out of any media file as 16 kHz mono 16-bit WAV, the format speech recognition wants.</summary>
public interface IAudioExtractor
{
    Task ExtractAsync(string mediaPath, TimeSpan start, TimeSpan end, string outputWavPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses a second, headless libmpv instance that "plays" the section into a WAV file (the pcm audio output), so no extra
/// program or download is needed and every format the player can play can be transcribed.
/// </summary>
public sealed class MpvAudioExtractor : IAudioExtractor
{
    public Task ExtractAsync(string mediaPath, TimeSpan start, TimeSpan end, string outputWavPath,
        CancellationToken cancellationToken = default)
    {
        if (end <= start) throw new ArgumentException("The section must end after it starts.", nameof(end));
        return Task.Run(() => Extract(mediaPath, start, end, outputWavPath, cancellationToken), cancellationToken);
    }

    private const int NothingToPlay = -16; // MPV_ERROR_NOTHING_TO_PLAY: the file has no audio (or video) stream

    private static void Extract(string mediaPath, TimeSpan start, TimeSpan end, string wavPath, CancellationToken ct)
    {
        // Seen once in testing: a normal run that finished with an empty output file, never reproduced in 30 repeats.
        // One automatic retry makes that invisible; a second empty result means the section really has no audio.
        for (var attempt = 1; attempt <= 2; attempt++)
            if (TryExtract(mediaPath, start, end, wavPath, ct)) return;

        throw new InvalidOperationException("There is no audio in the selected section.");
    }

    /// <returns>False if mpv finished without writing any audio.</returns>
    private static bool TryExtract(string mediaPath, TimeSpan start, TimeSpan end, string wavPath, CancellationToken ct)
    {
        var tmp = wavPath + ".part";
        File.Delete(tmp);

        IntPtr ctx;
        try
        {
            ctx = mpv_create();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new MpvNotFoundException("libmpv could not be loaded (needed to read the audio).", ex);
        }
        if (ctx == IntPtr.Zero) throw new MpvException("mpv_create failed", -1);

        var cancelled = false;
        try
        {
            void Opt(string name, string value, bool required = true)
            {
                var rc = mpv_set_option_string(ctx, name, value);
                if (rc < 0 && required) throw new MpvException($"option '{name}': {ErrorText(rc)}", rc);
            }

            // Headless: no window, no video, no sound card. Audio goes to a WAV file instead, resampled for Whisper.
            Opt("vo", "null");
            Opt("vid", "no");
            Opt("sid", "no");
            Opt("ao", "pcm");
            Opt("ao-pcm-file", tmp);
            Opt("ao-pcm-waveheader", "yes");
            Opt("audio-samplerate", "16000");
            Opt("audio-channels", "mono");
            Opt("audio-format", "s16");
            Opt("start", Seconds(start));
            Opt("end", Seconds(end));
            Opt("idle", "no", required: false);
            Opt("keep-open", "no", required: false);
            Opt("load-scripts", "no", required: false);
            Opt("ytdl", "no", required: false);
            Opt("osc", "no", required: false);
            Opt("terminal", "no", required: false);
            Opt("audio-display", "no", required: false);
            Opt("msg-level", "all=no", required: false);

            var rc2 = mpv_initialize(ctx);
            if (rc2 < 0) throw new MpvException($"mpv_initialize: {ErrorText(rc2)}", rc2);

            var rc3 = RunCommand(ctx, "loadfile", mediaPath, "replace");
            if (rc3 < 0) throw new MpvException($"loadfile: {ErrorText(rc3)}", rc3);

            while (true)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }

                var ptr = mpv_wait_event(ctx, 0.25);
                if (ptr == IntPtr.Zero) continue;
                var ev = System.Runtime.InteropServices.Marshal.PtrToStructure<MpvEvent>(ptr);

                if (ev.EventId == EventShutdown) break;
                if (ev.EventId != EventEndFile) continue;

                var end2 = System.Runtime.InteropServices.Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                if (end2.Reason == 4 && end2.Error == NothingToPlay)
                    throw new InvalidOperationException("This video has no audio track to transcribe.");
                if (end2.Reason == 4) throw new MpvException($"Could not read the audio: {ErrorText(end2.Error)}", end2.Error);
                break; // reached the end of the section (or the file)
            }
        }
        finally
        {
            mpv_terminate_destroy(ctx); // also finishes the WAV header
        }

        if (cancelled)
        {
            File.Delete(tmp);
            throw new OperationCanceledException(ct);
        }

        // A WAV header alone is 44 bytes: anything that small means nothing was written.
        if (!File.Exists(tmp) || new FileInfo(tmp).Length <= 44)
        {
            File.Delete(tmp);
            return false;
        }
        File.Move(tmp, wavPath, overwrite: true);
        return true;
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
