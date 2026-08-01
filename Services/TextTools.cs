using System.Globalization;
using System.Text;

namespace NotepadX.Services;

/// <summary>
/// Line and case transforms applied to the selection, or to the whole document when
/// nothing is selected. Every method preserves the line ending style it was handed, so
/// running a tool on part of a CRLF file does not quietly introduce bare newlines.
/// </summary>
public static class TextTools
{
    public static string SortLines(string text, bool descending = false, bool ignoreCase = true)
    {
        return Transform(text, lines =>
        {
            var comparer = ignoreCase ? StringComparer.CurrentCultureIgnoreCase : StringComparer.CurrentCulture;
            var sorted = lines.OrderBy(l => l, comparer).ToList();
            if (descending) sorted.Reverse();
            return sorted;
        });
    }

    public static string ReverseLines(string text) =>
        Transform(text, lines => Enumerable.Reverse(lines).ToList());

    public static string RemoveDuplicateLines(string text, bool ignoreCase = false)
    {
        return Transform(text, lines =>
        {
            var seen = new HashSet<string>(ignoreCase ? StringComparer.CurrentCultureIgnoreCase : StringComparer.CurrentCulture);
            return lines.Where(seen.Add).ToList();
        });
    }

    public static string RemoveEmptyLines(string text) =>
        Transform(text, lines => lines.Where(l => l.Trim().Length > 0).ToList());

    public static string TrimTrailingWhitespace(string text) =>
        Transform(text, lines => lines.Select(l => l.TrimEnd(' ', '\t')).ToList());

    public static string JoinLines(string text, string separator = " ")
    {
        var eol = TextFileIo.DetectLineEnding(text);
        var lines = SplitLines(TextFileIo.Normalize(text));

        // A trailing newline in the source should not become a trailing separator.
        bool trailing = lines.Count > 1 && lines[^1].Length == 0;
        if (trailing) lines.RemoveAt(lines.Count - 1);

        string joined = string.Join(separator, lines);
        return trailing ? joined + Eol(eol) : joined;
    }

    public static string ToUpper(string text) => text.ToUpper(CultureInfo.CurrentCulture);

    public static string ToLower(string text) => text.ToLower(CultureInfo.CurrentCulture);

    /// <summary>Capitalises the first letter of each word and lowercases the rest.</summary>
    public static string ToTitleCase(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool atWordStart = true;

        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                sb.Append(atWordStart
                    ? char.ToUpper(c, CultureInfo.CurrentCulture)
                    : char.ToLower(c, CultureInfo.CurrentCulture));
                atWordStart = false;
            }
            else
            {
                sb.Append(c);
                // An apostrophe stays inside a word: "don't", not "Don'T".
                if (c != '\'') atWordStart = true;
            }
        }
        return sb.ToString();
    }

    public static string ToggleCase(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(char.IsUpper(c) ? char.ToLower(c, CultureInfo.CurrentCulture)
                    : char.IsLower(c) ? char.ToUpper(c, CultureInfo.CurrentCulture)
                    : c);
        }
        return sb.ToString();
    }

    public static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;

        foreach (char c in text)
        {
            bool part = !char.IsWhiteSpace(c);
            if (part && !inWord) words++;
            inWord = part;
        }
        return words;
    }

    // ------------------------------------------------------------------ helpers

    private static string Transform(string text, Func<List<string>, List<string>> operation)
    {
        var eol = TextFileIo.DetectLineEnding(text);
        var lines = SplitLines(TextFileIo.Normalize(text));

        // Keep a trailing blank line out of the operation and put it back afterwards,
        // so sorting does not float an empty line to the top.
        bool trailing = lines.Count > 1 && lines[^1].Length == 0;
        if (trailing) lines.RemoveAt(lines.Count - 1);

        var result = operation(lines);
        if (trailing) result.Add(string.Empty);

        return string.Join(Eol(eol), result);
    }

    private static List<string> SplitLines(string normalized) => [.. normalized.Split('\n')];

    private static string Eol(LineEnding eol) => eol switch
    {
        LineEnding.Crlf => "\r\n",
        LineEnding.Cr => "\r",
        _ => "\n"
    };
}
