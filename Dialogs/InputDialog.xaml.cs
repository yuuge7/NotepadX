using System.Windows;
using System.Windows.Input;
using NotepadX.Interop;
using NotepadX.Services;

namespace NotepadX.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog(string prompt, string title, string initial)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Title = title;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            NativeMethods.SetDarkTitleBar(this, ThemeManager.IsDark);
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }
}
