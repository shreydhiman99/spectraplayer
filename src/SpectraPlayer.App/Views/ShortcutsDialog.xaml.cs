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
        Groups.ItemsSource = ShortcutGuide.All;
    }
}
