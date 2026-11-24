using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SpectraPlayer.App.Views;

/// <summary>
/// The focus timer's corner display for fullscreen. A window of its own (the video is a native child window that WPF can't
/// draw over), translucent, and click-through so it never takes a click or the keyboard from the video.
/// </summary>
public partial class FocusHud : Window
{
    private bool _shown;

    public FocusHud()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.MakeOverlayClickThrough(this);
    }

    public void SetContent(string time, string label, double fraction, Brush arc)
    {
        TimeText.Text = time;
        LabelText.Text = label;
        Ring.Fraction = fraction;
        Ring.ArcBrush = arc;
    }

    /// <summary>Puts the display in a corner: the top right of the given screen area (device-independent units).</summary>
    public void PlaceTopRight(double right, double top, double margin)
    {
        UpdateLayout();
        Left = right - ActualWidth - margin;
        Top = top + margin;
    }

    /// <summary>Fades in or out. Only affects the window when the wish changes.</summary>
    public void FadeTo(bool visible)
    {
        if (visible == _shown) return;
        _shown = visible;

        if (visible)
        {
            if (!IsVisible) { Opacity = 0; Show(); }
            BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(160)));
        }
        else
        {
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) =>
            {
                if (_shown) return; // asked to show again meanwhile
                BeginAnimation(OpacityProperty, null);
                Opacity = 0;
                Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Gone at once (leaving fullscreen).</summary>
    public void HideNow()
    {
        _shown = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (IsVisible) Hide();
    }
}
