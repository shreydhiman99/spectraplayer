using SpectraPlayer.Core.Library;

namespace SpectraPlayer.Core.Transcription;

/// <param name="Stage">Short text for the UI, e.g. "Reading audio" or "Transcribing".</param>
/// <param name="Fraction">0–1 over the part of the section that actually has to be transcribed.</param>
public sealed record TranscriptionProgress(string Stage, double Fraction);

/// <summary>
/// Transcribes a section of a video: reuses what is already in the library, transcribes only the missing parts
/// (extract the audio, run speech recognition, store the result), and can be cancelled at any time without losing the
/// text recognised so far.
/// <para>Callbacks (<c>progress</c>, <c>onSegment</c>) are raised on background threads: marshal to the UI thread yourself.</para>
/// </summary>
public sealed class TranscriptionService(IAudioExtractor extractor, ITranscriber transcriber, LibraryStore store,
    TimeSpan? contextPadding = null)
{
    /// <summary>
    /// Extra audio given to the recogniser on each side of the requested range. A range that starts or ends mid-sentence
    /// otherwise makes it guess at the cut-off words (it even invents text). Only segments centred inside the range are kept.
    /// </summary>
    public static readonly TimeSpan DefaultContextPadding = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _padding = contextPadding ?? DefaultContextPadding;

    /// <returns>Every stored segment for the section, cached and new, in time order.</returns>
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string videoPath, TimeRange section, ModelInfo model, string modelPath,
        IProgress<TranscriptionProgress>? progress = null,
        Action<TranscriptSegment>? onSegment = null,
        CancellationToken cancellationToken = default)
    {
        if (section.IsEmpty) throw new ArgumentException("The section must end after it starts.", nameof(section));

        // What is already known comes first, so a UI can show it immediately.
        foreach (var cached in store.GetTranscript(videoPath, model.Id, section.Start, section.End))
            onSegment?.Invoke(cached);

        var covered = store.GetTranscribedRanges(videoPath, model.Id);
        var gaps = TimeRanges.Subtract(section, covered);
        var total = gaps.Sum(g => g.Length.TotalSeconds);
        var finished = 0.0;

        foreach (var gap in gaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TranscriptionProgress("Reading audio", total > 0 ? finished / total : 0));

            // If earlier text ended exactly where this gap begins (a stored sentence ends right there), that is a natural
            // place to start: adding context would only repeat the end of the old text. At an arbitrary cut the context
            // is needed, or the words around the cut would be lost.
            var startsAtCoveredEdge = covered.Any(c => c.End == gap.Start)
                                      && store.HasSegmentEndingAt(videoPath, model.Id, gap.Start);

            await TranscribeGapAsync(videoPath, gap, startsAtCoveredEdge, model, modelPath, finished, total, progress, onSegment,
                    cancellationToken)
                .ConfigureAwait(false);
            finished += gap.Length.TotalSeconds;
        }

        progress?.Report(new TranscriptionProgress("Done", 1));
        return store.GetTranscript(videoPath, model.Id, section.Start, section.End);
    }

    private async Task TranscribeGapAsync(string videoPath, TimeRange gap, bool startsAtCoveredEdge, ModelInfo model,
        string modelPath, double finishedSeconds, double totalSeconds, IProgress<TranscriptionProgress>? progress,
        Action<TranscriptSegment>? onSegment, CancellationToken ct)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"spectra-{Guid.NewGuid():N}.wav");
        var found = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        var lastEnd = gap.Start; // real video time where the last kept segment ended
        var completed = false;

        // The audio handed to the recogniser starts a little before the gap and ends a little after it.
        var leadIn = startsAtCoveredEdge ? TimeSpan.Zero : _padding;
        var audioStart = gap.Start - leadIn < TimeSpan.Zero ? TimeSpan.Zero : gap.Start - leadIn;
        var audioEnd = gap.End + _padding;

        try
        {
            await extractor.ExtractAsync(videoPath, audioStart, audioEnd, wav, ct).ConfigureAwait(false);

            await foreach (var seg in transcriber.TranscribeAsync(wav, modelPath, ct).ConfigureAwait(false))
            {
                // Recognition times are relative to the audio; the library stores real video times.
                var start = audioStart + seg.Start;
                var end = audioStart + seg.End;
                if (start >= end) continue;

                // Padding is only context. A segment belongs to the gap if its centre lies inside it; this also keeps
                // neighbouring gaps from both storing the same sentence.
                var centre = start + (end - start) / 2;
                if (centre < gap.Start || centre >= gap.End) continue;

                found.Add((start, end, seg.Text));
                lastEnd = end;
                onSegment?.Invoke(new TranscriptSegment(0, videoPath, model.Id, start, end, seg.Text));

                var sofar = lastEnd - gap.Start;
                var done = finishedSeconds + (sofar < gap.Length ? sofar : gap.Length).TotalSeconds;
                progress?.Report(new TranscriptionProgress("Transcribing", totalSeconds > 0 ? Math.Min(1, done / totalSeconds) : 0));
            }
            completed = true;
        }
        finally
        {
            TryDelete(wav);

            // Keep whatever was recognised, even after a cancel or an error, and remember how far we got so the next
            // request carries on from there instead of starting again. A sentence that runs past the end of the range
            // belongs to this run, so coverage extends to its end; the next run then starts after it.
            var coveredEnd = completed ? (lastEnd > gap.End ? lastEnd : gap.End) : lastEnd;
            if (coveredEnd > gap.Start)
                store.AddTranscribed(videoPath, model.Id, new TimeRange(gap.Start, coveredEnd), found);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* a temp file left behind is harmless */ }
    }
}
