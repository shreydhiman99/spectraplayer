using System.Windows;
using SpectraPlayer.App.Views;

namespace SpectraPlayer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "Open with Spectraplayer" / command line: the first argument is a video file to play.
        var window = new MainWindow(e.Args.FirstOrDefault(File.Exists));
        MainWindow = window;
        window.Show();
    }
}
