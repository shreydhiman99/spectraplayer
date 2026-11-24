using SpectraPlayer.Core;
using SpectraPlayer.Core.Library;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class TimeFormatTotalTests
{
    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(42, "42 s")]
    [InlineData(59.4, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(89, "1 min")]          // 1.48 min rounds down
    [InlineData(90, "2 min")]          // and 1.5 rounds up
    [InlineData(42 * 60, "42 min")]
    [InlineData(59 * 60 + 40, "1 h")]  // rounds up into the next hour
    [InlineData(3600, "1 h")]
    [InlineData(3600 + 5 * 60, "1 h 5 min")]
    [InlineData(8 * 3600 + 42 * 60, "8 h 42 min")]
    [InlineData(104 * 3600, "104 h")]
    public void Lengths_read_naturally(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.FormatTotal(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_negative_length_is_shown_as_zero() =>
        Assert.Equal("0 s", TimeFormat.FormatTotal(TimeSpan.FromSeconds(-5)));

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(12, "00:00:12")]
    [InlineData(59.6, "00:01:00")]       // rounds to the nearest second
    [InlineData(3600 + 125, "01:02:05")]
    [InlineData(8 * 3600 + 42 * 60 + 15, "08:42:15")]
    [InlineData(100 * 3600, "100:00:00")]
    [InlineData(-5, "00:00:00")]
    public void The_clock_form_always_has_three_parts(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.FormatClock(TimeSpan.FromSeconds(seconds)));
}

public sealed class DurationServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "spectra-dur-" + Guid.NewGuid().ToString("N"));
    private readonly LibraryStore _store = new(":memory:");

    public DurationServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string File_(string name, string content = "x")
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FakeProbe(Dictionary<string, TimeSpan?> answers) : IDurationProbe
    {
        public List<string> Calls { get; } = [];

        public TimeSpan? Probe(string mediaPath, CancellationToken cancellationToken = default)
        {
            Calls.Add(Path.GetFileName(mediaPath));
            return answers.GetValueOrDefault(Path.GetFileName(mediaPath));
        }
    }

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public async Task Measures_each_file_once_and_reports_it()
    {
        var a = File_("a.mp4"); var b = File_("b.mp4");
        var probe = new FakeProbe(new() { ["a.mp4"] = Min(10), ["b.mp4"] = Min(25) });
        var service = new DurationService(_store, probe);

        var seen = new List<(string, TimeSpan)>();
        await service.MeasureAsync([a, b], (p, d) => seen.Add((Path.GetFileName(p), d)));

        Assert.Equal([("a.mp4", Min(10)), ("b.mp4", Min(25))], seen);
        Assert.Equal(["a.mp4", "b.mp4"], probe.Calls);
    }

    [Fact]
    public async Task Measured_files_are_remembered_so_the_second_visit_asks_nobody()
    {
        var a = File_("a.mp4");
        var probe = new FakeProbe(new() { ["a.mp4"] = Min(10) });
        var service = new DurationService(_store, probe);
        await service.MeasureAsync([a], (_, _) => { });

        var known = service.LoadKnown(_folder, [a]);
        Assert.Equal(Min(10), known[a]);
        Assert.Single(probe.Calls);
    }

    [Fact]
    public void Nothing_is_known_before_anything_is_measured()
    {
        var a = File_("a.mp4");
        var service = new DurationService(_store, new FakeProbe([]));
        Assert.Empty(service.LoadKnown(_folder, [a]));
    }

    [Fact]
    public async Task A_file_that_changed_is_measured_again()
    {
        var a = File_("a.mp4", "short");
        var probe = new FakeProbe(new() { ["a.mp4"] = Min(10) });
        var service = new DurationService(_store, probe);
        await service.MeasureAsync([a], (_, _) => { });

        File.WriteAllText(a, "a different and longer file");
        Assert.Empty(service.LoadKnown(_folder, [a]));
    }

    [Fact]
    public async Task An_unreadable_file_is_recorded_as_zero_and_not_tried_again()
    {
        var a = File_("broken.mp4");
        var probe = new FakeProbe(new() { ["broken.mp4"] = null });
        var service = new DurationService(_store, probe);

        TimeSpan? reported = null;
        await service.MeasureAsync([a], (_, d) => reported = d);

        Assert.Equal(TimeSpan.Zero, reported);
        Assert.Equal(TimeSpan.Zero, service.LoadKnown(_folder, [a])[a]);
    }

    [Fact]
    public async Task Files_that_have_disappeared_are_skipped()
    {
        var a = File_("a.mp4");
        var gone = Path.Combine(_folder, "gone.mp4");
        var probe = new FakeProbe(new() { ["a.mp4"] = Min(3) });
        var seen = new List<string>();
        await new DurationService(_store, probe).MeasureAsync([gone, a], (p, _) => seen.Add(Path.GetFileName(p)));
        Assert.Equal(["a.mp4"], seen);
        Assert.Equal(["a.mp4"], probe.Calls);
    }

    [Fact]
    public async Task Cancelling_stops_after_the_current_file()
    {
        var a = File_("a.mp4"); var b = File_("b.mp4"); var c = File_("c.mp4");
        var probe = new FakeProbe(new() { ["a.mp4"] = Min(1), ["b.mp4"] = Min(2), ["c.mp4"] = Min(3) });
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await new DurationService(_store, probe).MeasureAsync([a, b, c], (p, _) =>
        {
            seen.Add(Path.GetFileName(p));
            cts.Cancel(); // the user switched to another tab
        }, cts.Token);

        Assert.Equal(["a.mp4"], seen);
    }

    [Fact]
    public void A_length_learned_by_playing_is_remembered_without_measuring()
    {
        var a = File_("a.mp4");
        var probe = new FakeProbe([]);
        var service = new DurationService(_store, probe);
        service.Remember(a, Min(42));

        Assert.Equal(Min(42), service.LoadKnown(_folder, [a])[a]);
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public void Remembering_a_zero_length_changes_nothing()
    {
        var a = File_("a.mp4");
        var service = new DurationService(_store, new FakeProbe([]));
        service.Remember(a, TimeSpan.Zero);
        Assert.Empty(service.LoadKnown(_folder, [a]));
    }

    [Fact]
    public async Task Only_files_under_the_folder_are_looked_up_and_paths_ignore_case()
    {
        var a = File_("sub/a.mp4");
        var service = new DurationService(_store, new FakeProbe(new() { ["a.mp4"] = Min(7) }));
        await service.MeasureAsync([a], (_, _) => { });

        Assert.Equal(Min(7), service.LoadKnown(_folder.ToUpperInvariant(), [a])[a]);
        Assert.Empty(service.LoadKnown(Path.Combine(_folder, "other"), [a]));
    }
}
