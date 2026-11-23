using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SpectraPlayer.Core.Transcription;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class ModelManagerTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("spectra-models");
    private readonly byte[] _data = new byte[1024 * 1024 + 123]; // bigger than the download buffer, so progress arrives in several steps

    public ModelManagerTests() => new Random(42).NextBytes(_data);

    public void Dispose() => _dir.Delete(true);

    private string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private ModelInfo Model(string? sha = "auto") =>
        new("test", "Test", "", "ggml-test.bin", _data.Length, sha == "auto" ? Sha(_data) : sha, new Uri("http://test.invalid/ggml-test.bin"));

    private ModelManager Manager(FakeServer server) => new(_dir.FullName, new HttpClient(server));

    [Fact]
    public async Task Downloads_verifies_and_reports_progress_up_to_one()
    {
        var server = new FakeServer(_data);
        var reports = new List<double>();
        var manager = Manager(server);

        await manager.DownloadAsync(Model(), new SyncProgress<DownloadProgress>(p => reports.Add(p.Fraction)));

        Assert.True(manager.IsInstalled(Model()));
        Assert.Equal(_data, File.ReadAllBytes(manager.PathFor(Model())));
        Assert.False(File.Exists(manager.PathFor(Model()) + ".part"));
        Assert.Equal(1.0, reports[^1], 6);
        Assert.True(reports.Count > 3);
        Assert.Equal(reports.OrderBy(x => x), reports); // never goes backwards
    }

    [Fact]
    public async Task Already_installed_model_is_not_downloaded_again()
    {
        var server = new FakeServer(_data);
        var manager = Manager(server);
        await manager.DownloadAsync(Model());
        server.Requests.Clear();

        await manager.DownloadAsync(Model());

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Resumes_a_partial_download_with_a_range_request()
    {
        var manager = Manager(new FakeServer(_data) { });
        var half = _data.Length / 2;
        Directory.CreateDirectory(_dir.FullName);
        File.WriteAllBytes(Path.Combine(_dir.FullName, "ggml-test.bin.part"), _data[..half]);
        var server = new FakeServer(_data);
        manager = Manager(server);

        await manager.DownloadAsync(Model());

        Assert.Equal($"bytes={half}-", server.Requests.Single().Range);
        Assert.Equal(_data, File.ReadAllBytes(manager.PathFor(Model())));
    }

    [Fact]
    public async Task Starts_over_when_the_server_ignores_the_range_request()
    {
        File.WriteAllBytes(Path.Combine(_dir.FullName, "ggml-test.bin.part"), _data[..1000]);
        var server = new FakeServer(_data) { IgnoreRange = true };
        var manager = Manager(server);

        await manager.DownloadAsync(Model());

        Assert.Equal(_data, File.ReadAllBytes(manager.PathFor(Model())));
    }

    [Fact]
    public async Task Wrong_checksum_discards_the_file_and_fails()
    {
        var manager = Manager(new FakeServer(_data));

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(Model(sha: new string('0', 64))));

        Assert.False(File.Exists(manager.PathFor(Model())));
        Assert.False(File.Exists(manager.PathFor(Model()) + ".part")); // a corrupt partial must not be resumed
    }

    [Fact]
    public async Task Cancelling_keeps_the_partial_file_and_never_exposes_a_model()
    {
        using var cts = new CancellationTokenSource();
        var manager = Manager(new FakeServer(_data));
        var progress = new SyncProgress<DownloadProgress>(p =>
        {
            if (p.BytesDone > 300_000) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.DownloadAsync(Model(), progress, cts.Token));

        Assert.False(manager.IsInstalled(Model()));
        Assert.True(File.Exists(manager.PathFor(Model()) + ".part"));

        // ...and a later attempt carries on and completes.
        await manager.DownloadAsync(Model());
        Assert.Equal(_data, File.ReadAllBytes(manager.PathFor(Model())));
    }

    [Fact]
    public async Task Server_error_surfaces_as_an_exception()
    {
        var manager = Manager(new FakeServer(_data) { Status = HttpStatusCode.NotFound });
        await Assert.ThrowsAsync<HttpRequestException>(() => manager.DownloadAsync(Model()));
        Assert.False(manager.IsInstalled(Model()));
    }

    [Fact]
    public void Installed_lists_user_supplied_models_and_ignores_incomplete_known_ones()
    {
        File.WriteAllBytes(Path.Combine(_dir.FullName, "ggml-mymodel.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_dir.FullName, WhisperModels.Base.FileName), new byte[10]); // wrong size: not installed
        File.WriteAllText(Path.Combine(_dir.FullName, "notes.txt"), "not a model");

        var installed = new ModelManager(_dir.FullName).Installed();

        var custom = Assert.Single(installed);
        Assert.Equal("mymodel", custom.Id);
        Assert.True(custom.IsCustom);
    }

    [Fact]
    public void Known_models_have_published_sizes_and_checksums()
    {
        Assert.Equal(59_721_011, WhisperModels.Base.SizeBytes);
        Assert.Equal(190_098_681, WhisperModels.Small.SizeBytes);
        Assert.All(WhisperModels.All, m =>
        {
            Assert.Equal(64, m.Sha256!.Length);
            Assert.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/", m.Url!.ToString());
        });
        Assert.Same(WhisperModels.Base, WhisperModels.Default);
    }

    /// <summary>Reports on the calling thread so tests can react immediately (the normal Progress&lt;T&gt; posts asynchronously).</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class FakeServer(byte[] data) : HttpMessageHandler
    {
        public List<(string? Range, Uri? Url)> Requests { get; } = [];
        public bool IgnoreRange { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Headers.Range?.ToString(), request.RequestUri));
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));

            var from = IgnoreRange ? null : request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            var response = new HttpResponseMessage(from is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(new MemoryStream(data, (int)(from ?? 0), data.Length - (int)(from ?? 0))),
            };
            if (from is { } f)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(f, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }
}
