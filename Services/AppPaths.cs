namespace NotepadX.Services;

/// <summary>
/// Everything the app writes lives under %LOCALAPPDATA%\NotepadX. No network, no cloud.
/// A "NotepadX.portable" file next to the exe switches storage to a Data folder beside it.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> RootLazy = new(ResolveRoot);

    public static string Root => RootLazy.Value;
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string SessionFile => Path.Combine(Root, "session.json");
    public static string BufferDir => Path.Combine(Root, "buffers");

    private static string ResolveRoot()
    {
        var exeDir = AppContext.BaseDirectory;
        var portableMarker = Path.Combine(exeDir, "NotepadX.portable");

        string root = File.Exists(portableMarker)
            ? Path.Combine(exeDir, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotepadX");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "buffers"));
        return root;
    }

    public static string BufferFor(Guid id) => Path.Combine(BufferDir, id.ToString("N") + ".txt");
}
