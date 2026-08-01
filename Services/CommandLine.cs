namespace NotepadX.Services;

public sealed class FileRequest
{
    public required string Path { get; init; }
    /// <summary>1-based line to jump to, or null when none was given.</summary>
    public int? Line { get; init; }
}

public sealed class CommandLineArgs
{
    public List<FileRequest> Files { get; } = [];
    public bool Print { get; init; }
    public bool NewWindow { get; init; }
    public bool ShowHelp { get; init; }
    public List<string> Unknown { get; } = [];

    public const string Usage = """
        NotepadX — offline text editor

        Usage:
          NotepadX [options] [file ...]

        Files:
          file              Open the file, creating it if you confirm
          file:42           Open the file and jump to line 42

        Options:
          /p, --print       Print each file, then exit
          -n, --new-window  Force a new window instead of a tab in the running one
          -h, --help, /?    Show this message
        """;
}

/// <summary>
/// Parses the arguments the shell hands over. The awkward part is <c>file:42</c>: a
/// Windows path already contains a colon after the drive letter, so only a trailing
/// ":digits" that is not the drive separator counts as a line number.
/// </summary>
public static class CommandLine
{
    public static CommandLineArgs Parse(IEnumerable<string> args)
    {
        bool print = false, newWindow = false, help = false;
        var files = new List<FileRequest>();
        var unknown = new List<string>();

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var arg = raw.Trim();

            if (IsSwitch(arg, out string name))
            {
                switch (name.ToLowerInvariant())
                {
                    case "p" or "print": print = true; break;
                    case "n" or "new-window" or "newwindow": newWindow = true; break;
                    case "h" or "?" or "help": help = true; break;
                    default: unknown.Add(arg); break;
                }
                continue;
            }

            files.Add(ParseFile(arg));
        }

        var result = new CommandLineArgs { Print = print, NewWindow = newWindow, ShowHelp = help };
        result.Files.AddRange(files);
        result.Unknown.AddRange(unknown);
        return result;
    }

    private static bool IsSwitch(string arg, out string name)
    {
        name = "";

        if (arg.StartsWith("--", StringComparison.Ordinal)) { name = arg[2..]; return true; }
        if (arg.StartsWith('-') && arg.Length > 1) { name = arg[1..]; return true; }

        // "/p" is a switch; "/home/user/notes.txt" and "/My Documents" are paths.
        if (arg.StartsWith('/') && arg.Length > 1)
        {
            string rest = arg[1..];
            bool looksLikePath = rest.Contains('/') || rest.Contains('\\') || rest.Contains('.') || rest.Contains(' ');
            if (!looksLikePath && rest.Length <= 12)
            {
                name = rest;
                return true;
            }
        }
        return false;
    }

    public static FileRequest ParseFile(string arg)
    {
        int colon = arg.LastIndexOf(':');

        // Not a separator at all, or it is the drive colon in "C:\path".
        if (colon > 1 && colon < arg.Length - 1)
        {
            string tail = arg[(colon + 1)..];
            if (tail.Length > 0 && tail.All(char.IsAsciiDigit) && int.TryParse(tail, out int line) && line > 0)
            {
                string head = arg[..colon];
                if (head.Length > 0) return new FileRequest { Path = head, Line = line };
            }
        }

        return new FileRequest { Path = arg };
    }
}
