using System.Runtime.CompilerServices;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace SpectraPlayer.Core.Transcription;

/// <summary>Turns a WAV file (16 kHz mono) into text segments. Abstracted so the pipeline can be tested without a model.</summary>
public interface ITranscriber
{
    /// <summary>Yields speech segments as they are recognised; times are relative to the start of the audio.</summary>
    IAsyncEnumerable<RecognizedSegment> TranscribeAsync(string wavPath, string modelPath, CancellationToken cancellationToken = default);
}

/// <summary>Whisper (whisper.cpp through Whisper.net) running on the CPU. The model is loaded for one run and released after it.</summary>
public sealed class WhisperTranscriber : ITranscriber
{
    static WhisperTranscriber()
    {
        // CPU only: it is the one runtime we ship, and pinning it avoids surprises on machines with odd GPU drivers.
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
    }

    public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(string wavPath, string modelPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var factory = WhisperFactory.FromPath(modelPath);
        await using var processor = factory.CreateBuilder().WithLanguage("en").Build();
        await using var wav = File.OpenRead(wavPath);

        await foreach (var segment in processor.ProcessAsync(wav, cancellationToken))
        {
            if (SegmentFilter.Clean(segment.Text) is { } text)
                yield return new RecognizedSegment(segment.Start, segment.End, text);
        }
    }
}
