using System.ComponentModel;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Transcription;

namespace SpectraPlayer.App.ViewModels;

/// <summary>One line of the transcript list; <see cref="IsCurrent"/> marks the line being spoken right now.</summary>
public sealed class TranscriptRow(TranscriptSegment segment) : INotifyPropertyChanged
{
    private bool _isCurrent;

    public TranscriptSegment Segment { get; } = segment;
    public TimeSpan Start => Segment.Start;
    public TimeSpan End => Segment.End;
    public string Text => Segment.Text;
    public string TimeLabel => TimeFormat.Format(Segment.Start);

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
