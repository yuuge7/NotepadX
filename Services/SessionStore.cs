using System.Text.Json;

namespace NotepadX.Services;

public sealed class SessionTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? FilePath { get; set; }
    public string Title { get; set; } = "Untitled";
    public bool HasUnsavedBuffer { get; set; }
    public string EncodingName { get; set; } = "UTF-8";
    public LineEnding LineEnding { get; set; } = LineEnding.Crlf;
    public int CaretIndex { get; set; }
    public double ScrollOffset { get; set; }
}

public sealed class SessionWindow
{
    public List<SessionTab> Tabs { get; set; } = new();
    public int ActiveIndex { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 700;
    public bool Maximized { get; set; }
}

public sealed class SessionState
{
    public List<SessionWindow> Windows { get; set; } = new();
}

/// <summary>
/// Persists open tabs and their unsaved text so closing the app never loses work.
/// Buffers are plain UTF-8 files on this machine only.
/// </summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static SessionState Load()
    {
        try
        {
            if (File.Exists(AppPaths.SessionFile))
            {
                var json = File.ReadAllText(AppPaths.SessionFile);
                return JsonSerializer.Deserialize<SessionState>(json, Options) ?? new SessionState();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new SessionState();
    }

    public static void Save(SessionState state)
    {
        lock (Gate)
        {
            try
            {
                var tmp = AppPaths.SessionFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
                File.Copy(tmp, AppPaths.SessionFile, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static void WriteBuffer(Guid id, string text)
    {
        try { File.WriteAllText(AppPaths.BufferFor(id), text, System.Text.Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string? ReadBuffer(Guid id)
    {
        try
        {
            var p = AppPaths.BufferFor(id);
            return File.Exists(p) ? File.ReadAllText(p, System.Text.Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void DeleteBuffer(Guid id)
    {
        try
        {
            var p = AppPaths.BufferFor(id);
            if (File.Exists(p)) File.Delete(p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes buffer files that no live session tab refers to.</summary>
    public static void PruneOrphanBuffers(SessionState state)
    {
        try
        {
            var live = state.Windows.SelectMany(w => w.Tabs).Select(t => t.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(AppPaths.BufferDir, "*.txt"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!live.Contains(name))
                {
                    try { File.Delete(file); } catch (IOException) { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
