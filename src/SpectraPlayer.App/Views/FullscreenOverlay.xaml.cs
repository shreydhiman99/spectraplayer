using System.Windows;

namespace SpectraPlayer.App.Views;

/// <summary>
/// Borderless always-on-top strip that hosts the control bar in fullscreen. It has to be its own window:
/// the video is a native child window, so WPF content can't be drawn on top of it ("airspace").
/// </summary>
public partial class FullscreenOverlay : Window
{
    public FullscreenOverlay() => InitializeComponent();
}
