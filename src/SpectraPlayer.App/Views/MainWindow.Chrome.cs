using System.Windows;

namespace SpectraPlayer.App.Views;

// The custom title bar: window buttons and the About box.
public partial class MainWindow
{
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var maximised = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximised ? "" : ""; // restore / maximise glyphs
        MaximizeButton.ToolTip = maximised ? "Restore" : "Maximise";

        // A window without a Windows title bar extends past the screen edges by its (invisible) resize border when
        // maximised, which would hide the top of the title bar and the bottom of the controls. Pull the content back in.
        Root.Margin = maximised ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => new AboutDialog { Owner = this }.ShowDialog();
}
