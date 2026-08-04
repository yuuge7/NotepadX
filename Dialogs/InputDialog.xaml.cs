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
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>Before the first paint, or the caption is drawn light and stays light.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.SetDarkTitleBar(this, ThemeManager.IsDark);
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }
}
