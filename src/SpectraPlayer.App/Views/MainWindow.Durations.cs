using System.Diagnostics;
using System.Windows;
using Microsoft.Data.Sqlite;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Library;
using SpectraPlayer.Core.Playback.Mpv;

namespace SpectraPlayer.App.Views;

// How long each video of the open playlist is, for the "total length" in the playlist header. Lengths come from the library
// database when known; the rest are measured one by one in the background (a headless libmpv, see DurationService).
public partial class MainWindow
{
    private DurationService? _durations;
    private CancellationTokenSource? _measureCts;

    private DurationService Durations => _durations ??= new DurationService(_library, new MpvDurationProbe());

    /// <summary>Called when a playlist has just been (re)loaded: fill in the lengths already known, measure the others.</summary>
    private async Task MeasureTabAsync(TabViewModel tab)
    {
        _measureCts?.Cancel();
        var cts = _measureCts = new CancellationTokenSource();

        var paths = tab.Items.Select(i => i.Path).ToList();
        var known = await Task.Run(() => Durations.LoadKnown(tab.Folder, paths), CancellationToken.None);
        if (cts.IsCancellationRequested) return;

        foreach (var item in tab.Items)
            if (known.TryGetValue(item.Path, out var length)) item.SetDuration(length);
        if (tab == _activeTab) UpdatePlaylistHeader(tab);

        var pending = tab.Items.Where(i => i.Duration is null).Select(i => i.Path).ToList();
        if (pending.Count == 0) return;

        try
        {
            await Durations.MeasureAsync(pending,
                (path, length) => Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (FindItem(tab, path) is { } item) item.SetDuration(length);
                    if (tab == _activeTab) UpdatePlaylistHeader(tab);
                }),
                cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is MpvException or MpvNotFoundException or SqliteException or IOException)
        {
            // The length is a nicety: without it the header just says what it knows.
            Debug.WriteLine(ex);
            foreach (var item in tab.Items.Where(i => i.Duration is null)) item.SetDuration(TimeSpan.Zero);
            if (tab == _activeTab) UpdatePlaylistHeader(tab);
        }
    }

    /// <summary>The player has learned a video's real length by playing it: keep it and refresh the header.</summary>
    private void LearnDuration(PlaylistItem item, TimeSpan duration)
    {
        if (item.Duration is { } known && Math.Abs((known - duration).TotalSeconds) < 1) return;
        try { Durations.Remember(item.Path, duration); }
        catch (Exception ex) when (ex is SqliteException or IOException) { Debug.WriteLine(ex); }
        item.SetDuration(duration);
        if (_activeTab is not null) UpdatePlaylistHeader(_activeTab);
    }

    /// <summary>The "12 videos · 3 watched" line, and the length of the whole playlist as a clock (08:42:15) beside it.</summary>
    private void ShowPlaylistSummary(TabViewModel tab)
    {
        var count = tab.Items.Count;
        PlaylistSummary.Text = count == 0 ? string.Empty
            : $"{count} video{(count == 1 ? "" : "s")} · {tab.WatchedCount} watched";

        var total = tab.TotalDuration;
        var pending = tab.UnmeasuredCount;
        if (count == 0 || (total == TimeSpan.Zero && pending == 0))
        {
            PlaylistTotalBox.Visibility = Visibility.Collapsed;
            return;
        }

        PlaylistTotalBox.Visibility = Visibility.Visible;
        var clock = TimeFormat.FormatClock(total);
        if (pending > 0)
        {
            PlaylistTotal.Text = clock + "+"; // still counting up
            PlaylistTotalBox.ToolTip = $"Still measuring {pending} video{(pending == 1 ? "" : "s")}: the total will grow.";
        }
        else
        {
            PlaylistTotal.Text = clock;
            PlaylistTotalBox.ToolTip = $"Whole playlist: {clock}\nStill to watch: {TimeFormat.FormatClock(tab.RemainingDuration)}";
        }
    }}
