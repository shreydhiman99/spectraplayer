using System.Windows;
using System.Windows.Controls;

namespace SpectraPlayer.App.Views;

/// <summary>Hooks up the close button of the shared dialog title bar (see the DialogWindow style in the theme).</summary>
internal static class DialogChrome
{
    public static void Attach(Window window) =>
        window.Loaded += (_, _) =>
        {
            if (window.Template.FindName("PART_Close", window) is Button close)
                close.Click += (_, _) => window.Close();
        };
}
