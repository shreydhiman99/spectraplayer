using System.Windows;
using SpectraPlayer.App.Input;

namespace SpectraPlayer.App.Views;

/// <summary>Every keyboard shortcut in one place (F1).</summary>
public partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        DialogChrome.Attach(this);
        Groups.ItemsSource = ShortcutGuide.All();
    }

    /// <summary>Set when the user asked to change them: the caller opens Settings once this dialog has closed.</summary>
    public bool ChangeRequested { get; private set; }

    private void OnChangeClick(object sender, RoutedEventArgs e)
    {
        ChangeRequested = true;
        Close();
    }
}
