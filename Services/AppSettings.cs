using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotepadX.Services;

public enum AppTheme { System, Light, Dark }
public enum OpenFilesIn { NewTab, NewWindow }
public enum SessionMode { ContinuePrevious, OpenNewTab }

public sealed class AppSettings : INotifyPropertyChanged
{
    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private AppTheme _theme = AppTheme.System;
    private string _fontFamily = "Consolas";
    private double _fontSize = 11;
    private bool _fontBold;
    private bool _fontItalic;
    private bool _wordWrap = true;
    private bool _showStatusBar = true;
    private bool _spellCheck;
    private bool _autoIndent;
    private SessionMode _sessionMode = SessionMode.ContinuePrevious;
    private bool _askToSave = true;
    private OpenFilesIn _openFilesIn = OpenFilesIn.NewTab;
    private string _defaultEncoding = "UTF-8";
    private string _defaultLineEnding = "CRLF";
    private double _zoom = 1.0;

    public AppTheme Theme { get => _theme; set => Set(ref _theme, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public bool FontBold { get => _fontBold; set => Set(ref _fontBold, value); }
    public bool FontItalic { get => _fontItalic; set => Set(ref _fontItalic, value); }
    public bool WordWrap { get => _wordWrap; set => Set(ref _wordWrap, value); }
    public bool ShowStatusBar { get => _showStatusBar; set => Set(ref _showStatusBar, value); }
    public bool SpellCheck { get => _spellCheck; set => Set(ref _spellCheck, value); }
    public bool AutoIndent { get => _autoIndent; set => Set(ref _autoIndent, value); }
    public SessionMode SessionMode { get => _sessionMode; set => Set(ref _sessionMode, value); }
    public bool AskToSaveOnClose { get => _askToSave; set => Set(ref _askToSave, value); }
    public OpenFilesIn OpenFilesIn { get => _openFilesIn; set => Set(ref _openFilesIn, value); }
    public string DefaultEncoding { get => _defaultEncoding; set => Set(ref _defaultEncoding, value); }
    public string DefaultLineEnding { get => _defaultLineEnding; set => Set(ref _defaultLineEnding, value); }
    public double Zoom { get => _zoom; set => Set(ref _zoom, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file must never stop the editor from opening.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Copy(tmp, AppPaths.SettingsFile, overwrite: true);
            File.Delete(tmp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
