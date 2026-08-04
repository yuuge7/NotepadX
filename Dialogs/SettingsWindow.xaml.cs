using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotepadX.Interop;
using NotepadX.Services;

namespace NotepadX.Dialogs;

public partial class SettingsWindow : Window
{
    private static AppSettings S => AppSettings.Current;
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();

        PopulateFonts();
        PopulateSizes();
        PopulateEncodings();
        LoadValues();
        _loading = false;
        Wire();

        VersionText.Text = "NotepadX " +
            (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        RefreshAssociationState();
    }

    /// <summary>
    /// Dark caption has to be asked for before the window is first painted. Doing it from
    /// Loaded is too late: the frame is already drawn light and stays that way.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.SetDarkTitleBar(this, ThemeManager.IsDark);
    }

    private void PopulateFonts()
    {
        var names = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FontFamilyBox.ItemsSource = names;
    }

    private void PopulateSizes()
    {
        FontSizeBox.ItemsSource = new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72 };
    }

    private void PopulateEncodings()
    {
        EncodingBox.ItemsSource = TextFileIo.All.Select(e => e.Name).ToList();
    }

    private void LoadValues()
    {
        ThemeBox.SelectedIndex = S.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };

        FontFamilyBox.SelectedItem = S.FontFamily;
        if (FontFamilyBox.SelectedItem is null) FontFamilyBox.Text = S.FontFamily;
        FontSizeBox.Text = ((int)S.FontSize).ToString();

        BoldCheck.IsChecked = S.FontBold;
        ItalicCheck.IsChecked = S.FontItalic;
        WrapCheck.IsChecked = S.WordWrap;
        LineNumbersCheck.IsChecked = S.ShowLineNumbers;
        StatusCheck.IsChecked = S.ShowStatusBar;
        WordCountCheck.IsChecked = S.ShowWordCount;
        HighlightCheck.IsChecked = S.HighlightAllMatches;
        SpellCheckBox.IsChecked = S.SpellCheck;
        IndentCheck.IsChecked = S.AutoIndent;

        OpenInBox.SelectedIndex = S.OpenFilesIn == OpenFilesIn.NewWindow ? 1 : 0;
        StartupBox.SelectedIndex = S.SessionMode == SessionMode.OpenNewTab ? 1 : 0;
        AskSaveCheck.IsChecked = S.AskToSaveOnClose;
        EncodingBox.SelectedItem = S.DefaultEncoding;
        EolBox.SelectedIndex = S.DefaultLineEnding == "LF" ? 1 : 0;

        UpdatePreview();
    }

    private void Wire()
    {
        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            S.Theme = ThemeBox.SelectedIndex switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.System };
        };

        FontFamilyBox.SelectionChanged += (_, _) =>
        {
            if (_loading || FontFamilyBox.SelectedItem is not string name) return;
            S.FontFamily = name;
            UpdatePreview();
        };

        FontSizeBox.SelectionChanged += (_, _) => ApplySize();
        FontSizeBox.LostFocus += (_, _) => ApplySize();
        FontSizeBox.KeyUp += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplySize(); };

        BoldCheck.Click += (_, _) => { S.FontBold = BoldCheck.IsChecked == true; UpdatePreview(); };
        ItalicCheck.Click += (_, _) => { S.FontItalic = ItalicCheck.IsChecked == true; UpdatePreview(); };
        WrapCheck.Click += (_, _) => S.WordWrap = WrapCheck.IsChecked == true;
        LineNumbersCheck.Click += (_, _) => S.ShowLineNumbers = LineNumbersCheck.IsChecked == true;
        StatusCheck.Click += (_, _) => S.ShowStatusBar = StatusCheck.IsChecked == true;
        WordCountCheck.Click += (_, _) => S.ShowWordCount = WordCountCheck.IsChecked == true;
        HighlightCheck.Click += (_, _) => S.HighlightAllMatches = HighlightCheck.IsChecked == true;
        SpellCheckBox.Click += (_, _) => S.SpellCheck = SpellCheckBox.IsChecked == true;
        IndentCheck.Click += (_, _) => S.AutoIndent = IndentCheck.IsChecked == true;

        OpenInBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            S.OpenFilesIn = OpenInBox.SelectedIndex == 1 ? OpenFilesIn.NewWindow : OpenFilesIn.NewTab;
        };
        StartupBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            S.SessionMode = StartupBox.SelectedIndex == 1 ? SessionMode.OpenNewTab : SessionMode.ContinuePrevious;
        };
        AskSaveCheck.Click += (_, _) => S.AskToSaveOnClose = AskSaveCheck.IsChecked == true;
        EncodingBox.SelectionChanged += (_, _) =>
        {
            if (_loading || EncodingBox.SelectedItem is not string n) return;
            S.DefaultEncoding = n;
        };
        EolBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            S.DefaultLineEnding = EolBox.SelectedIndex == 1 ? "LF" : "CRLF";
        };
    }

    private void ApplySize()
    {
        if (_loading) return;
        string raw = (FontSizeBox.SelectedItem?.ToString() ?? FontSizeBox.Text ?? "").Trim();
        if (double.TryParse(raw, out double size) && size >= 4 && size <= 200)
        {
            S.FontSize = size;
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        try { FontPreview.FontFamily = new FontFamily(S.FontFamily); }
        catch (Exception) { FontPreview.FontFamily = new FontFamily("Consolas"); }

        FontPreview.FontSize = Math.Clamp(S.FontSize * 96.0 / 72.0, 6, 60);
        FontPreview.FontWeight = S.FontBold ? FontWeights.Bold : FontWeights.Normal;
        FontPreview.FontStyle = S.FontItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppPaths.Root, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var ok = MessageBox.Show("Reset every setting to its default?", "NotepadX",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        var d = new AppSettings();
        S.Theme = d.Theme;
        S.FontFamily = d.FontFamily;
        S.FontSize = d.FontSize;
        S.FontBold = d.FontBold;
        S.FontItalic = d.FontItalic;
        S.WordWrap = d.WordWrap;
        S.ShowStatusBar = d.ShowStatusBar;
        S.SpellCheck = d.SpellCheck;
        S.AutoIndent = d.AutoIndent;
        S.SessionMode = d.SessionMode;
        S.AskToSaveOnClose = d.AskToSaveOnClose;
        S.OpenFilesIn = d.OpenFilesIn;
        S.DefaultEncoding = d.DefaultEncoding;
        S.DefaultLineEnding = d.DefaultLineEnding;
        S.Zoom = d.Zoom;

        _loading = true;
        LoadValues();
        _loading = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ file types

    private void RefreshAssociationState()
    {
        bool registered = FileAssociation.IsRegistered();
        AssociationState.Text = registered
            ? "NotepadX is registered with Windows for text file types."
            : "NotepadX is not registered with Windows yet.";

        RegisterButton.IsEnabled = !registered;
        UnregisterButton.IsEnabled = registered;
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        if (FileAssociation.Register(out string? error))
        {
            RefreshAssociationState();
            MessageBox.Show(
                "Registered.\n\n" +
                "Windows does not allow an app to make itself the default — open Windows " +
                "default apps and pick NotepadX for the types you want.",
                "NotepadX", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Could not register:\n\n" + error, "NotepadX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Unregister_Click(object sender, RoutedEventArgs e)
    {
        if (FileAssociation.Unregister(out string? error)) RefreshAssociationState();
        else
        {
            MessageBox.Show("Could not unregister:\n\n" + error, "NotepadX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e) =>
        FileAssociation.OpenDefaultAppsSettings();
}
