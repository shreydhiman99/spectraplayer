using System.ComponentModel;

namespace SpectraPlayer.Core.Bookmarks;

/// <summary>A named position in a video. Mirrors one line of a PotPlayer .pbf file.</summary>
public sealed class Bookmark : INotifyPropertyChanged
{
    private string _name;

    public Bookmark(TimeSpan position, string? name = null, string? thumbnailRaw = null)
    {
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _name = string.IsNullOrWhiteSpace(name) ? TimeFormat.Format(Position) : name.Trim();
        ThumbnailRaw = thumbnailRaw ?? string.Empty;
    }

    public TimeSpan Position { get; }

    public string Name
    {
        get => _name;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? TimeFormat.Format(Position) : value.Trim();
            if (v == _name) return;
            _name = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    /// <summary>
    /// Opaque third field of a .pbf line (PotPlayer stores a thumbnail/hash there). We never interpret it;
    /// it is kept so bookmarks imported from PotPlayer survive a round trip.
    /// </summary>
    public string ThumbnailRaw { get; }

    public string PositionText => TimeFormat.Format(Position);

    public event PropertyChangedEventHandler? PropertyChanged;
}
