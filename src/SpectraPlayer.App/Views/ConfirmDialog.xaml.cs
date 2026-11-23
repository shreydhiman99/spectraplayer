using System.Windows;

namespace SpectraPlayer.App.Views;

/// <summary>A themed yes/no question: used where the app must get permission before doing something (such as a download).</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string okText)
    {
        InitializeComponent();
        DialogChrome.Attach(this);
        Title = title;
        Message.Text = message;
        OkButton.Content = okText;
    }

    /// <returns>True if the user pressed the confirm button.</returns>
    public static bool Ask(Window owner, string title, string message, string okText) =>
        new ConfirmDialog(title, message, okText) { Owner = owner }.ShowDialog() == true;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
