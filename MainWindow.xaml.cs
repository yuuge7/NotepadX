using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NotepadX.Controls;
using NotepadX.Dialogs;
using NotepadX.Interop;
using NotepadX.Models;
using NotepadX.Services;

namespace NotepadX;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string AppName = "NotepadX";

    private DocumentTab? _activeTab;
    private double _zoom = 1.0;
    private string _statusLeft = "";
    private string _statusPosition = "Ln 1, Col 1";
    private bool _closing;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _statusTimer;
    private Point _dragStart;
    private DocumentTab? _dragTab;

    /// <summary>Snapshots of recently closed tabs, newest last, for Ctrl+Shift+T.</summary>
    private readonly List<ClosedTab> _closedTabs = [];
    private const int ClosedTabLimit = 10;

    private MatchHighlightAdorner? _highlightAdorner;
    private bool _highlightRetryPending;
    private bool _checkingDisk;

    private sealed record ClosedTab(
        string? FilePath, string Title, string Text, TextEncodingInfo Encoding,
        LineEnding LineEnding, int CaretIndex, bool WasDirty);

    public ObservableCollection<DocumentTab> Documents { get; } = new();
    public AppSettings Settings => AppSettings.Current;

    public MainWindow(SessionWindow? session)
    {
        // Commands first: they are plain properties with no change notification, so a
        // Command="{Binding ...}" evaluated during InitializeComponent would latch onto
        // null and never recover.
        BuildCommands();
        InitializeComponent();
        BuildInputBindings();

        _zoom = Settings.Zoom <= 0 ? 1.0 : Settings.Zoom;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _autosaveTimer.Tick += (_, _) => { _autosaveTimer.Stop(); App.PersistSession(); };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatus(); };

        Settings.PropertyChanged += OnSettingsChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;

        if (session is not null) RestoreSession(session);
        if (Documents.Count == 0) NewTab(activate: true);

        StatusBarRoot.Visibility = Settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;

        ApplyGutterVisibility();

        Drop += OnWindowDrop;
        DragOver += OnWindowDragOver;
        StateChanged += OnWindowStateChanged;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        // Polling on focus rather than watching the filesystem: it catches every case that
        // matters, costs one stat call, and cannot fire duplicate events off-thread.
        Activated += OnWindowActivated;
    }

    // ==================================================================== window chrome

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        NativeMethods.SetDarkTitleBar(hwnd, ThemeManager.IsDark);
        NativeMethods.TryRoundCorners(hwnd);
        NativeMethods.TryEnableMica(hwnd);
        UpdateMaximizeGlyph();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_GETMINMAXINFO:
                NativeMethods.ApplyMaxSizeToWorkArea(hwnd, lParam);
                handled = true;
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                // Fires when the user flips the system light/dark preference.
                if (Settings.Theme == AppTheme.System)
                    Dispatcher.BeginInvoke(() => ThemeManager.Apply(AppTheme.System), DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        RootBorder.BorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(0)
            : new Thickness(1);
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        MaxButton.Content = WindowState == WindowState.Maximized
            ? FindResource("Glyph.Restore")
            : FindResource("Glyph.Maximize");
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ==================================================================== commands

    public ICommand NewTabCommand { get; private set; } = null!;
    public ICommand NewWindowCommand { get; private set; } = null!;
    public ICommand OpenCommand { get; private set; } = null!;
    public ICommand SaveCommand { get; private set; } = null!;
    public ICommand SaveAsCommand { get; private set; } = null!;
    public ICommand SaveAllCommand { get; private set; } = null!;
    public ICommand CloseTabCommand { get; private set; } = null!;
    public ICommand CloseSpecificTabCommand { get; private set; } = null!;
    public ICommand CloseWindowCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand PrintCommand { get; private set; } = null!;

    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand RedoCommand { get; private set; } = null!;
    public ICommand CutCommand { get; private set; } = null!;
    public ICommand CopyCommand { get; private set; } = null!;
    public ICommand PasteCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand TimeDateCommand { get; private set; } = null!;

    public ICommand FindCommand { get; private set; } = null!;
    public ICommand ReplaceCommand { get; private set; } = null!;
    public ICommand FindNextCommand { get; private set; } = null!;
    public ICommand FindPreviousCommand { get; private set; } = null!;
    public ICommand ReplaceOneCommand { get; private set; } = null!;
    public ICommand ReplaceAllCommand { get; private set; } = null!;
    public ICommand GoToCommand { get; private set; } = null!;
    public ICommand EscapeCommand { get; private set; } = null!;

    public ICommand ZoomInCommand { get; private set; } = null!;
    public ICommand ZoomOutCommand { get; private set; } = null!;
    public ICommand ZoomResetCommand { get; private set; } = null!;
    public ICommand NextTabCommand { get; private set; } = null!;
    public ICommand PreviousTabCommand { get; private set; } = null!;
    public ICommand SettingsCommand { get; private set; } = null!;
    public ICommand ReopenClosedTabCommand { get; private set; } = null!;
    public ICommand TextToolCommand { get; private set; } = null!;

    private void BuildCommands()
    {
        NewTabCommand = new RelayCommand(() => NewTab(activate: true));
        NewWindowCommand = new RelayCommand(OpenNewWindow);
        OpenCommand = new RelayCommand(OpenWithDialog);
        SaveCommand = new RelayCommand(() => { if (ActiveTab is not null) SaveTab(ActiveTab, saveAs: false); });
        SaveAsCommand = new RelayCommand(() => { if (ActiveTab is not null) SaveTab(ActiveTab, saveAs: true); });
        SaveAllCommand = new RelayCommand(() => { foreach (var t in Documents.ToList()) if (t.IsDirty) SaveTab(t, false); });
        CloseTabCommand = new RelayCommand(() => { if (ActiveTab is not null) CloseTab(ActiveTab); });
        CloseSpecificTabCommand = new RelayCommand(p => { if (p is DocumentTab t) CloseTab(t); });
        CloseWindowCommand = new RelayCommand(Close);
        ExitCommand = new RelayCommand(() =>
        {
            // Snapshot every window first; closing them one by one would otherwise
            // shrink the session down to whatever closed last.
            App.PersistSession();
            App.SuspendPersist = true;
            foreach (var w in Application.Current.Windows.OfType<MainWindow>().ToList())
            {
                if (!w.Close2()) { App.SuspendPersist = false; App.PersistSession(); return; }
            }
        });
        PrintCommand = new RelayCommand(PrintActive);

        UndoCommand = new RelayCommand(() => ActiveTab?.Editor.Undo());
        RedoCommand = new RelayCommand(() => ActiveTab?.Editor.Redo());
        CutCommand = new RelayCommand(() => ActiveTab?.Editor.Cut());
        CopyCommand = new RelayCommand(() => ActiveTab?.Editor.Copy());
        PasteCommand = new RelayCommand(() => ActiveTab?.Editor.Paste());
        DeleteCommand = new RelayCommand(() =>
        {
            var ed = ActiveTab?.Editor;
            if (ed is null) return;
            if (ed.SelectionLength == 0)
            {
                if (ed.CaretIndex >= ed.Text.Length) return;
                ed.Select(ed.CaretIndex, 1);   // keeps the deletion on the undo stack
            }
            ed.SelectedText = "";
        });
        SelectAllCommand = new RelayCommand(() => ActiveTab?.Editor.SelectAll());
        TimeDateCommand = new RelayCommand(InsertTimeDate);

        FindCommand = new RelayCommand(() => ShowFindBar(replace: false));
        ReplaceCommand = new RelayCommand(() => ShowFindBar(replace: true));
        FindNextCommand = new RelayCommand(() => FindStep(up: false, fromCaret: true));
        FindPreviousCommand = new RelayCommand(() => FindStep(up: true, fromCaret: true));
        ReplaceOneCommand = new RelayCommand(ReplaceOne);
        ReplaceAllCommand = new RelayCommand(ReplaceAll);
        GoToCommand = new RelayCommand(GoToLine);
        // Guarded so Escape still reaches open menus and dialogs when the bar is hidden.
        EscapeCommand = new RelayCommand(HideFindBar, () => FindBar.Visibility == Visibility.Visible);

        ZoomInCommand = new RelayCommand(() => SetZoom(_zoom + 0.1));
        ZoomOutCommand = new RelayCommand(() => SetZoom(_zoom - 0.1));
        ZoomResetCommand = new RelayCommand(() => SetZoom(1.0));
        NextTabCommand = new RelayCommand(() => CycleTab(1));
        PreviousTabCommand = new RelayCommand(() => CycleTab(-1));
        SettingsCommand = new RelayCommand(OpenSettings);
        ReopenClosedTabCommand = new RelayCommand(ReopenClosedTab, () => _closedTabs.Count > 0);
        TextToolCommand = new RelayCommand(p => ApplyTextTool(p as string));
    }

    /// <summary>
    /// Shortcuts are matched during the tunnel, before the editor sees the key. TextBox
    /// swallows a few of these itself (Ctrl+H is backspace in Win32 edit controls), so
    /// waiting for the bubble to reach the window's InputBindings loses them.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        foreach (var binding in InputBindings.OfType<KeyBinding>())
        {
            if (binding.Key != e.Key || binding.Modifiers != mods) continue;
            if (binding.Command?.CanExecute(null) != true) continue;

            binding.Command.Execute(null);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void BuildInputBindings()
    {
        void Bind(ICommand command, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
            InputBindings.Add(new KeyBinding(command, key, modifiers));

        Bind(NewTabCommand, Key.N, ModifierKeys.Control);
        Bind(NewWindowCommand, Key.N, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(OpenCommand, Key.O, ModifierKeys.Control);
        Bind(SaveCommand, Key.S, ModifierKeys.Control);
        Bind(SaveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(SaveAllCommand, Key.S, ModifierKeys.Control | ModifierKeys.Alt);
        Bind(CloseTabCommand, Key.W, ModifierKeys.Control);
        Bind(CloseWindowCommand, Key.W, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(PrintCommand, Key.P, ModifierKeys.Control);

        Bind(FindCommand, Key.F, ModifierKeys.Control);
        Bind(ReplaceCommand, Key.H, ModifierKeys.Control);
        Bind(GoToCommand, Key.G, ModifierKeys.Control);
        Bind(FindNextCommand, Key.F3);
        Bind(FindPreviousCommand, Key.F3, ModifierKeys.Shift);
        Bind(TimeDateCommand, Key.F5);

        Bind(ZoomInCommand, Key.OemPlus, ModifierKeys.Control);
        Bind(ZoomInCommand, Key.Add, ModifierKeys.Control);
        Bind(ZoomOutCommand, Key.OemMinus, ModifierKeys.Control);
        Bind(ZoomOutCommand, Key.Subtract, ModifierKeys.Control);
        Bind(ZoomResetCommand, Key.D0, ModifierKeys.Control);
        Bind(ZoomResetCommand, Key.NumPad0, ModifierKeys.Control);

        Bind(NextTabCommand, Key.Tab, ModifierKeys.Control);
        Bind(PreviousTabCommand, Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(ReopenClosedTabCommand, Key.T, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(EscapeCommand, Key.Escape);
    }

    // ==================================================================== tabs

    public DocumentTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (ReferenceEquals(_activeTab, value)) return;

            if (_activeTab is not null)
            {
                _activeTab.IsActive = false;
                _activeTab.ScrollOffset = _activeTab.Editor.VerticalOffset;
            }

            _activeTab = value;

            DetachHighlighter();

            if (_activeTab is not null)
            {
                _activeTab.IsActive = true;
                EditorHost.Content = _activeTab.Editor;
                var tab = _activeTab;
                Dispatcher.BeginInvoke(() =>
                {
                    // Switching again before this ran would otherwise attach the gutter and
                    // a second adorner to an editor that is no longer on screen.
                    if (!ReferenceEquals(_activeTab, tab)) return;

                    tab.Editor.ScrollToVerticalOffset(tab.ScrollOffset);
                    tab.Editor.Focus();
                    Gutter.Attach(tab.Editor);
                    AttachHighlighter(tab.Editor);
                    RefreshHighlights();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                EditorHost.Content = null;
                Gutter.Attach(null);
            }

            ScrollActiveTabIntoView();

            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            UpdateStatus();
        }
    }

    public DocumentTab NewTab(bool activate, string? title = null)
    {
        var tab = new DocumentTab();
        if (title is not null) tab.Title = title;
        tab.Encoding = TextFileIo.ByName(Settings.DefaultEncoding);
        tab.LineEnding = Settings.DefaultLineEnding == "LF" ? LineEnding.Lf : LineEnding.Crlf;

        WireEditor(tab);
        tab.ApplyOptions(Settings, _zoom);
        tab.PropertyChanged += OnTabPropertyChanged;

        Documents.Add(tab);
        if (activate) ActiveTab = tab;
        SchedulePersist();
        return tab;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentTab.Title) or nameof(DocumentTab.IsDirty) or nameof(DocumentTab.FilePath))
            OnPropertyChanged(nameof(WindowTitle));
    }

    private void WireEditor(DocumentTab tab)
    {
        var ed = tab.Editor;
        // Plain template on purpose: the Fluent TextBox draws a focus underline and a
        // rounded fill that make no sense for a full-window editing surface.
        ed.Style = (Style)FindResource("EditorTextBoxStyle");
        ed.SetResourceReference(ForegroundProperty, "App.Text");
        ed.SetResourceReference(TextBoxBase.SelectionBrushProperty, "App.Selection");
        ed.SetResourceReference(TextBoxBase.CaretBrushProperty, "App.Text");
        ed.SelectionOpacity = 0.4;

        AutomationProperties.SetName(ed, "Document text");

        ed.SelectionChanged += (_, _) => ScheduleStatus();
        // Highlights are recomputed after layout: right on TextChanged the editor cannot
        // yet report which lines are visible.
        ed.TextChanged += (_, _) =>
        {
            ScheduleStatus();
            SchedulePersist();
            Dispatcher.BeginInvoke(RefreshHighlights, DispatcherPriority.Loaded);
        };
        ed.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RefreshHighlights()));
        ed.PreviewMouseWheel += Editor_PreviewMouseWheel;
        ed.PreviewKeyDown += Editor_PreviewKeyDown;
        ed.AllowDrop = true;
        ed.PreviewDragOver += OnEditorDragOver;
        ed.PreviewDrop += OnEditorDrop;
        ed.ContextMenu = BuildEditorContextMenu();
    }

    // ==================================================================== gutter & highlights

    private void ApplyGutterVisibility()
    {
        Gutter.Visibility = Settings.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;
        if (Settings.ShowLineNumbers) Gutter.Attach(ActiveTab?.Editor);
        Gutter.Refresh();
    }

    private void AttachHighlighter(TextBox editor)
    {
        var layer = AdornerLayer.GetAdornerLayer(editor);
        if (layer is null) return;

        _highlightAdorner = new MatchHighlightAdorner(editor);
        if (TryFindResource("App.FindHighlight") is Brush brush) _highlightAdorner.HighlightBrush = brush;
        layer.Add(_highlightAdorner);
    }

    private void DetachHighlighter()
    {
        if (_highlightAdorner is null) return;

        var layer = AdornerLayer.GetAdornerLayer(_highlightAdorner.AdornedElement);
        layer?.Remove(_highlightAdorner);
        _highlightAdorner = null;
    }

    /// <summary>
    /// Repaints the secondary match highlights. Only the visible character range is
    /// scanned, so this stays cheap on a large document.
    /// </summary>
    private void RefreshHighlights()
    {
        if (_highlightAdorner is null) return;

        var editor = ActiveTab?.Editor;
        string pattern = FindBox?.Text ?? "";

        if (editor is null || pattern.Length == 0 ||
            FindBar.Visibility != Visibility.Visible ||
            !Settings.HighlightAllMatches)
        {
            _highlightAdorner.Clear();
            return;
        }

        // A just-switched-to editor has no layout yet, so its line queries throw. Retry
        // once after layout rather than leaving the bar looking like there are no matches.
        int lineCount;
        try { lineCount = editor.LineCount; } catch (Exception) { lineCount = 0; }
        if (lineCount <= 0)
        {
            _highlightAdorner.Clear();
            if (!_highlightRetryPending)
            {
                _highlightRetryPending = true;
                Dispatcher.BeginInvoke(() => { _highlightRetryPending = false; RefreshHighlights(); },
                    DispatcherPriority.Background);
            }
            return;
        }

        try
        {
            int firstLine = editor.GetFirstVisibleLineIndex();
            int lastLine = editor.GetLastVisibleLineIndex();
            if (firstLine < 0 || lastLine < firstLine) { _highlightAdorner.Clear(); return; }
            if (firstLine >= lineCount) { _highlightAdorner.Clear(); return; }
            lastLine = Math.Min(lastLine, lineCount - 1);

            int from = editor.GetCharacterIndexFromLineIndex(firstLine);
            int lastStart = editor.GetCharacterIndexFromLineIndex(lastLine);
            if (from < 0 || lastStart < 0) { _highlightAdorner.Clear(); return; }

            int to = Math.Min(editor.Text.Length, lastStart + editor.GetLineLength(lastLine));
            var hits = DocumentSearch.FindInRange(editor.Text, pattern, CurrentSearchOptions, from, to - from);
            _highlightAdorner.SetHits(hits);
        }
        catch (Exception)
        {
            _highlightAdorner.Clear();
        }
    }

    private void CycleTab(int delta)
    {
        if (Documents.Count < 2 || ActiveTab is null) return;
        int i = Documents.IndexOf(ActiveTab);
        int next = (i + delta + Documents.Count) % Documents.Count;
        ActiveTab = Documents[next];
    }

    public void CloseTab(DocumentTab tab)
    {
        if (!ConfirmSave(tab)) return;
        RemoveTab(tab);

        if (Documents.Count == 0)
        {
            if (Application.Current.Windows.OfType<MainWindow>().Count() > 1) Close();
            else NewTab(activate: true);
        }
        SchedulePersist();
    }

    private void RemoveTab(DocumentTab tab)
    {
        int index = Documents.IndexOf(tab);
        if (index < 0) return;

        RememberClosedTab(tab);

        tab.PropertyChanged -= OnTabPropertyChanged;
        Documents.Remove(tab);
        SessionStore.DeleteBuffer(tab.Id);

        if (ReferenceEquals(ActiveTab, tab))
            ActiveTab = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
    }

    private void RememberClosedTab(DocumentTab tab)
    {
        string text = tab.Editor.Text;
        if (!tab.HasFile && text.Length == 0) return;   // nothing worth bringing back

        _closedTabs.Add(new ClosedTab(
            tab.FilePath, tab.Title, text, tab.Encoding, tab.LineEnding,
            tab.Editor.CaretIndex, tab.IsDirty));

        if (_closedTabs.Count > ClosedTabLimit) _closedTabs.RemoveAt(0);
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0) return;

        var closed = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);

        // A saved file that still exists is reopened from disk so it is current; an
        // unsaved buffer comes back exactly as it was.
        if (!closed.WasDirty && closed.FilePath is not null && File.Exists(closed.FilePath))
        {
            OpenFiles([closed.FilePath]);
            if (ActiveTab is not null)
                ActiveTab.Editor.CaretIndex = Math.Clamp(closed.CaretIndex, 0, ActiveTab.Editor.Text.Length);
            return;
        }

        var tab = NewTab(activate: true, title: closed.Title);
        tab.SetTextSilently(closed.Text);
        tab.Encoding = closed.Encoding;
        tab.LineEnding = closed.LineEnding;
        if (closed.FilePath is not null)
        {
            tab.FilePath = closed.FilePath;
            tab.Title = closed.Title;
            tab.MarkSynchronized();
        }
        tab.IsDirty = closed.WasDirty;
        tab.Editor.CaretIndex = Math.Clamp(closed.CaretIndex, 0, tab.Editor.Text.Length);
        SchedulePersist();
    }

    // ==================================================================== text tools

    /// <summary>
    /// Applies a transform to the selection, or to the whole document when nothing is
    /// selected. Going through SelectedText keeps every one of these undoable in one step.
    /// </summary>
    private void ApplyTextTool(string? tool)
    {
        var tab = ActiveTab;
        var ed = tab?.Editor;
        if (tab is null || ed is null || tool is null) return;
        if (!EnsureWritable(tab)) return;

        if (tool == "path")
        {
            ed.SelectedText = tab.FilePath ?? tab.Title;
            ed.CaretIndex = ed.SelectionStart + ed.SelectionLength;
            ed.SelectionLength = 0;
            ed.Focus();
            return;
        }

        bool wholeDocument = ed.SelectionLength == 0;
        string input = wholeDocument ? ed.Text : ed.SelectedText;
        if (input.Length == 0) return;

        string output = tool switch
        {
            "sort" => TextTools.SortLines(input),
            "sortdesc" => TextTools.SortLines(input, descending: true),
            "reverse" => TextTools.ReverseLines(input),
            "dedupe" => TextTools.RemoveDuplicateLines(input),
            "noempty" => TextTools.RemoveEmptyLines(input),
            "trim" => TextTools.TrimTrailingWhitespace(input),
            "join" => TextTools.JoinLines(input),
            "upper" => TextTools.ToUpper(input),
            "lower" => TextTools.ToLower(input),
            "title" => TextTools.ToTitleCase(input),
            "toggle" => TextTools.ToggleCase(input),
            _ => input
        };

        if (output == input) return;

        if (wholeDocument)
        {
            int caret = ed.CaretIndex;
            ed.SelectAll();
            ed.SelectedText = output;
            ed.CaretIndex = Math.Clamp(caret, 0, ed.Text.Length);
            ed.SelectionLength = 0;
        }
        else
        {
            int at = ed.SelectionStart;
            ed.SelectedText = output;
            ed.Select(at, output.Length);
        }

        ed.Focus();
    }

    /// <summary>Returns false when the user cancels out of the save prompt.</summary>
    private bool ConfirmSave(DocumentTab tab)
    {
        if (!tab.IsDirty || !Settings.AskToSaveOnClose) return true;
        if (!tab.HasFile && tab.Editor.Text.Length == 0) return true;

        var result = MessageBox.Show(
            $"Do you want to save changes to {tab.Title}?",
            AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                return SaveTab(tab, saveAs: false);

            case MessageBoxResult.No:
                // Honour "don't save": drop the recovery buffer so it does not come back.
                tab.Discarded = true;
                tab.IsDirty = false;
                SessionStore.DeleteBuffer(tab.Id);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Close, reporting false when the user cancelled out of it.</summary>
    private bool Close2()
    {
        Close();
        return _closing;
    }

    // ==================================================================== file io

    private void OpenWithDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open",
            Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true) OpenFiles(dlg.FileNames);
    }

    public void OpenFiles(IEnumerable<string> paths)
    {
        DocumentTab? last = null;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            var full = Path.GetFullPath(path);

            var existing = Documents.FirstOrDefault(
                d => d.FilePath is not null && string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { last = existing; continue; }

            if (!File.Exists(full))
            {
                var create = MessageBox.Show(
                    $"{Path.GetFileName(full)} was not found.\n\nCreate a new file?",
                    AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (create != MessageBoxResult.Yes) continue;

                var blank = NewTab(activate: false, title: Path.GetFileName(full));
                blank.FilePath = full;
                last = blank;
                continue;
            }

            if (!ConfirmLargeFile(full)) continue;

            try
            {
                var loaded = TextFileIo.Load(full);
                var target = ReusableBlankTab() ?? NewTab(activate: false);
                target.SetTextSilently(loaded.Text);
                target.Encoding = loaded.Encoding;
                target.LineEnding = loaded.LineEnding;
                target.FilePath = full;
                target.Title = Path.GetFileName(full);
                target.MarkSynchronized();
                last = target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                MessageBox.Show($"Cannot open {Path.GetFileName(full)}:\n\n{ex.Message}",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (last is not null) ActiveTab = last;
        SchedulePersist();
    }

    /// <summary>
    /// The editor is a plain TextBox, which holds the whole document in one run. Past a
    /// few tens of megabytes typing becomes visibly slow, so say so before opening rather
    /// than after the window has frozen.
    /// </summary>
    private bool ConfirmLargeFile(string path)
    {
        int limitMb = Settings.LargeFileWarningMb;
        if (limitMb <= 0) return true;

        try
        {
            long bytes = new FileInfo(path).Length;
            if (bytes < limitMb * 1024L * 1024L) return true;

            double mb = bytes / (1024.0 * 1024.0);
            var answer = MessageBox.Show(
                $"{Path.GetFileName(path)} is {mb:N0} MB.\n\n" +
                "NotepadX keeps the whole file in memory, so editing a file this large will be slow.\n\n" +
                "Open it anyway?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return answer == MessageBoxResult.Yes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Blocks edits to a file marked read-only, offering to clear the attribute. Returns
    /// false when the caller should abandon the change.
    /// </summary>
    private bool EnsureWritable(DocumentTab tab)
    {
        if (!tab.IsReadOnly || tab.FilePath is null) return true;

        var answer = MessageBox.Show(
            $"{tab.Title} is marked read-only.\n\nRemove the read-only attribute and continue?",
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return false;

        try
        {
            var info = new FileInfo(tab.FilePath) { IsReadOnly = false };
            tab.IsReadOnly = info.IsReadOnly;
            return !tab.IsReadOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Cannot clear the read-only attribute:\n\n{ex.Message}",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// Compares the file on disk against what this tab last read or wrote. Runs when the
    /// window regains focus, which is when a change made elsewhere becomes relevant.
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_checkingDisk || _closing) return;

        _checkingDisk = true;
        try
        {
            foreach (var tab in Documents.ToList())
            {
                if (tab.ReloadPromptOpen || !tab.HasChangedOnDisk()) continue;
                HandleExternalChange(tab);
            }
        }
        finally
        {
            _checkingDisk = false;
        }
    }

    private void HandleExternalChange(DocumentTab tab)
    {
        if (tab.FilePath is null) return;

        tab.ReloadPromptOpen = true;
        try
        {
            if (!tab.IsDirty)
            {
                // Nothing of the user's to lose, so take the newer content.
                ReloadFromDisk(tab);
                return;
            }

            var answer = MessageBox.Show(
                $"{tab.Title} was changed by another program, and this tab has unsaved edits.\n\n" +
                "Reload from disk and discard your changes?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes) ReloadFromDisk(tab);
            else tab.MarkSynchronized();   // user keeps their copy; stop asking about this version
        }
        finally
        {
            tab.ReloadPromptOpen = false;
        }
    }

    private void ReloadFromDisk(DocumentTab tab)
    {
        if (tab.FilePath is null) return;
        try
        {
            int caret = tab.Editor.CaretIndex;
            double scroll = tab.Editor.VerticalOffset;

            var loaded = TextFileIo.Load(tab.FilePath);
            tab.SetTextSilently(loaded.Text);
            tab.Encoding = loaded.Encoding;
            tab.LineEnding = loaded.LineEnding;
            tab.MarkSynchronized();

            tab.Editor.CaretIndex = Math.Clamp(caret, 0, tab.Editor.Text.Length);
            tab.Editor.ScrollToVerticalOffset(scroll);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Cannot reload {tab.Title}:\n\n{ex.Message}",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>An untouched "Untitled" tab is recycled instead of piling up empties.</summary>
    private DocumentTab? ReusableBlankTab()
    {
        if (Documents.Count != 1) return null;
        var only = Documents[0];
        return !only.HasFile && !only.IsDirty && only.Editor.Text.Length == 0 ? only : null;
    }

    private bool SaveTab(DocumentTab tab, bool saveAs)
    {
        string? path = tab.FilePath;

        if (saveAs || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Title = saveAs ? "Save as" : "Save",
                Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = tab.HasFile ? Path.GetFileName(tab.FilePath!) : tab.Title,
                DefaultExt = ".txt",
                AddExtension = true
            };
            if (tab.HasFile)
            {
                var dir = Path.GetDirectoryName(tab.FilePath!);
                if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
            }
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        else
        {
            if (!EnsureWritable(tab)) return false;
            if (!ConfirmOverwriteExternalChange(tab)) return false;
        }

        var encoding = tab.Encoding;
        if (!ConfirmEncoding(tab, ref encoding)) return false;

        try
        {
            TextFileIo.Save(path!, tab.Editor.Text, encoding, tab.LineEnding);
            tab.Encoding = encoding;
            tab.FilePath = path;
            tab.Title = Path.GetFileName(path!);
            tab.IsDirty = false;
            tab.MarkSynchronized();
            SessionStore.DeleteBuffer(tab.Id);
            SchedulePersist();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show($"Cannot save {Path.GetFileName(path)}:\n\n{ex.Message}",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// ANSI and other legacy code pages cannot represent most of Unicode, and the default
    /// encoder quietly substitutes '?'. Saying so is the difference between a warning and
    /// silent data loss.
    /// </summary>
    private bool ConfirmEncoding(DocumentTab tab, ref TextEncodingInfo encoding)
    {
        if (TextFileIo.CanEncode(tab.Editor.Text, encoding, out int count, out string sample)) return true;

        string detail = string.IsNullOrEmpty(sample) ? "" : $"\n\nFor example: {sample}";
        var answer = MessageBox.Show(
            $"{count:N0} character{(count == 1 ? "" : "s")} cannot be written as {encoding.Name} " +
            $"and would be replaced with '?'.{detail}\n\n" +
            "Save as UTF-8 instead, keeping every character?\n\n" +
            "Yes — save as UTF-8\n" +
            "No — save as " + encoding.Name + " and lose them\n" +
            "Cancel — go back",
            AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                encoding = TextFileIo.Utf8;
                return true;
            case MessageBoxResult.No:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Last line of defence against clobbering an edit made by another program.</summary>
    private bool ConfirmOverwriteExternalChange(DocumentTab tab)
    {
        if (!tab.HasChangedOnDisk()) return true;

        var answer = MessageBox.Show(
            $"{tab.Title} was changed by another program since you opened it.\n\n" +
            "Saving now overwrites those changes. Continue?",
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);

        return answer == MessageBoxResult.Yes;
    }

    private void OpenNewWindow()
    {
        var w = new MainWindow(null);
        w.Show();
        w.Activate();
        SchedulePersist();
    }

    // ==================================================================== drag & drop

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenFiles(files);
        e.Handled = true;
    }

    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenFiles(files);
        e.Handled = true;
    }

    // ==================================================================== editor input

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(_zoom + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox ed) return;

        if (e.Key == Key.Enter && Settings.AutoIndent && Keyboard.Modifiers == ModifierKeys.None)
        {
            string text = ed.Text;
            int caret = ed.CaretIndex;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (caret > 0 && lineStart <= caret)
            {
                int i = lineStart;
                while (i < caret && (text[i] == ' ' || text[i] == '\t')) i++;
                string indent = text[lineStart..i];
                if (indent.Length > 0)
                {
                    ed.SelectedText = Environment.NewLine + indent;
                    ed.CaretIndex = ed.SelectionStart + ed.SelectionLength;
                    ed.SelectionLength = 0;
                    e.Handled = true;
                }
            }
        }
    }

    private void InsertTimeDate()
    {
        var ed = ActiveTab?.Editor;
        if (ed is null) return;
        string stamp = DateTime.Now.ToString("t", CultureInfo.CurrentCulture) + " " +
                       DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        ed.SelectedText = stamp;
        ed.CaretIndex = ed.SelectionStart + ed.SelectionLength;
        ed.SelectionLength = 0;
        ed.Focus();
    }

    // ==================================================================== zoom

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(Math.Round(value, 2), 0.2, 5.0);
        foreach (var tab in Documents) tab.ApplyOptions(Settings, _zoom);
        Settings.Zoom = _zoom;
        Gutter.Refresh();
        RefreshHighlights();
        OnPropertyChanged(nameof(StatusZoom));
    }

    private void ZoomStatus_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewContextMenu((FrameworkElement)sender);
        foreach (var pct in new[] { 50, 75, 100, 125, 150, 200, 300 })
        {
            var item = new MenuItem
            {
                Header = pct + "%",
                IsCheckable = true,
                IsChecked = Math.Abs(_zoom * 100 - pct) < 0.5
            };
            item.Click += (_, _) => SetZoom(pct / 100.0);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ==================================================================== status bar

    private void ScheduleStatus()
    {
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public string StatusLeft
    {
        get => _statusLeft;
        private set { _statusLeft = value; OnPropertyChanged(); }
    }

    public string StatusPosition
    {
        get => _statusPosition;
        private set { _statusPosition = value; OnPropertyChanged(); }
    }

    public string StatusZoom => (int)Math.Round(_zoom * 100) + "%";

    public string WindowTitle
    {
        get
        {
            if (ActiveTab is null) return AppName;
            return (ActiveTab.IsDirty ? "*" : "") + ActiveTab.Title + " - " + AppName;
        }
    }

    private void UpdateStatus()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            StatusLeft = "";
            StatusPosition = "Ln 1, Col 1";
            return;
        }

        var ed = tab.Editor;
        string text = ed.Text;
        int caret = Math.Clamp(ed.CaretIndex, 0, text.Length);

        int line = 1, lineStart = 0;
        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        }
        int col = caret - lineStart + 1;

        StatusPosition = $"Ln {line}, Col {col}";

        var status = new StringBuilder();
        status.Append(ed.SelectionLength > 0
            ? $"{ed.SelectionLength:N0} selected of {text.Length:N0} characters"
            : $"{text.Length:N0} characters");

        if (Settings.ShowWordCount)
        {
            string counted = ed.SelectionLength > 0 ? ed.SelectedText : text;
            status.Append($"   ·   {TextTools.CountWords(counted):N0} words");
        }

        if (tab.IsReadOnly) status.Append("   ·   Read-only");

        StatusLeft = status.ToString();

        OnPropertyChanged(nameof(WindowTitle));
    }

    private void LineEndingStatus_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        var menu = NewContextMenu((FrameworkElement)sender);
        foreach (var eol in new[] { LineEnding.Crlf, LineEnding.Lf, LineEnding.Cr })
        {
            var item = new MenuItem
            {
                Header = TextFileIo.Label(eol),
                IsCheckable = true,
                IsChecked = ActiveTab.LineEnding == eol
            };
            item.Click += (_, _) =>
            {
                if (ActiveTab is null || ActiveTab.LineEnding == eol) return;
                ActiveTab.LineEnding = eol;
                ActiveTab.IsDirty = true;
                SchedulePersist();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void EncodingStatus_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        var menu = NewContextMenu((FrameworkElement)sender);
        foreach (var enc in TextFileIo.All)
        {
            var item = new MenuItem
            {
                Header = enc.Name,
                IsCheckable = true,
                IsChecked = ActiveTab.Encoding.Name == enc.Name
            };
            item.Click += (_, _) =>
            {
                if (ActiveTab is null || ActiveTab.Encoding.Name == enc.Name) return;
                ActiveTab.Encoding = enc;
                ActiveTab.IsDirty = true;
                SchedulePersist();
            };
            menu.Items.Add(item);
        }

        if (ActiveTab.HasFile)
        {
            menu.Items.Add(new Separator());
            var reopen = new MenuItem { Header = "Reopen with encoding..." };
            foreach (var enc in TextFileIo.All)
            {
                var sub = new MenuItem { Header = enc.Name };
                sub.Click += (_, _) => ReopenWithEncoding(enc);
                reopen.Items.Add(sub);
            }
            menu.Items.Add(reopen);
        }

        menu.IsOpen = true;
    }

    private void ReopenWithEncoding(TextEncodingInfo enc)
    {
        var tab = ActiveTab;
        if (tab?.FilePath is null) return;
        try
        {
            var bytes = File.ReadAllBytes(tab.FilePath);
            int skip = 0;
            var preamble = enc.Encoding.GetPreamble();
            if (preamble.Length > 0 && bytes.Length >= preamble.Length &&
                bytes.Take(preamble.Length).SequenceEqual(preamble))
                skip = preamble.Length;

            string text = enc.Encoding.GetString(bytes, skip, bytes.Length - skip);
            tab.SetTextSilently(TextFileIo.Normalize(text));
            tab.Encoding = enc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ContextMenu NewContextMenu(FrameworkElement placement)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("AppContextMenuStyle"),
            PlacementTarget = placement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };
        return menu;
    }

    // ==================================================================== find & replace

    private void ShowFindBar(bool replace)
    {
        bool wasHidden = FindBar.Visibility != Visibility.Visible;

        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;

        var ed = ActiveTab?.Editor;
        if (ed is not null && ed.SelectionLength > 0 && !ed.SelectedText.Contains('\n'))
            FindBox.Text = ed.SelectedText;

        if (wasHidden) AnimateFindBarIn();

        FindBox.Focus();
        FindBox.SelectAll();
        UpdateMatchCount();
    }

    /// <summary>Short slide-and-fade so the bar does not just pop into place.</summary>
    private void AnimateFindBarIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(160));

        FindBar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        FindBarSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, duration) { EasingFunction = ease });
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ActiveTab?.Editor.Focus();
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFindBar();

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindStep(up: Keyboard.Modifiers == ModifierKeys.Shift, fromCaret: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideFindBar();
            e.Handled = true;
        }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Incremental search keeps focus in the box, the selection is still painted
        // because the editor keeps inactive-selection highlighting on.
        FindStep(up: false, fromCaret: false, keepFocus: true);
        UpdateMatchCount();
    }

    private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ReplaceOne(); e.Handled = true; }
        else if (e.Key == Key.Escape) { HideFindBar(); e.Handled = true; }
    }

    private bool WrapAround => WrapAroundToggle.IsChecked == true;

    private SearchOptions CurrentSearchOptions => new(
        MatchCase: MatchCaseToggle.IsChecked == true,
        WholeWord: WholeWordToggle.IsChecked == true,
        UseRegex: RegexToggle.IsChecked == true);

    private void SearchOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateMatchCount();
        RefreshHighlights();
    }

    private void FindHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = Settings.FindHistory;
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("AppContextMenuStyle"),
            PlacementTarget = (FrameworkElement)sender,
            Placement = PlacementMode.Bottom
        };

        if (history.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No recent searches", IsEnabled = false });
        }
        else
        {
            foreach (var term in history)
            {
                var item = new MenuItem { Header = term };
                item.Click += (_, _) =>
                {
                    FindBox.Text = term;
                    FindBox.CaretIndex = term.Length;
                    FindBox.Focus();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear history" };
            clear.Click += (_, _) => { Settings.FindHistory = []; Settings.Save(); };
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    private void FindStep(bool up, bool fromCaret, bool keepFocus = false)
    {
        var ed = ActiveTab?.Editor;
        string pattern = FindBox.Text;
        if (ed is null || pattern.Length == 0) return;

        var options = CurrentSearchOptions;
        if (DocumentSearch.TryBuild(pattern, options, out string? error) is null)
        {
            FindStatus.Text = error is null ? "" : "Invalid pattern";
            _highlightAdorner?.Clear();
            return;
        }

        string text = ed.Text;
        int start = fromCaret
            ? (up ? ed.SelectionStart : ed.SelectionStart + Math.Max(1, ed.SelectionLength))
            : ed.SelectionStart;

        var hit = DocumentSearch.FindWrapped(text, pattern, start, up, options, WrapAround);
        if (hit is null)
        {
            FindStatus.Text = "No results";
            return;
        }

        ed.Select(hit.Value.Index, hit.Value.Length);
        ScrollToCharacter(ed, hit.Value.Index);
        if (!keepFocus) ed.Focus();

        Settings.RememberSearch(pattern);
        UpdateMatchCount();
        RefreshHighlights();
    }

    private void UpdateMatchCount()
    {
        var ed = ActiveTab?.Editor;
        string pattern = FindBox.Text;
        if (ed is null || pattern.Length == 0) { FindStatus.Text = ""; return; }

        var options = CurrentSearchOptions;
        if (DocumentSearch.TryBuild(pattern, options, out _) is null)
        {
            FindStatus.Text = "Invalid pattern";
            return;
        }

        int count = DocumentSearch.Count(ed.Text, pattern, options);
        FindStatus.Text = count == 0 ? "No results" : count + (count == 1 ? " result" : " results");
    }

    private void ReplaceOne()
    {
        var ed = ActiveTab?.Editor;
        string pattern = FindBox.Text;
        if (ed is null || pattern.Length == 0) return;
        if (!EnsureWritable(ActiveTab!)) return;

        var options = CurrentSearchOptions;
        var hit = DocumentSearch.Find(ed.Text, pattern, ed.SelectionStart, backwards: false, options);

        // Only replace when the selection is exactly the match under the caret; otherwise
        // this acts as "find next" so the first press never overwrites something else.
        if (hit is { } current && current.Index == ed.SelectionStart && current.Length == ed.SelectionLength)
        {
            string replacement = DocumentSearch.ExpandReplacement(ed.Text, current, pattern, ReplaceBox.Text, options);
            int at = ed.SelectionStart;
            ed.SelectedText = replacement;
            ed.Select(at + replacement.Length, 0);
        }

        Settings.RememberSearch(pattern);
        FindStep(up: false, fromCaret: true, keepFocus: true);
    }

    private void ReplaceAll()
    {
        var ed = ActiveTab?.Editor;
        string pattern = FindBox.Text;
        if (ed is null || pattern.Length == 0) return;
        if (!EnsureWritable(ActiveTab!)) return;

        var options = CurrentSearchOptions;
        if (DocumentSearch.TryBuild(pattern, options, out _) is null)
        {
            FindStatus.Text = "Invalid pattern";
            return;
        }

        string result = DocumentSearch.ReplaceAll(ed.Text, pattern, ReplaceBox.Text, options, out int replaced);

        if (replaced > 0)
        {
            int caret = ed.CaretIndex;
            ed.SelectAll();
            ed.SelectedText = result;
            ed.CaretIndex = Math.Min(caret, ed.Text.Length);
            ed.SelectionLength = 0;
        }

        Settings.RememberSearch(pattern);
        FindStatus.Text = replaced == 0 ? "No results" : $"Replaced {replaced}";
        RefreshHighlights();
    }

    private void GoToLine()
    {
        var ed = ActiveTab?.Editor;
        if (ed is null) return;

        string text = ed.Text;
        int totalLines = 1;
        foreach (char c in text) if (c == '\n') totalLines++;

        var dlg = new InputDialog($"Line number (1 - {totalLines}):", "Go to line", "")
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(dlg.Value.Trim(), out int target)) return;

        GoToLine(target);
    }

    /// <summary>Moves the caret to a 1-based line. Also used by the <c>file:42</c> argument.</summary>
    public void GoToLine(int target)
    {
        var ed = ActiveTab?.Editor;
        if (ed is null) return;

        string text = ed.Text;
        int totalLines = 1;
        foreach (char c in text) if (c == '\n') totalLines++;

        target = Math.Clamp(target, 1, totalLines);

        int index = 0, line = 1;
        while (line < target && index < text.Length)
        {
            int nl = text.IndexOf('\n', index);
            if (nl < 0) break;
            index = nl + 1;
            line++;
        }

        ed.CaretIndex = index;
        ed.SelectionLength = 0;
        ScrollToCharacter(ed, index);
        ed.Focus();
    }

    /// <summary>
    /// Scrolls a character offset into view. A freshly hosted editor has not been laid out
    /// yet and its line queries throw, so the scroll is retried once after layout.
    /// </summary>
    private void ScrollToCharacter(TextBox ed, int index)
    {
        if (!TryScrollToCharacter(ed, index))
            Dispatcher.BeginInvoke(() => TryScrollToCharacter(ed, index), DispatcherPriority.Loaded);
    }

    private static bool TryScrollToCharacter(TextBox ed, int index)
    {
        try
        {
            if (ed.LineCount <= 0) return false;
            int line = ed.GetLineIndexFromCharacterIndex(Math.Clamp(index, 0, ed.Text.Length));
            ed.ScrollToLine(Math.Clamp(line, 0, ed.LineCount - 1));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens files, honouring any <c>:line</c> suffix. Returns the tab of the last file.
    /// </summary>
    public void OpenRequests(IReadOnlyList<FileRequest> requests)
    {
        foreach (var request in requests)
        {
            OpenFiles([request.Path]);
            if (request.Line is int line) GoToLine(line);
        }
    }

    // ==================================================================== printing

    private void PrintActive() => PrintTab(ActiveTab, prompt: true);

    /// <summary>Prints a tab. With <paramref name="prompt"/> false it goes to the default printer.</summary>
    public void PrintTab(DocumentTab? tab, bool prompt)
    {
        if (tab is null) return;

        var dlg = new PrintDialog();
        if (prompt && dlg.ShowDialog() != true) return;

        var doc = new FlowDocument(new Paragraph(new Run(tab.Editor.Text)))
        {
            FontFamily = tab.Editor.FontFamily,
            FontSize = Settings.FontSize * 96.0 / 72.0,
            PagePadding = new Thickness(48),
            PageWidth = dlg.PrintableAreaWidth,
            PageHeight = dlg.PrintableAreaHeight
        };
        doc.ColumnWidth = doc.PageWidth;   // one column, never newspaper-style splits

        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, tab.Title);
    }

    // ==================================================================== context menus

    private ContextMenu BuildEditorContextMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenuStyle") };

        void Add(string header, ICommand command, string gesture = "")
        {
            menu.Items.Add(new MenuItem { Header = header, Command = command, InputGestureText = gesture });
        }

        Add("Undo", UndoCommand, "Ctrl+Z");
        Add("Redo", RedoCommand, "Ctrl+Y");
        menu.Items.Add(new Separator());
        Add("Cut", CutCommand, "Ctrl+X");
        Add("Copy", CopyCommand, "Ctrl+C");
        Add("Paste", PasteCommand, "Ctrl+V");
        Add("Delete", DeleteCommand, "Del");
        menu.Items.Add(new Separator());
        Add("Select all", SelectAllCommand, "Ctrl+A");
        Add("Find", FindCommand, "Ctrl+F");
        Add("Go to...", GoToCommand, "Ctrl+G");
        return menu;
    }

    private void ShowTabContextMenu(DocumentTab tab, FrameworkElement anchor)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("AppContextMenuStyle"),
            PlacementTarget = anchor
        };

        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Close tab", () => CloseTab(tab));
        Add("Close other tabs", () =>
        {
            foreach (var other in Documents.Where(d => d != tab).ToList())
            {
                if (!ConfirmSave(other)) return;
                RemoveTab(other);
            }
            SchedulePersist();
        }, Documents.Count > 1);
        Add("Close tabs to the right", () =>
        {
            int i = Documents.IndexOf(tab);
            foreach (var other in Documents.Skip(i + 1).ToList())
            {
                if (!ConfirmSave(other)) return;
                RemoveTab(other);
            }
            SchedulePersist();
        }, Documents.IndexOf(tab) < Documents.Count - 1);

        menu.Items.Add(new Separator());
        Add("Rename...", () => RenameTab(tab), tab.HasFile);
        Add("Copy full path", () =>
        {
            try { Clipboard.SetText(tab.FilePath ?? tab.Title); } catch (System.Runtime.InteropServices.COMException) { }
        }, tab.HasFile);
        Add("Open containing folder", () =>
        {
            if (tab.FilePath is null) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{tab.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }, tab.HasFile);

        menu.IsOpen = true;
    }

    private void RenameTab(DocumentTab tab)
    {
        if (tab.FilePath is null) return;

        var dlg = new InputDialog("New file name:", "Rename", Path.GetFileName(tab.FilePath)) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        string newName = dlg.Value.Trim();
        if (newName.Length == 0 || newName == Path.GetFileName(tab.FilePath)) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("That name contains characters a file name cannot use.",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dir = Path.GetDirectoryName(tab.FilePath)!;
        string target = Path.Combine(dir, newName);
        try
        {
            File.Move(tab.FilePath, target);
            tab.FilePath = target;
            tab.Title = newName;
            SchedulePersist();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==================================================================== tab strip mouse

    private static DocumentTab? TabFromPoint(ItemsControl list, Point p)
    {
        var hit = list.InputHitTest(p) as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement fe && fe.DataContext is DocumentTab t) return t;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private void TabList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tab = TabFromPoint(TabList, e.GetPosition(TabList));
        if (tab is null) return;
        ActiveTab = tab;
        _dragStart = e.GetPosition(this);
        _dragTab = tab;
    }

    private void TabList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _dragStart.X) < 12) return;

        var over = TabFromPoint(TabList, e.GetPosition(TabList));
        if (over is null || ReferenceEquals(over, _dragTab)) return;

        int from = Documents.IndexOf(_dragTab);
        int to = Documents.IndexOf(over);
        if (from >= 0 && to >= 0 && from != to)
        {
            Documents.Move(from, to);
            _dragStart = now;
            SchedulePersist();
        }
    }

    private void TabList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragTab = null;

    private TabStripPanel? _tabStrip;

    private void TabStrip_Loaded(object sender, RoutedEventArgs e)
    {
        _tabStrip = (TabStripPanel)sender;
        UpdateTabStripWidth();
    }

    private void TabScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabStripWidth();

    /// <summary>
    /// Hands the strip the width it may divide between the tabs. The ScrollViewer's own width
    /// comes from the title-bar grid and never grows with its content, so this cannot feed back
    /// into itself the way binding to the viewport does.
    /// </summary>
    private void UpdateTabStripWidth()
    {
        if (_tabStrip is not null) _tabStrip.AvailableWidth = TabScroller.ActualWidth;
    }

    private void TabScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only claim the wheel when the strip actually overflows; otherwise let it bubble.
        if (TabScroller.ScrollableWidth <= 0) return;
        TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// Scrolls the active tab back into view. Tabs stop shrinking at a minimum width, so a
    /// strip with enough tabs on it overflows and the active one can sit off-screen.
    /// </summary>
    private void ScrollActiveTabIntoView()
    {
        var tab = _activeTab;
        if (tab is null) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_activeTab, tab)) return;
            if (TabList.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
                container.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void TabList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var tab = TabFromPoint(TabList, e.GetPosition(TabList));
        if (tab is null) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            CloseTab(tab);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            ActiveTab = tab;
            ShowTabContextMenu(tab, TabList);
            e.Handled = true;
        }
    }

    // ==================================================================== settings & theme

    private void OpenSettings()
    {
        var dlg = new SettingsWindow { Owner = this };
        dlg.ShowDialog();
        Settings.Save();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(Settings.Theme);
                OnPropertyChanged(nameof(IsThemeSystem));
                OnPropertyChanged(nameof(IsThemeLight));
                OnPropertyChanged(nameof(IsThemeDark));
                break;

            case nameof(AppSettings.ShowStatusBar):
                StatusBarRoot.Visibility = Settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AppSettings.ShowLineNumbers):
                ApplyGutterVisibility();
                break;

            case nameof(AppSettings.ShowWordCount):
                UpdateStatus();
                break;

            case nameof(AppSettings.HighlightAllMatches):
                RefreshHighlights();
                break;

            case nameof(AppSettings.FindHistory):
                return;   // written straight to disk below without touching the editors

            case nameof(AppSettings.Zoom):
                return;   // already applied by SetZoom, and it is written out on close

            default:
                foreach (var tab in Documents) tab.ApplyOptions(Settings, _zoom);
                Gutter.Refresh();
                break;
        }
        Settings.Save();
    }

    private void OnThemeChanged()
    {
        NativeMethods.SetDarkTitleBar(this, ThemeManager.IsDark);
    }

    public bool IsThemeSystem
    {
        get => Settings.Theme == AppTheme.System;
        set { if (value) Settings.Theme = AppTheme.System; }
    }

    public bool IsThemeLight
    {
        get => Settings.Theme == AppTheme.Light;
        set { if (value) Settings.Theme = AppTheme.Light; }
    }

    public bool IsThemeDark
    {
        get => Settings.Theme == AppTheme.Dark;
        set { if (value) Settings.Theme = AppTheme.Dark; }
    }

    // ==================================================================== session

    private void SchedulePersist()
    {
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    public SessionWindow CaptureSession()
    {
        var bounds = RestoreBounds.IsEmpty
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var sw = new SessionWindow
        {
            ActiveIndex = ActiveTab is null ? 0 : Math.Max(0, Documents.IndexOf(ActiveTab)),
            Maximized = WindowState == WindowState.Maximized,
            Left = double.IsNaN(bounds.Left) ? double.NaN : bounds.Left,
            Top = double.IsNaN(bounds.Top) ? double.NaN : bounds.Top,
            Width = double.IsNaN(bounds.Width) || bounds.Width < 200 ? 1040 : bounds.Width,
            Height = double.IsNaN(bounds.Height) || bounds.Height < 150 ? 720 : bounds.Height
        };

        foreach (var tab in Documents)
        {
            if (tab.Discarded) continue;

            bool keepBuffer = tab.IsDirty || !tab.HasFile;
            if (keepBuffer && tab.Editor.Text.Length > 0)
                SessionStore.WriteBuffer(tab.Id, tab.Editor.Text);
            else
                SessionStore.DeleteBuffer(tab.Id);

            sw.Tabs.Add(new SessionTab
            {
                Id = tab.Id,
                FilePath = tab.FilePath,
                Title = tab.Title,
                HasUnsavedBuffer = keepBuffer && tab.Editor.Text.Length > 0,
                EncodingName = tab.Encoding.Name,
                LineEnding = tab.LineEnding,
                CaretIndex = tab.Editor.CaretIndex,
                ScrollOffset = ReferenceEquals(tab, ActiveTab) ? tab.Editor.VerticalOffset : tab.ScrollOffset
            });
        }

        return sw;
    }

    private void RestoreSession(SessionWindow sw)
    {
        if (!double.IsNaN(sw.Left) && !double.IsNaN(sw.Top) && sw.Width > 200 && sw.Height > 150)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = sw.Left;
            Top = sw.Top;
            Width = sw.Width;
            Height = sw.Height;
            EnsureOnScreen();
        }
        if (sw.Maximized) WindowState = WindowState.Maximized;

        foreach (var st in sw.Tabs)
        {
            var tab = new DocumentTab { Id = st.Id };
            WireEditor(tab);
            tab.PropertyChanged += OnTabPropertyChanged;
            tab.Encoding = TextFileIo.ByName(st.EncodingName);
            tab.LineEnding = st.LineEnding;
            tab.Title = st.Title;

            string? buffer = st.HasUnsavedBuffer ? SessionStore.ReadBuffer(st.Id) : null;

            if (buffer is not null)
            {
                tab.SetTextSilently(buffer);
                if (st.FilePath is not null) tab.FilePath = st.FilePath;
                tab.IsDirty = true;
            }
            else if (st.FilePath is not null && File.Exists(st.FilePath))
            {
                try
                {
                    var loaded = TextFileIo.Load(st.FilePath);
                    tab.SetTextSilently(loaded.Text);
                    tab.Encoding = loaded.Encoding;
                    tab.LineEnding = loaded.LineEnding;
                    tab.FilePath = st.FilePath;
                    tab.MarkSynchronized();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;   // file went away or is locked; drop the tab rather than fail startup
                }
            }
            else if (st.FilePath is not null)
            {
                continue;       // saved file no longer exists and nothing unsaved to recover
            }

            tab.ApplyOptions(Settings, _zoom);
            tab.Editor.CaretIndex = Math.Clamp(st.CaretIndex, 0, tab.Editor.Text.Length);
            tab.ScrollOffset = st.ScrollOffset;
            Documents.Add(tab);
        }

        if (Documents.Count > 0)
            ActiveTab = Documents[Math.Clamp(sw.ActiveIndex, 0, Documents.Count - 1)];
    }

    private void EnsureOnScreen()
    {
        var area = SystemParameters.WorkArea;
        if (Left + Width < area.Left + 80 || Left > area.Right - 80 ||
            Top < area.Top - 10 || Top > area.Bottom - 60)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // ==================================================================== lifetime

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;

        foreach (var tab in Documents.ToList())
        {
            if (!ConfirmSave(tab)) { e.Cancel = true; return; }
        }

        _closing = true;
        _autosaveTimer.Stop();
        Settings.Zoom = _zoom;
        Settings.Save();
        App.PersistSession();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;

        // Only rewrite the session while other windows survive. When this was the last
        // one, the snapshot written during Closing is exactly what should be restored.
        if (Application.Current?.Windows.OfType<MainWindow>().Any() == true)
            App.PersistSession();
    }

    // ==================================================================== INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
