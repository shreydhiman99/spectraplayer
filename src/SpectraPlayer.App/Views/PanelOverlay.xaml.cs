using System.Windows;

namespace SpectraPlayer.App.Views;

/// <summary>
/// Borderless always-on-top window that holds the side panel when it slides in over the video: in fullscreen, or when the
/// panel is hidden and the mouse hits the right edge. (Like the fullscreen control bar it must be its own window, because
/// the video is a native child window and WPF content can't be drawn over it.)
/// The window grows leftwards from the screen edge while its content keeps its width, so the panel appears to slide in.
/// </summary>
public partial class PanelOverlay : Window
{
    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(PanelOverlay),
        new PropertyMetadata(0.0, (d, _) => ((PanelOverlay)d).Apply()));

    private double _right, _fullWidth = 340;

    public PanelOverlay() => InitializeComponent();

    /// <summary>0 = fully hidden past the edge, 1 = fully shown.</summary>
    public double Reveal
    {
        get => (double)GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    /// <summary>Where the panel docks, in device-independent units: its right edge, top and height.</summary>
    public void Place(double right, double top, double height, double fullWidth)
    {
        _right = right;
        _fullWidth = fullWidth;
        Top = top;
        Height = height;
        Apply();
    }

    private void Apply()
    {
        var width = Math.Max(1, Math.Round(_fullWidth * Reveal));
        Width = width;
        Left = _right - width;
    }
}
