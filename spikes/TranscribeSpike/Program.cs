using System.Diagnostics;
using SpectraPlayer.Core.Transcription;
using Whisper.net;
using Whisper.net.LibraryLoader;

// usage: TranscribeSpike <media> <startSec> <endSec> [model.bin] [cpu|vulkan]
//   without a model it only tests audio extraction.

// usage: TranscribeSpike service <media> <model.bin>
//   end-to-end check of TranscriptionService with real Whisper: cache, only-the-gaps, cancel and resume.
if (args.Length == 3 && args[0] == "service") return await ServiceDemo(args[1], args[2]);

if (args.Length < 3)
{
    Console.WriteLine("usage: TranscribeSpike <media> <startSec> <endSec> [model.bin] [cpu|vulkan]");
    return 1;
}

var media = args[0];
var start = TimeSpan.FromSeconds(double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
var end = TimeSpan.FromSeconds(double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
var modelPath = args.Length > 3 ? args[3] : null;
var runtime = args.Length > 4 ? args[4] : "cpu";

var wav = Path.Combine(Path.GetTempPath(), "spectra-spike.wav");
var sw = Stopwatch.StartNew();
await new MpvAudioExtractor().ExtractAsync(media, start, end, wav);
var extractTime = sw.Elapsed;

var (channels, rate, bits, seconds) = ReadWavHeader(wav);
Console.WriteLine($"EXTRACT  {extractTime.TotalSeconds:0.00}s  -> {channels} ch, {rate} Hz, {bits} bit, {seconds:0.00}s of audio " +
                  $"(asked for {(end - start).TotalSeconds:0.00}s)  [{extractTime.TotalSeconds / seconds:0.000}x real time]");

if (modelPath is null) return 0;

RuntimeOptions.RuntimeLibraryOrder = runtime == "vulkan"
    ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]
    : [RuntimeLibrary.Cpu];

sw.Restart();
using var factory = WhisperFactory.FromPath(modelPath);
Console.WriteLine($"MODEL    loaded in {sw.Elapsed.TotalSeconds:0.00}s  ({Path.GetFileName(modelPath)}, runtime requested: {runtime})");

await using var processor = factory.CreateBuilder().WithLanguage("en").Build();
Console.WriteLine($"RUNTIME  actually loaded: {RuntimeOptions.LoadedLibrary}");

sw.Restart();
var firstSegment = TimeSpan.Zero;
var text = new System.Text.StringBuilder();
await using (var stream = File.OpenRead(wav))
{
    await foreach (var seg in processor.ProcessAsync(stream))
    {
        if (firstSegment == TimeSpan.Zero) firstSegment = sw.Elapsed;
        Console.WriteLine($"  [{seg.Start:mm\\:ss} -> {seg.End:mm\\:ss}] {seg.Text.Trim()}   (at {sw.Elapsed.TotalSeconds:0.0}s)");
        text.Append(seg.Text).Append(' ');
    }
}
var total = sw.Elapsed;
Console.WriteLine($"DONE     {total.TotalSeconds:0.0}s for {seconds:0.0}s of audio  =>  {total.TotalSeconds / seconds:0.00}x real time " +
                  $"(first text after {firstSegment.TotalSeconds:0.0}s)   peak memory {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");
return 0;

static async Task<int> ServiceDemo(string media, string modelPath)
{
    var dbFile = Path.Combine(Path.GetTempPath(), $"spectra-demo-{Guid.NewGuid():N}.db");
    using var store = new SpectraPlayer.Core.Library.LibraryStore(dbFile);
    var service = new TranscriptionService(new MpvAudioExtractor(), new WhisperTranscriber(), store);
    var model = WhisperModels.Base; // id only labels the cache; the file below is what runs

    TimeSpan T(double s) => TimeSpan.FromSeconds(s);
    string Ranges() => string.Join(", ", store.GetTranscribedRanges(media, model.Id)
        .Select(r => $"{r.Start.TotalSeconds:0}-{r.End.TotalSeconds:0}s"));

    async Task<int> Step(string title, double from, double to, CancellationTokenSource? cancelAfterTwo = null)
    {
        Console.WriteLine($"\n=== {title}: {from:0}-{to:0}s");
        var sw = Stopwatch.StartNew();
        var live = 0;
        var lastStage = "";
        var progress = new Progress<TranscriptionProgress>(p =>
        {
            if (p.Stage != lastStage) { lastStage = p.Stage; Console.WriteLine($"   [{sw.Elapsed.TotalSeconds,5:0.0}s] {p.Stage}"); }
        });
        try
        {
            var result = await service.TranscribeAsync(media, new TimeRange(T(from), T(to)), model, modelPath, progress,
                s =>
                {
                    live++;
                    if (s.Id == 0) Console.WriteLine($"   [{sw.Elapsed.TotalSeconds,5:0.0}s] new    {s.Start.TotalSeconds,6:0.0}-{s.End.TotalSeconds,6:0.0}  {s.Text}");
                    else Console.WriteLine($"   [{sw.Elapsed.TotalSeconds,5:0.0}s] cached {s.Start.TotalSeconds,6:0.0}-{s.End.TotalSeconds,6:0.0}  {s.Text}");
                    if (cancelAfterTwo is not null && live >= 2) cancelAfterTwo.Cancel();
                }, cancelAfterTwo?.Token ?? default);
            Console.WriteLine($"   -> finished in {sw.Elapsed.TotalSeconds:0.0}s, {result.Count} segments returned; covered now: {Ranges()}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"   -> CANCELLED after {sw.Elapsed.TotalSeconds:0.0}s; covered now: {Ranges()}; stored segments: " +
                              store.GetTranscript(media, model.Id, T(0), T(100000)).Count);
        }
        return 0;
    }

    await Step("1. fresh section", 20, 80);
    await Step("2. same section again (must be instant, all cached)", 20, 80);
    await Step("3. wider section (only 0-20 and 80-110 may be transcribed)", 0, 110);
    using (var cts = new CancellationTokenSource())
        await Step("4. new stretch, cancelled after two segments", 110, 224, cts);
    await Step("5. same stretch again (must carry on from where it stopped)", 110, 224);
    try { File.Delete(dbFile); } catch (IOException) { /* still open by the store; it is a temp file */ }
    return 0;
}

static (int Channels, int Rate, int Bits, double Seconds) ReadWavHeader(string path)
{
    using var f = File.OpenRead(path);
    var h = new byte[44];
    f.ReadExactly(h);
    var channels = BitConverter.ToUInt16(h, 22);
    var rate = (int)BitConverter.ToUInt32(h, 24);
    var bits = BitConverter.ToUInt16(h, 34);
    var dataBytes = f.Length - 44; // header chunk is 44 bytes for plain PCM
    return (channels, rate, bits, dataBytes / (double)(rate * channels * bits / 8));
}
