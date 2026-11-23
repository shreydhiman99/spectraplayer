using System.Windows;

namespace SpectraPlayer.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        DialogChrome.Attach(this);

        var v = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        VersionText.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
    }
}
