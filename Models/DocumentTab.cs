using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotepadX.Services;

namespace NotepadX.Models;

/// <summary>
/// One tab. Owns its own TextBox instance for the lifetime of the tab so that undo
/// history, caret position and scroll offset survive switching between tabs.
/// </summary>
public sealed class DocumentTab : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string? _filePath;
    private bool _isDirty;
    private bool _isActive;
    private TextEncodingInfo _encoding = TextFileIo.Utf8;
    private LineEnding _lineEnding = LineEnding.Crlf;

    public Guid Id { get; init; } = Guid.NewGuid();

    public TextBox Editor { get; }

    /// <summary>Remembered while the tab is not the one shown in the editor host.</summary>
    public double ScrollOffset { get; set; }

    /// <summary>Set when the user answered "Don't save", so nothing is kept for recovery.</summary>
    public bool Discarded { get; set; }

    public DocumentTab()
    {
        Editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 6, 10, 6),
            UndoLimit = 500,
            SpellCheck = { IsEnabled = false },
            Tag = this
        };
        Editor.IsInactiveSelectionHighlightEnabled = true;
        Editor.TextChanged += (_, _) => { if (!Suppressed) IsDirty = true; };
    }

    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) OnPropertyChanged(nameof(DisplayTitle)); }
    }

    public string DisplayTitle => IsDirty ? Title + "*" : Title;

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (!Set(ref _filePath, value)) return;
            Title = string.IsNullOrEmpty(value) ? Title : Path.GetFileName(value);
            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(HasFile));
        }
    }

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    public string ToolTipText => FilePath ?? Title;

    public bool IsDirty
    {
        get => _isDirty;
        set { if (Set(ref _isDirty, value)) OnPropertyChanged(nameof(DisplayTitle)); }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public TextEncodingInfo Encoding
    {
        get => _encoding;
        set { if (Set(ref _encoding, value)) OnPropertyChanged(nameof(EncodingLabel)); }
    }

    public string EncodingLabel => Encoding.Name;

    public LineEnding LineEnding
    {
        get => _lineEnding;
        set { if (Set(ref _lineEnding, value)) OnPropertyChanged(nameof(LineEndingLabel)); }
    }

    public string LineEndingLabel => TextFileIo.Label(LineEnding);

    /// <summary>Set while loading a file so TextChanged does not mark the tab dirty.</summary>
    public bool Suppressed { get; private set; }

    public void SetTextSilently(string text)
    {
        Suppressed = true;
        try
        {
            Editor.IsUndoEnabled = false;   // clears the stack, so loading is not undoable
            Editor.Text = text;
            Editor.CaretIndex = 0;
            Editor.IsUndoEnabled = true;
        }
        finally
        {
            Suppressed = false;
        }
        IsDirty = false;
    }

    public void ApplyOptions(AppSettings s, double zoom)
    {
        Editor.FontFamily = SafeFont(s.FontFamily);
        Editor.FontSize = Math.Max(1, s.FontSize * 96.0 / 72.0 * zoom);
        Editor.FontWeight = s.FontBold ? FontWeights.Bold : FontWeights.Normal;
        Editor.FontStyle = s.FontItalic ? FontStyles.Italic : FontStyles.Normal;
        Editor.TextWrapping = s.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.HorizontalScrollBarVisibility = s.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Editor.SpellCheck.IsEnabled = s.SpellCheck;
    }

    private static FontFamily SafeFont(string name)
    {
        try { return new FontFamily(name); }
        catch (Exception) { return new FontFamily("Consolas"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
