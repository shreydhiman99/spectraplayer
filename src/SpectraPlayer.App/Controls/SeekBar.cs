using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SpectraPlayer.Core;

namespace SpectraPlayer.App.Controls;

public sealed record SeekMarker(double Seconds, string Label);

/// <summary>Raised while the user Shift+drags on the seek bar to mark a section (the two points can be in either order).</summary>
public sealed class SectionDraggedEventArgs(double fromSeconds, double toSeconds, bool isFinal) : EventArgs
{
    public double FromSeconds { get; } = fromSeconds;
    public double ToSeconds { get; } = toSeconds;
    public bool IsFinal { get; } = isFinal;
}

public sealed class SeekRequestedEventArgs(double seconds, bool isFinal) : EventArgs
{
    public double Seconds { get; } = seconds;
    /// <summary>False while the user is still dragging (use a fast seek), true on release or a plain click.</summary>
    public bool IsFinal { get; } = isFinal;
}

/// <summary>Timeline with bookmark ticks, hover preview and click/drag seeking.</summary>
public sealed class SeekBar : FrameworkElement
{
    private const double PadX = 8;
    private const double MarkerHitRadius = 6;

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(SeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(SeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers), typeof(IReadOnlyList<SeekMarker>), typeof(SeekBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NoteMarkersProperty = DependencyProperty.Register(
        nameof(NoteMarkers), typeof(IReadOnlyList<SeekMarker>), typeof(SeekBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart), typeof(double?), typeof(SeekBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionEndProperty = DependencyProperty.Register(
        nameof(SelectionEnd), typeof(double?), typeof(SeekBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly ToolTip _tip = new()
    {
        Placement = PlacementMode.Relative,
        IsHitTestVisible = false,
        VerticalOffset = -34,
    };

    private bool _dragging;
    private double _dragSeconds;
    private bool _selecting;       // Shift+drag in progress
    private double _selectAnchor;

    public SeekBar()
    {
        Height = 24;
        Cursor = Cursors.Hand;
        Focusable = false;
        _tip.PlacementTarget = this;
    }

    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public IReadOnlyList<SeekMarker>? Markers { get => (IReadOnlyList<SeekMarker>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }

    /// <summary>Notes: drawn as small dots under the track, so they don't collide with bookmark ticks.</summary>
    public IReadOnlyList<SeekMarker>? NoteMarkers { get => (IReadOnlyList<SeekMarker>?)GetValue(NoteMarkersProperty); set => SetValue(NoteMarkersProperty, value); }

    /// <summary>Seconds of the marked section's start / end (null = not set).</summary>
    public double? SelectionStart { get => (double?)GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public double? SelectionEnd { get => (double?)GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }

    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;
    public event EventHandler<SectionDraggedEventArgs>? SectionDragged;

    private double TrackWidth => Math.Max(1, ActualWidth - 2 * PadX);

    private double XFor(double seconds) =>
        PadX + (Duration > 0 ? Math.Clamp(seconds / Duration, 0, 1) : 0) * TrackWidth;

    private double SecondsFor(double x) =>
        Duration > 0 ? Math.Clamp((x - PadX) / TrackWidth, 0, 1) * Duration : 0;

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, cy = h / 2;
        var active = IsMouseOver || _dragging;
        var trackH = active ? 6.0 : 4.0;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // make the whole strip hit-testable

        dc.DrawRoundedRectangle(Res("HoverBrush", Brushes.DimGray), null,
            new Rect(PadX, cy - trackH / 2, TrackWidth, trackH), trackH / 2, trackH / 2);

        var shown = _dragging ? _dragSeconds : Position;
        var px = XFor(shown);
        if (Duration > 0 && px > PadX)
            dc.DrawRoundedRectangle(Res("AccentBrush", Brushes.DodgerBlue), null,
                new Rect(PadX, cy - trackH / 2, px - PadX, trackH), trackH / 2, trackH / 2);

        if (Duration > 0) DrawSelection(dc, cy);

        if (Markers is { } markers)
        {
            var markerBrush = Res("MarkerBrush", Brushes.Gold);
            foreach (var m in markers)
                dc.DrawRoundedRectangle(markerBrush, null, new Rect(XFor(m.Seconds) - 1.5, cy - 8, 3, 16), 1.5, 1.5);
        }

        if (NoteMarkers is { } notes && Duration > 0)
        {
            var noteBrush = Res("NoteBrush", Brushes.Aquamarine);
            foreach (var n in notes)
                dc.DrawEllipse(noteBrush, null, new Point(XFor(n.Seconds), cy + 10), 2.5, 2.5);
        }

        if (active && Duration > 0)
            dc.DrawEllipse(Res("TextBrush", Brushes.White), null, new Point(px, cy), 6.5, 6.5);
    }

    /// <summary>A translucent band over the marked section, with [ ] brackets at its ends (just one bracket if only one end is set).</summary>
    private void DrawSelection(DrawingContext dc, double cy)
    {
        if (SelectionStart is null && SelectionEnd is null) return;

        var brush = Res("SelectionBrush", Brushes.MediumPurple);
        var pen = new Pen(brush, 2);

        if (SelectionStart is { } s && SelectionEnd is { } e && e > s)
        {
            var x1 = XFor(s);
            var x2 = XFor(e);
            dc.PushOpacity(0.35);
            dc.DrawRoundedRectangle(brush, null, new Rect(x1, cy - 10, Math.Max(2, x2 - x1), 20), 3, 3);
            dc.Pop();
        }

        if (SelectionStart is { } start)
        {
            var x = XFor(start);
            dc.DrawLine(pen, new Point(x, cy - 10), new Point(x, cy + 10));
            dc.DrawLine(pen, new Point(x, cy - 10), new Point(x + 5, cy - 10));
            dc.DrawLine(pen, new Point(x, cy + 10), new Point(x + 5, cy + 10));
        }

        if (SelectionEnd is { } end)
        {
            var x = XFor(end);
            dc.DrawLine(pen, new Point(x, cy - 10), new Point(x, cy + 10));
            dc.DrawLine(pen, new Point(x, cy - 10), new Point(x - 5, cy - 10));
            dc.DrawLine(pen, new Point(x, cy + 10), new Point(x - 5, cy + 10));
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _tip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Duration <= 0) return;

        var x = e.GetPosition(this).X;
        UpdateTip(x);

        if (_selecting)
        {
            SectionDragged?.Invoke(this, new SectionDraggedEventArgs(_selectAnchor, SecondsFor(x), isFinal: false));
        }
        else if (_dragging)
        {
            _dragSeconds = SecondsFor(x);
            InvalidateVisual();
            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(_dragSeconds, isFinal: false));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Shift+drag marks a section instead of seeking.
            _selecting = true;
            _selectAnchor = SecondsFor(e.GetPosition(this).X);
            CaptureMouse();
            SectionDragged?.Invoke(this, new SectionDraggedEventArgs(_selectAnchor, _selectAnchor, isFinal: false));
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragSeconds = SecondsFor(e.GetPosition(this).X);
        CaptureMouse();
        InvalidateVisual();
        SeekRequested?.Invoke(this, new SeekRequestedEventArgs(_dragSeconds, isFinal: false));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();
            SectionDragged?.Invoke(this, new SectionDraggedEventArgs(_selectAnchor, SecondsFor(e.GetPosition(this).X), isFinal: true));
            e.Handled = true;
            return;
        }

        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        SetCurrentValue(PositionProperty, _dragSeconds); // hold the bar here until the player reports the new time
        SeekRequested?.Invoke(this, new SeekRequestedEventArgs(_dragSeconds, isFinal: true));
        e.Handled = true;
    }

    private void UpdateTip(double x)
    {
        var text = TimeFormat.Format(SecondsFor(x));

        // Closest bookmark or note within reach of the pointer wins.
        var near = (Markers ?? []).Select(m => (Marker: m, Prefix: ""))
            .Concat((NoteMarkers ?? []).Select(m => (Marker: m, Prefix: "Note: ")))
            .Where(c => Math.Abs(XFor(c.Marker.Seconds) - x) <= MarkerHitRadius)
            .OrderBy(c => Math.Abs(XFor(c.Marker.Seconds) - x))
            .FirstOrDefault(); // a default tuple (no marker) when nothing is in reach
        if (near.Marker is not null) text = $"{near.Prefix}{near.Marker.Label}  ·  {TimeFormat.Format(near.Marker.Seconds)}";

        _tip.Content = text;
        _tip.HorizontalOffset = x - 24;
        _tip.IsOpen = true;
    }
}
