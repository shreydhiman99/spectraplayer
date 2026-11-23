using System.ComponentModel;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.App.ViewModels;

/// <summary>One video in a tab's playlist, with its watch progress.</summary>
public sealed class PlaylistItem : INotifyPropertyChanged
{
    private double _progress;
    private bool _isWatched, _isCurrent;

    public PlaylistItem(MediaEntry entry)
    {
        Path = entry.FullPath;
        Name = entry.Name;
        Subfolder = entry.Subfolder;
    }

    public string Path { get; }
    public string Name { get; }
    public string Subfolder { get; }
    public bool HasSubfolder => Subfolder.Length > 0;

    public double Progress => _progress;
    public bool IsWatched => _isWatched;
    public bool IsCurrent => _isCurrent;

    /// <summary>Show the thin progress bar only for videos that are started but not finished.</summary>
    public bool HasProgress => _progress > 0.005 && !_isWatched;

    /// <summary>Segoe MDL2 glyph: play arrow for the current video, check mark once watched.</summary>
    public string StatusGlyph => _isCurrent ? "" : _isWatched ? "" : string.Empty;

    public void ApplyProgress(ProgressRecord? record)
    {
        _progress = record?.Fraction ?? 0;
        _isWatched = record?.IsWatched ?? false;
        Changed();
    }

    public void SetCurrent(bool value)
    {
        if (_isCurrent == value) return;
        _isCurrent = value;
        Changed();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Several properties depend on each other, so just tell the bindings everything may have changed.
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
