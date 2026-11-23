using System.Security.Cryptography;

namespace SpectraPlayer.Core.Transcription;

/// <param name="Id">Short stable name, stored with every transcript so results from different models don't mix.</param>
/// <param name="Sha256">Expected checksum (lower-case hex); null for a model file the user supplied themselves.</param>
/// <param name="Url">Where to download it; null for a user-supplied file.</param>
public sealed record ModelInfo(string Id, string DisplayName, string Description, string FileName,
    long SizeBytes, string? Sha256, Uri? Url)
{
    public bool IsCustom => Url is null;
    public string SizeText => $"{SizeBytes / 1024.0 / 1024.0:0} MB";
}

/// <summary>The English Whisper models Spectraplayer knows how to fetch. Checksums and sizes are Hugging Face's published ones.</summary>
public static class WhisperModels
{
    private const string DefaultSource = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>
    /// Where model files are fetched from. Set the SPECTRAPLAYER_MODEL_SOURCE environment variable to a folder-style URL
    /// to use your own mirror (a school server, say). The checksums below still apply, so a mirror cannot alter a model.
    /// </summary>
    private static readonly string Source = ResolveSource();

    private static string ResolveSource()
    {
        var custom = Environment.GetEnvironmentVariable("SPECTRAPLAYER_MODEL_SOURCE");
        if (string.IsNullOrWhiteSpace(custom)) return DefaultSource;
        return custom.EndsWith('/') ? custom : custom + "/";
    }

    public static readonly ModelInfo Base = new(
        "base.en-q5_1", "Standard (base, English)", "Fast: about four times quicker than playing the video on a typical laptop.",
        "ggml-base.en-q5_1.bin", 59_721_011, "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f",
        new Uri(Source + "ggml-base.en-q5_1.bin"));

    public static readonly ModelInfo Small = new(
        "small.en-q5_1", "Better accuracy (small, English)", "More accurate on difficult audio, but takes about as long as the video itself.",
        "ggml-small.en-q5_1.bin", 190_098_681, "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30",
        new Uri(Source + "ggml-small.en-q5_1.bin"));

    public static readonly IReadOnlyList<ModelInfo> All = [Base, Small];

    /// <summary>The model used when the user hasn't chosen one.</summary>
    public static ModelInfo Default => Base;
}

public sealed record DownloadProgress(long BytesDone, long BytesTotal)
{
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;
}

/// <summary>
/// Keeps Whisper model files in one folder: lists what is installed (including .bin files the user dropped in) and
/// downloads the known ones with resume, free-space check and checksum verification. Never downloads by itself:
/// the caller must ask the user first.
/// </summary>
public sealed class ModelManager
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public ModelManager(string modelsDirectory, HttpClient? http = null)
    {
        ModelsDirectory = modelsDirectory;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // downloads are long; cancellation is explicit
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectraplayer");
    }

    public string ModelsDirectory { get; }

    public string PathFor(ModelInfo model) => Path.Combine(ModelsDirectory, model.FileName);

    /// <summary>Present and complete (a half-downloaded file is not installed).</summary>
    public bool IsInstalled(ModelInfo model) =>
        File.Exists(PathFor(model)) && (model.IsCustom || new FileInfo(PathFor(model)).Length == model.SizeBytes);

    /// <summary>Known models that are installed, plus any other ggml-*.bin file in the folder, as custom models.</summary>
    public IReadOnlyList<ModelInfo> Installed()
    {
        var result = WhisperModels.All.Where(IsInstalled).ToList();
        if (!Directory.Exists(ModelsDirectory)) return result;

        var known = WhisperModels.All.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(ModelsDirectory, "ggml-*.bin").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (known.Contains(name)) continue;
            var id = Path.GetFileNameWithoutExtension(name)["ggml-".Length..];
            result.Add(new ModelInfo(id, id, "Model file you added yourself.", name, new FileInfo(file).Length, null, null));
        }
        return result;
    }

    /// <summary>
    /// Downloads a model into the models folder. Resumes a partial file from an earlier attempt, checks the checksum, and
    /// only then makes the file visible. Safe to cancel at any time; the partial file is kept for next time.
    /// </summary>
    public async Task DownloadAsync(ModelInfo model, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (model.Url is null) throw new InvalidOperationException("This model file has no download address.");
        if (IsInstalled(model)) return;

        await _downloadLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(ModelsDirectory);
            var final = PathFor(model);
            var part = final + ".part";

            var have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > model.SizeBytes) { File.Delete(part); have = 0; }
            EnsureFreeSpace(model.SizeBytes - have);

            using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
            if (have > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (have > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                have = 0; // the server ignored the range and is sending everything again
            response.EnsureSuccessStatusCode();

            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                var done = have;
                progress?.Report(new DownloadProgress(done, model.SizeBytes));
                int read;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(new DownloadProgress(done, model.SizeBytes));
                }
            }

            if (new FileInfo(part).Length != model.SizeBytes)
                throw new IOException("The download ended early. Try again to continue from where it stopped.");

            if (model.Sha256 is not null)
            {
                var actual = await ComputeSha256Async(part, ct);
                if (!string.Equals(actual, model.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part); // a corrupt file must not be resumed
                    throw new InvalidDataException("The downloaded model failed its checksum and was discarded. Try again.");
                }
            }

            File.Move(part, final, overwrite: true);
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private void EnsureFreeSpace(long neededBytes)
    {
        if (neededBytes <= 0) return;
        var root = Path.GetPathRoot(Path.GetFullPath(ModelsDirectory));
        if (string.IsNullOrEmpty(root)) return;
        long free;
        try { free = new DriveInfo(root).AvailableFreeSpace; }
        catch (ArgumentException) { return; } // network paths and similar: let the download itself report any problem

        if (free < neededBytes + 50L * 1024 * 1024)
            throw new IOException($"Not enough free disk space for the model ({neededBytes / 1024 / 1024} MB needed).");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
}
