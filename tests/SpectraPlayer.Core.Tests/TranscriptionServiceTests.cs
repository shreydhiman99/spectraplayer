using System.Runtime.CompilerServices;
using SpectraPlayer.Core.Library;
using SpectraPlayer.Core.Transcription;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class TranscriptionServiceTests : IDisposable
{
    private const string Video = @"D:\Course\lecture.mp4";
    private static readonly ModelInfo Model = WhisperModels.Base;

    private readonly LibraryStore _store = new(":memory:");
    private readonly FakeExtractor _extractor = new();

    public void Dispose() => _store.Dispose();

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static TimeRange R(double a, double b) => new(S(a), S(b));

    // Most tests use no padding so the numbers stay simple; the padding behaviour has its own tests below.
    private TranscriptionService Service(FakeTranscriber transcriber) => new(_extractor, transcriber, _store, TimeSpan.Zero);

    private TranscriptionService PaddedService(FakeTranscriber transcriber) => new(_extractor, transcriber, _store);

    private static Task<IReadOnlyList<TranscriptSegment>> Run(TranscriptionService svc, TimeRange section, string? model = null,
        IProgress<TranscriptionProgress>? progress = null, Action<TranscriptSegment>? onSegment = null, CancellationToken ct = default) =>
        svc.TranscribeAsync(Video, section, model is null ? Model : Model with { Id = model }, "model.bin", progress, onSegment, ct);

    [Fact]
    public async Task Fresh_section_is_extracted_transcribed_offset_to_video_time_and_stored()
    {
        // Recognition times are relative to the audio given to it: 0–4 s and 4–9 s of a section that starts at 100 s.
        var svc = Service(new FakeTranscriber(_ => Seq((0, 4, "Hello."), (4, 9, "World."))));
        var live = new List<TranscriptSegment>();

        var result = await Run(svc, R(100, 160), onSegment: live.Add);

        Assert.Equal([(S(100), S(160))], _extractor.Calls);
        Assert.Equal([(S(100), S(104), "Hello."), (S(104), S(109), "World.")], result.Select(s => (s.Start, s.End, s.Text)));
        Assert.Equal(["Hello.", "World."], live.Select(s => s.Text));
        Assert.Equal([R(100, 160)], _store.GetTranscribedRanges(Video, Model.Id));
        Assert.All(result, s => Assert.True(s.Id > 0)); // returned from the library, not the live copies
    }

    [Fact]
    public async Task Asking_again_uses_the_cache_and_replays_the_text()
    {
        var transcriber = new FakeTranscriber(_ => Seq((0, 5, "Once.")));
        var svc = Service(transcriber);
        await Run(svc, R(0, 60));
        _extractor.Calls.Clear();

        var replay = new List<string>();
        var second = await Run(svc, R(0, 60), onSegment: s => replay.Add(s.Text));

        Assert.Empty(_extractor.Calls);
        Assert.Equal(1, transcriber.Runs);
        Assert.Equal(["Once."], replay);
        Assert.Equal(["Once."], second.Select(s => s.Text));
    }

    [Fact]
    public async Task A_wider_section_transcribes_only_the_missing_parts()
    {
        var svc = Service(new FakeTranscriber(_ => Seq((0, 1.5, "x"))));
        await Run(svc, R(60, 120));
        _extractor.Calls.Clear();

        await Run(svc, R(30, 180));

        Assert.Equal([(S(30), S(60)), (S(120), S(180))], _extractor.Calls);
        Assert.Equal([R(30, 180)], _store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task Coverage_extends_to_the_end_of_a_sentence_that_runs_past_the_range()
    {
        // The last segment ends at 85 s although the range ends at 70 s: that sentence is stored with this run,
        // so the next run must start after it rather than repeat it.
        var svc = Service(new FakeTranscriber(_ => Seq((0, 30, "a"), (30, 75, "runs on"))));

        await Run(svc, R(10, 70));

        Assert.Equal([R(10, 85)], _store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task A_gap_that_starts_where_a_stored_sentence_ended_gets_no_context_on_that_side()
    {
        // the last sentence runs to 101 s, so coverage extends to 101 and the next gap starts exactly at a sentence end
        var svc = PaddedService(new FakeTranscriber(_ => Seq((0, 50, "a"), (50, 101, "b"))));
        await Run(svc, R(0, 100));
        _extractor.Calls.Clear();

        await Run(svc, R(0, 200));

        // gap 101-200: no lead-in (it would repeat the end of "b"), but still context after its end
        Assert.Equal([(S(101), S(202))], _extractor.Calls);
    }

    [Fact]
    public async Task A_gap_that_starts_at_an_arbitrary_cut_gets_context_so_the_words_there_are_not_lost()
    {
        // the last sentence ended at 98 s but the range ended at 100 s: the cut falls inside the next sentence
        var svc = PaddedService(new FakeTranscriber(_ => Seq((0, 50, "a"), (50, 98, "b"))));
        await Run(svc, R(0, 100));
        _extractor.Calls.Clear();

        await Run(svc, R(0, 200));

        Assert.Equal([(S(98), S(202))], _extractor.Calls); // starts 2 s before the gap at 100 s
    }

    [Fact]
    public async Task A_stretch_with_no_speech_is_remembered_so_it_is_not_redone()
    {
        var transcriber = new FakeTranscriber(_ => Seq());
        var svc = Service(transcriber);

        var first = await Run(svc, R(0, 30));
        var second = await Run(svc, R(0, 30));

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(1, transcriber.Runs);
    }

    [Fact]
    public async Task Different_models_keep_separate_transcripts()
    {
        var transcriber = new FakeTranscriber(_ => Seq((0, 3, "text")));
        var svc = Service(transcriber);

        await Run(svc, R(0, 30), model: "base");
        await Run(svc, R(0, 30), model: "small");

        Assert.Equal(2, transcriber.Runs);
    }

    [Fact]
    public async Task Cancelling_keeps_what_was_recognised_and_the_next_run_carries_on_from_there()
    {
        using var cts = new CancellationTokenSource();
        var firstRun = new FakeTranscriber(ct => SeqThenWait(ct, (0, 10, "one"), (10, 20, "two")));
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run(Service(firstRun), R(0, 100), onSegment: _ => { if (++seen == 2) cts.Cancel(); }, ct: cts.Token));

        // the two recognised segments survived, and the library knows the first 20 s are done
        Assert.Equal(["one", "two"], _store.GetTranscript(Video, Model.Id, S(0), S(100)).Select(s => s.Text));
        Assert.Equal([R(0, 20)], _store.GetTranscribedRanges(Video, Model.Id));

        _extractor.Calls.Clear();
        var resumed = await Run(Service(new FakeTranscriber(_ => Seq((0, 30, "three")))), R(0, 100));

        Assert.Equal([(S(20), S(100))], _extractor.Calls); // only the rest
        Assert.Equal(["one", "two", "three"], resumed.Select(s => s.Text));
        Assert.Equal(S(20), resumed[2].Start);
        Assert.Equal([R(0, 100)], _store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task Cancelled_before_any_text_stores_nothing()
    {
        using var cts = new CancellationTokenSource();
        var transcriber = new FakeTranscriber(ct => SeqThenWait(ct)); // never yields
        var task = Run(Service(transcriber), R(0, 100), ct: cts.Token);
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Empty(_store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task Extraction_failure_propagates_and_records_nothing()
    {
        _extractor.Failure = new InvalidOperationException("This video has no audio track to transcribe.");
        var svc = Service(new FakeTranscriber(_ => Seq((0, 1, "never"))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(svc, R(0, 60)));

        Assert.Contains("no audio", ex.Message);
        Assert.Empty(_store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task Recognition_error_keeps_the_text_found_before_it()
    {
        var svc = Service(new FakeTranscriber(_ => SeqThenFail((0, 5, "kept"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(svc, R(0, 60)));

        Assert.Equal(["kept"], _store.GetTranscript(Video, Model.Id, S(0), S(60)).Select(s => s.Text));
        Assert.Equal([R(0, 5)], _store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task A_segment_belongs_to_the_range_if_its_centre_is_inside_it_and_the_temp_audio_is_deleted()
    {
        // section 10-70: "straddles" runs 40-85 (centre 62.5, inside, keeps its real end); "outside" runs 70-80 (centre 75, not ours)
        var svc = Service(new FakeTranscriber(_ => Seq((0, 30, "a"), (30, 75, "straddles"), (60, 70, "outside"))));

        var result = await Run(svc, R(10, 70));

        Assert.Equal(["a", "straddles"], result.Select(s => s.Text));
        Assert.Equal(S(85), result[^1].End);
        Assert.False(File.Exists(_extractor.LastWav));
    }

    [Fact]
    public async Task The_recogniser_gets_context_audio_on_both_sides_and_never_before_the_start_of_the_video()
    {
        var svc = PaddedService(new FakeTranscriber(_ => Seq()));

        await Run(svc, R(100, 160));
        await Run(svc, R(0, 30));

        Assert.Equal([(S(98), S(162)), (S(0), S(32))], _extractor.Calls);
        // the stored coverage is the requested range, not the padded one
        Assert.Equal([R(0, 30), R(100, 160)], _store.GetTranscribedRanges(Video, Model.Id));
    }

    [Fact]
    public async Task Padding_only_gives_context_and_text_is_placed_at_real_video_times()
    {
        // audio starts at 98 s. "before" is centred at 99.5 s and "after" at 162.5 s: both outside 100-160 and dropped.
        var svc = PaddedService(new FakeTranscriber(_ => Seq(
            (0, 3, "before"), (1, 6, "edge-in"), (60, 63, "tail"), (63, 66, "after"))));

        var result = await Run(svc, R(100, 160));

        Assert.Equal(["edge-in", "tail"], result.Select(s => s.Text));
        Assert.Equal([S(99), S(158)], result.Select(s => s.Start));
    }

    [Fact]
    public async Task Progress_ends_at_one_and_never_goes_backwards()
    {
        var reports = new List<TranscriptionProgress>();
        var svc = Service(new FakeTranscriber(_ => Seq((0, 20, "a"), (20, 40, "b"), (40, 60, "c"))));

        await Run(svc, R(0, 60), progress: new SyncProgress(reports.Add));

        Assert.Equal("Done", reports[^1].Stage);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(reports.Select(r => r.Fraction).OrderBy(x => x), reports.Select(r => r.Fraction));
        Assert.Contains(reports, r => r.Stage == "Transcribing" && r.Fraction is > 0 and < 1);
    }

    [Fact]
    public async Task Empty_section_is_rejected()
    {
        var svc = Service(new FakeTranscriber(_ => Seq()));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(svc, R(10, 10)));
    }

    // ---- fakes -----------------------------------------------------------------------------------------------

    private static async IAsyncEnumerable<RecognizedSegment> Seq(params (double Start, double End, string Text)[] items)
    {
        foreach (var (s, e, t) in items) yield return new RecognizedSegment(S(s), S(e), t);
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<RecognizedSegment> SeqThenWait(
        [EnumeratorCancellation] CancellationToken ct, params (double Start, double End, string Text)[] items)
    {
        foreach (var (s, e, t) in items) yield return new RecognizedSegment(S(s), S(e), t);
        await Task.Delay(Timeout.Infinite, ct); // like a long recognition run that is interrupted
    }

    private static async IAsyncEnumerable<RecognizedSegment> SeqThenFail(params (double Start, double End, string Text)[] items)
    {
        foreach (var (s, e, t) in items) yield return new RecognizedSegment(S(s), S(e), t);
        await Task.Yield();
        throw new InvalidOperationException("recogniser crashed");
    }

    private sealed class SyncProgress(Action<TranscriptionProgress> handler) : IProgress<TranscriptionProgress>
    {
        public void Report(TranscriptionProgress value) => handler(value);
    }

    private sealed class FakeExtractor : IAudioExtractor
    {
        public List<(TimeSpan Start, TimeSpan End)> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public string? LastWav { get; private set; }

        public Task ExtractAsync(string mediaPath, TimeSpan start, TimeSpan end, string outputWavPath, CancellationToken ct = default)
        {
            Calls.Add((start, end));
            if (Failure is not null) throw Failure;
            File.WriteAllBytes(outputWavPath, new byte[64]);
            LastWav = outputWavPath;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTranscriber(Func<CancellationToken, IAsyncEnumerable<RecognizedSegment>> script) : ITranscriber
    {
        public int Runs { get; private set; }

        public IAsyncEnumerable<RecognizedSegment> TranscribeAsync(string wavPath, string modelPath, CancellationToken ct = default)
        {
            Runs++;
            return script(ct);
        }
    }
}
