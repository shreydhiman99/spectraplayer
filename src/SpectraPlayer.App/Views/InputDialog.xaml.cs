using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpectraPlayer.App.Views;

public partial class InputDialog : Window
{
    private InputDialog(string title, string prompt, string initial, bool multiline)
    {
        InitializeComponent();
        DialogChrome.Attach(this);
        Title = title;
        Prompt.Text = prompt;
        Input.Text = initial;
        Input.MaxLength = multiline ? 2000 : 120;

        if (multiline)
        {
            Width = 500;
            Input.AcceptsReturn = true;
            Input.TextWrapping = TextWrapping.Wrap;
            Input.MinHeight = 130;
            Input.MaxHeight = 280;
            Input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            OkButton.ToolTip = "Ctrl+Enter";
        }

        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public string Value => Input.Text.Trim();

    /// <summary>Shows a single-line prompt modally; returns the entered text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial) =>
        Show(new InputDialog(title, prompt, initial, multiline: false), owner);

    /// <summary>Multi-line variant (Enter adds a line, Ctrl+Enter confirms).</summary>
    public static string? AskMultiline(Window owner, string title, string prompt, string initial) =>
        Show(new InputDialog(title, prompt, initial, multiline: true), owner);

    private static string? Show(InputDialog dialog, Window owner)
    {
        dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
