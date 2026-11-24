using System.Collections.ObjectModel;
using System.ComponentModel;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.App.ViewModels;

/// <summary>One tab: a folder shown as a playlist (typically one per subject).</summary>
public sealed class TabViewModel : INotifyPropertyChanged
{
    private string _title;

    public TabViewModel(TabRecord record)
    {
        Id = record.Id;
        Folder = record.Folder;
        LastVideo = record.LastVideo;
        _title = record.Title;
    }

    public long Id { get; }
    public string Folder { get; }
    public string? LastVideo { get; set; }
    public bool FolderMissing { get; set; }
    public ObservableCollection<PlaylistItem> Items { get; } = [];

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public int WatchedCount => Items.Count(i => i.IsWatched);

    /// <summary>Length of the whole playlist, counting only the videos measured so far.</summary>
    public TimeSpan TotalDuration => TimeSpan.FromTicks(Items.Sum(i => i.Duration?.Ticks ?? 0));

    /// <summary>How many videos still wait to be measured (unreadable ones count as done).</summary>
    public int UnmeasuredCount => Items.Count(i => i.Duration is null);

    /// <summary>What is left to watch: unwatched videos, less the part already played.</summary>
    public TimeSpan RemainingDuration =>
        TimeSpan.FromTicks((long)Items.Where(i => !i.IsWatched && i.Duration is not null)
            .Sum(i => i.Duration!.Value.Ticks * (1 - Math.Clamp(i.Progress, 0, 1))));

    public event PropertyChangedEventHandler? PropertyChanged;
}
