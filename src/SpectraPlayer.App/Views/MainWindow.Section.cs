using System.Windows;
using SpectraPlayer.App.Controls;
using SpectraPlayer.Core;

namespace SpectraPlayer.App.Views;

// The marked section of the current video: set with I / O, by Shift+drag on the seek bar, or with a preset.
// It is the unit that gets transcribed (milestone 4) and, later, summarised or questioned (milestone 5).
public partial class MainWindow
{
    private readonly SectionSelection _selection = new();
    private bool _sectionLoop;

    private void InitializeSection()
    {
        _selection.Changed += (_, _) => RefreshSectionUi();
        RefreshSectionUi();
    }

    private TimeSpan? KnownDuration => _engine.Duration > TimeSpan.Zero ? _engine.Duration : null;

    /// <summary>A new video starts with no section, and any loop from the previous one is switched off.</summary>
    private void ResetSection()
    {
        StopSectionLoop();
        _selection.Clear();
    }

    private string SelectionSummary()
    {
        var f = (TimeSpan t) => TimeFormat.Format(t);
        return (_selection.Start, _selection.End) switch
        {
            (null, null) => "No section marked",
            ({ } s, null) => $"Start {f(s)}  ·  end not set",
            (null, { } e) => $"End {f(e)}  ·  start not set",
            ({ } s, { } e) => $"{f(s)} – {f(e)}",
        };
    }

    private void RefreshSectionUi()
    {
        Seek.SelectionStart = _selection.Start?.TotalSeconds;
        Seek.SelectionEnd = _selection.End?.TotalSeconds;

        SelectionText.Text = _selection.IsEmpty
            ? "Nothing marked yet."
            : SelectionSummary();
        SelectionLength.Text = _selection.IsComplete ? $"{TimeFormat.Format(_selection.Length)} long"
            : _selection.Start is not null && _selection.End is not null ? "Too short (under 1 second)"
            : string.Empty;

        SectionLoopButton.IsEnabled = _selection.IsComplete;
        if (_sectionLoop)
        {
            if (_selection.IsComplete) _engine.SetSectionLoop(_selection.Start, _selection.End); // follow edits to the section
            else StopSectionLoop();
        }

        RefreshTranscriptUi(); // the Transcribe button depends on a complete section
    }

    private void MarkSection(bool start)
    {
        if (!_engine.HasMedia)
        {
            ShowOsd("Open a video first");
            return;
        }

        var now = PlaybackStamp();
        if (start) _selection.SetStart(now, KnownDuration);
        else _selection.SetEnd(now, KnownDuration);
        ShowOsd("Section  " + SelectionSummary(), 2);
    }

    private void StopSectionLoop()
    {
        if (!_sectionLoop) return;
        _sectionLoop = false;
        _engine.SetSectionLoop(null, null);
        SectionLoopButton.Content = "Loop it";
    }

    // ---- events ----------------------------------------------------------------------------------------------

    private void OnSectionStartClick(object sender, RoutedEventArgs e) => MarkSection(start: true);
    private void OnSectionEndClick(object sender, RoutedEventArgs e) => MarkSection(start: false);
    private void OnPanelTranscriptClick(object sender, RoutedEventArgs e) => ShowPanel(PanelView.Transcript);

    private void OnSectionDragged(object? sender, SectionDraggedEventArgs e)
    {
        if (!_engine.HasMedia) return;
        _selection.Set(TimeSpan.FromSeconds(e.FromSeconds), TimeSpan.FromSeconds(e.ToSeconds), KnownDuration);
        if (e.IsFinal) ShowOsd("Section  " + SelectionSummary(), 2);
    }

    private void SelectLastMinutes(int minutes)
    {
        if (!_engine.HasMedia)
        {
            ShowOsd("Open a video first");
            return;
        }
        _selection.SelectLast(TimeSpan.FromMinutes(minutes), PlaybackStamp(), _engine.Duration);
        ShowOsd("Section  " + SelectionSummary(), 2);
    }

    private void OnSectionLast2Click(object sender, RoutedEventArgs e) => SelectLastMinutes(2);
    private void OnSectionLast5Click(object sender, RoutedEventArgs e) => SelectLastMinutes(5);

    private void OnSectionWholeClick(object sender, RoutedEventArgs e)
    {
        if (!_engine.HasMedia) { ShowOsd("Open a video first"); return; }
        _selection.SelectWhole(_engine.Duration);
    }

    private void OnSectionClearClick(object sender, RoutedEventArgs e) => _selection.Clear();

    private void OnSectionLoopClick(object sender, RoutedEventArgs e)
    {
        if (_sectionLoop)
        {
            StopSectionLoop();
            return;
        }
        if (!_selection.IsComplete || !_engine.HasMedia) return;

        _sectionLoop = true;
        SectionLoopButton.Content = "Stop loop";
        _engine.SetSectionLoop(_selection.Start, _selection.End);
        _engine.Seek(_selection.Start!.Value); // jump to the beginning of the section and play it
        _engine.Play();
    }
}
