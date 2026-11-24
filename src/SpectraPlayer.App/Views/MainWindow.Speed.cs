using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SpectraPlayer.App.Views;

// Playback speed: the button in the control bar (shows the speed, opens presets) and the shortcuts C / X / Z, > and <.
public partial class MainWindow
{
    private static readonly double[] SpeedPresets = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3];

    private static string SpeedText(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    /// <summary>Shows the current speed on the button, in the accent colour whenever it isn't normal.</summary>
    private void UpdateSpeedButton()
    {
        var speed = _engine.Speed;
        SpeedButton.Content = SpeedText(speed);
        var normal = Math.Abs(speed - 1) < 0.01;
        SpeedButton.Foreground = (Brush)FindResource(normal ? "TextBrush" : "AccentHoverBrush");
    }

    private void OnSpeedClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SpeedButton, Placement = PlacementMode.Top };

        foreach (var speed in SpeedPresets)
        {
            var current = Math.Abs(_engine.Speed - speed) < 0.01;
            var item = new MenuItem
            {
                Header = $"{(current ? "✓  " : "     ")}{SpeedText(speed)}{(speed == 1 ? "  Normal" : string.Empty)}",
            };
            item.Click += (_, _) => ChangeSpeed(speed);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnShortcutsClick(object sender, RoutedEventArgs e) => ShowShortcuts();

    private void ShowShortcuts()
    {
        var dialog = new ShortcutsDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.ChangeRequested) ShowSettings();
    }
}
