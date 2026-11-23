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

    public event PropertyChangedEventHandler? PropertyChanged;
}
