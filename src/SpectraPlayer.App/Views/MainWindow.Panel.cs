using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpectraPlayer.App.Views;

// The side panel (playlist, notes, transcript, bookmarks).
//
// It can be in one of three places:
//   docked   - beside the video, in the window layout. The user's choice ("pinned"), remembered between runs.
//   hidden   - out of the way. Moving the mouse to the right edge slides it in over the video ("peek"); leaving slides it out.
//   fullscreen - there is no layout to dock into, so the panel always works as a peek.
// A peek has to live in its own window, because the video is a native child window that WPF content can't be drawn over.
public partial class MainWindow
{
    private const double PanelWidth = 340;
    private const double EdgeStripDip = 12;       // how close to the right edge counts as "striking" it
    private const int PeekLingerMs = 450;         // how long the mouse may stay away before the peek slides out

    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private bool _panelPinned = true;             // docked when not fullscreen (saved)
    private bool _slidingOut;                     // the docked panel is animating away
    private PanelOverlay? _panelOverlay;
    private bool _peeking;                        // the panel is in the overlay window (opening, open or closing)
    private bool _peekClosing;
    private bool _peekSticky;                     // opened by key or button: stays until the mouse has visited it and left
    private int _panelGeneration;                 // a finished animation only acts if no newer one has started
    private int _edgeHits, _inactiveTicks;
    private long _outsideSince;

    private void InitializePanel()
    {
        _edgeTimer.Tick += OnEdgeTick;
        _edgeTimer.Start();
    }

    // ---- state ------------------------------------------------------------------------------------------------

    private void RestorePanelState()
    {
        var saved = _library.GetSetting("panel");
        _panelPinned = saved != "hidden";
        ApplyPanelView(saved switch
        {
            "bookmarks" => PanelView.Bookmarks,
            "notes" => PanelView.Notes,
            "transcript" => PanelView.Transcript,
            "focus" => PanelView.Focus,
            _ => PanelView.Playlist,
        });
        SidePanel.Visibility = _panelPinned ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SavePanelState() =>
        _library.SetSetting("panel", !_panelPinned ? "hidden"
            : _panelView switch { PanelView.Bookmarks => "bookmarks", PanelView.Notes => "notes", PanelView.Transcript => "transcript", PanelView.Focus => "focus", _ => "playlist" });

    /// <summary>Which of the four views the panel shows. Says nothing about whether the panel itself is visible.</summary>
    private void ApplyPanelView(PanelView view)
    {
        _panelView = view;
        PlaylistView.Visibility = view == PanelView.Playlist ? Visibility.Visible : Visibility.Collapsed;
        NotesView.Visibility = view == PanelView.Notes ? Visibility.Visible : Visibility.Collapsed;
        TranscriptView.Visibility = view == PanelView.Transcript ? Visibility.Visible : Visibility.Collapsed;
        BookmarksView.Visibility = view == PanelView.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
        FocusView.Visibility = view == PanelView.Focus ? Visibility.Visible : Visibility.Collapsed;
        PanelFocusButton.IsChecked = view == PanelView.Focus;
        PanelPlaylistButton.IsChecked = view == PanelView.Playlist;
        PanelNotesButton.IsChecked = view == PanelView.Notes;
        PanelTranscriptButton.IsChecked = view == PanelView.Transcript;
        PanelBookmarksButton.IsChecked = view == PanelView.Bookmarks;
    }

    /// <summary>The panel's own tab buttons: switch view and leave the panel where it is (a peek stays a peek).</summary>
    private void SwitchPanelView(PanelView view)
    {
        ApplyPanelView(view);
        SavePanelState();
    }

    /// <summary>Make the panel visible with this view: docked in a window, sliding in over the video in fullscreen.</summary>
    private void ShowPanel(PanelView view)
    {
        ApplyPanelView(view);
        if (_fullscreen)
        {
            OpenPeek(sticky: true);
        }
        else
        {
            _panelPinned = true;
            DockPanel();
        }
        SavePanelState();
    }

    /// <summary>P / H and the control-bar button: show the panel with this view, or hide it if it is already showing it.</summary>
    private void TogglePanel(PanelView view)
    {
        var showing = _fullscreen ? _peeking && !_peekClosing : _peeking || (SidePanel.Visibility == Visibility.Visible && !_slidingOut);
        var pinnedHere = _fullscreen || _panelPinned;
        if (showing && pinnedHere && _panelView == view) HidePanel();
        else ShowPanel(view);
    }

    private void HidePanel()
    {
        if (_peeking)
        {
            ClosePeek(animate: true);
            return;
        }
        if (_fullscreen) return;

        _panelPinned = false;
        SavePanelState();
        SlideDocked(show: false);
    }

    private void OnHidePanelClick(object sender, RoutedEventArgs e) => HidePanel();

    // ---- docked: the column's width slides --------------------------------------------------------------------

    private void DockPanel()
    {
        if (_peeking)
        {
            EndPeek(dock: true); // already in the right place: just hand it back to the layout
            return;
        }
        if (SidePanel.Visibility == Visibility.Visible && !_slidingOut) return;
        SlideDocked(show: true);
    }

    private void SlideDocked(bool show)
    {
        var generation = ++_panelGeneration;
        var from = SidePanel.Visibility == Visibility.Visible ? SidePanel.Width : 0;
        SidePanel.Visibility = Visibility.Visible;
        _slidingOut = !show;

        var animation = new DoubleAnimation(from, show ? PanelWidth : 0, SlideTime(show)) { EasingFunction = Ease(show) };
        animation.Completed += (_, _) =>
        {
            if (generation != _panelGeneration) return;
            SidePanel.BeginAnimation(WidthProperty, null);
            SidePanel.Width = PanelWidth;
            _slidingOut = false;
            if (!show) SidePanel.Visibility = Visibility.Collapsed;
        };
        SidePanel.BeginAnimation(WidthProperty, animation);
    }

    private static Duration SlideTime(bool opening) => new(TimeSpan.FromMilliseconds(opening ? 240 : 200));

    private static IEasingFunction Ease(bool opening) =>
        new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };

    // ---- peek: the panel in its own window, sliding over the video ---------------------------------------------

    private void OpenPeek(bool sticky)
    {
        if (_peeking && !_peekClosing) return;
        if (!TryGetPanelAnchor(out var right, out var top, out var height)) return;

        var generation = ++_panelGeneration;
        if (_fullscreen) ShowOverlay(); // the control bar comes up too, so the panel sits above it
        if (_panelOverlay is null)
        {
            _panelOverlay = new PanelOverlay { Owner = this };
            _panelOverlay.PreviewKeyDown += OnPreviewKeyDown; // shortcuts keep working while it has the keyboard
        }

        if (!_peeking)
        {
            // Hand the panel from the layout to the overlay window.
            SidePanel.BeginAnimation(WidthProperty, null);
            ContentArea.Children.Remove(SidePanel);
            SidePanel.Width = PanelWidth;
            SidePanel.Visibility = Visibility.Visible;
            _slidingOut = false;
            _panelOverlay.Content = SidePanel;
            _panelOverlay.Reveal = 0;
            _panelOverlay.Place(right, top, height, PanelWidth);
            _panelOverlay.Show();
            _peeking = true;
        }
        else
        {
            _panelOverlay.Place(right, top, height, PanelWidth); // was on its way out and the mouse came back
        }

        _peekClosing = false;
        _peekSticky = sticky;
        _outsideSince = 0;
        if (_fullscreen) VideoArea.Cursor = null;
        AnimateReveal(1, generation, null);
    }

    private void ClosePeek(bool animate)
    {
        if (!_peeking) return;
        if (!animate)
        {
            EndPeek(dock: false);
            return;
        }
        if (_peekClosing) return;

        _peekClosing = true;
        var generation = ++_panelGeneration;
        AnimateReveal(0, generation, () => EndPeek(dock: false));
    }

    /// <summary>Takes the panel back from the overlay window into the layout (visible if it is to stay docked).</summary>
    private void EndPeek(bool dock)
    {
        if (!_peeking || _panelOverlay is null) return;
        _panelGeneration++; // cancels any slide still running

        _panelOverlay.BeginAnimation(PanelOverlay.RevealProperty, null);
        _panelOverlay.Content = null;
        _panelOverlay.Hide();
        _panelOverlay.Reveal = 0;

        ContentArea.Children.Add(SidePanel);
        Grid.SetColumn(SidePanel, 1);
        SidePanel.Width = PanelWidth;
        SidePanel.Visibility = dock && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        _peeking = _peekClosing = _peekSticky = false;
        _inactiveTicks = 0;
    }

    private void AnimateReveal(double to, int generation, Action? completed)
    {
        var overlay = _panelOverlay!;
        var animation = new DoubleAnimation(overlay.Reveal, to, SlideTime(to > 0)) { EasingFunction = Ease(to > 0) };
        animation.Completed += (_, _) =>
        {
            if (generation != _panelGeneration) return;
            overlay.BeginAnimation(PanelOverlay.RevealProperty, null);
            overlay.Reveal = to;
            completed?.Invoke();
        };
        overlay.BeginAnimation(PanelOverlay.RevealProperty, animation);
    }

    /// <summary>Where the peek docks (right edge, top, height in device-independent units): beside the video area, or the whole screen height above the control bar in fullscreen.</summary>
    private bool TryGetPanelAnchor(out double right, out double top, out double height)
    {
        right = top = height = 0;
        var dpi = VisualTreeHelper.GetDpi(this);

        if (_fullscreen)
        {
            if (!NativeMethods.TryGetMonitorBounds(this, out var bounds)) return false;
            var bar = _overlay is { IsVisible: true } ? _overlay.ActualHeight : 0; // the whole height once the bar has hidden
            right = (bounds.X + bounds.Width) / dpi.DpiScaleX;
            top = bounds.Y / dpi.DpiScaleY;
            height = bounds.Height / dpi.DpiScaleY - bar;
        }
        else
        {
            var origin = ContentArea.PointToScreen(new Point(0, 0));
            var end = ContentArea.PointToScreen(new Point(ContentArea.ActualWidth, ContentArea.ActualHeight));
            right = end.X / dpi.DpiScaleX;
            top = origin.Y / dpi.DpiScaleY;
            height = (end.Y - origin.Y) / dpi.DpiScaleY;
        }
        return height > 0;
    }

    /// <summary>In fullscreen the control bar comes and goes; the open panel follows it so no strip of video shows below the panel.</summary>
    private void FitPeekToControlBar()
    {
        if (!_peeking || !_fullscreen || _panelOverlay is null) return;
        if (TryGetPanelAnchor(out var right, out var top, out var height))
            _panelOverlay.Place(right, top, height, PanelWidth);
    }

    // ---- the mouse striking the right edge --------------------------------------------------------------------

    private void OnEdgeTick(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized || !NativeMethods.TryGetCursor(out var cursor)) return;

        if (_peeking && !_peekClosing)
        {
            TickOpenPeek(cursor);
            return;
        }

        var docked = !_peeking && SidePanel.Visibility == Visibility.Visible;
        if (docked || ActiveOwnWindow() is null || !InEdgeStrip(cursor))
        {
            _edgeHits = 0;
            return;
        }

        // Two ticks in a row (~0.1 s): a deliberate move to the edge, not a fly-by.
        if (++_edgeHits >= 2)
        {
            _edgeHits = 0;
            OpenPeek(sticky: false);
        }
    }

    private void TickOpenPeek(Point cursor)
    {
        // Another program in front: don't float over it. One of our own dialogs in front (opened from the panel): leave things be.
        var active = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (active is null)
        {
            if (++_inactiveTicks >= 3) ClosePeek(animate: false);
            return;
        }
        _inactiveTicks = 0;
        if (active != this && active != _overlay && active != _panelOverlay) return;

        var inside = IsOverPeek(cursor);
        if (_peekSticky)
        {
            if (!inside) return;
            _peekSticky = false; // the mouse has been here; from now on leaving closes it
        }

        // Stay open while the mouse is on it, a menu or drag holds the mouse, or a note is being typed.
        if (inside || Mouse.Captured is not null || NoteInput.IsKeyboardFocusWithin)
        {
            _outsideSince = 0;
            return;
        }

        if (_outsideSince == 0) _outsideSince = Environment.TickCount64;
        else if (Environment.TickCount64 - _outsideSince >= PeekLingerMs) ClosePeek(animate: true);
    }

    private bool IsOverPeek(Point cursor)
    {
        if (_panelOverlay is null) return false;
        var dpi = VisualTreeHelper.GetDpi(_panelOverlay);
        var rect = new Rect(_panelOverlay.Left * dpi.DpiScaleX, _panelOverlay.Top * dpi.DpiScaleY,
                            _panelOverlay.Width * dpi.DpiScaleX, _panelOverlay.Height * dpi.DpiScaleY);
        return rect.Contains(cursor);
    }

    /// <summary>Is the pointer in the thin strip at the right edge of the video area (and over this program, not a window in front)?</summary>
    private bool InEdgeStrip(Point cursor)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double right, top, bottom;

        if (_fullscreen || WindowState == WindowState.Maximized)
        {
            // The strip is the screen's own edge. (A maximised window's content is inset a little; the screen edge is what you hit.)
            if (!NativeMethods.TryGetMonitorBounds(this, out var bounds)) return false;
            right = bounds.X + bounds.Width;
            var origin = _fullscreen ? new Point(bounds.X, bounds.Y) : ContentArea.PointToScreen(new Point(0, 0));
            top = origin.Y;
            bottom = _fullscreen ? bounds.Y + bounds.Height : ContentArea.PointToScreen(new Point(0, ContentArea.ActualHeight)).Y;
        }
        else
        {
            // Not at the very edge: the outermost few pixels grab the window border for resizing.
            var origin = ContentArea.PointToScreen(new Point(0, 0));
            var end = ContentArea.PointToScreen(new Point(ContentArea.ActualWidth, ContentArea.ActualHeight));
            right = end.X - SystemParameters.WindowResizeBorderThickness.Right * dpi.DpiScaleX;
            top = origin.Y;
            bottom = end.Y;
        }

        var strip = EdgeStripDip * dpi.DpiScaleX;
        if (cursor.X < right - strip || cursor.X > right || cursor.Y < top || cursor.Y >= bottom) return false;
        return OwnsWindow(NativeMethods.TopLevelWindowAt(cursor));
    }

    private bool OwnsWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return hwnd == new WindowInteropHelper(this).Handle
            || (_overlay is not null && hwnd == new WindowInteropHelper(_overlay).Handle)
            || (_panelOverlay is not null && hwnd == new WindowInteropHelper(_panelOverlay).Handle);
    }

    /// <summary>The window of ours that has the keyboard, if it is the player (or one of its overlays) and not a dialog.</summary>
    private Window? ActiveOwnWindow()
    {
        var active = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return active == this || active == _overlay || active == _panelOverlay ? active : null;
    }
}
