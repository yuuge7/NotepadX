using System.Text.RegularExpressions;

namespace NotepadX.Services;

public readonly record struct SearchOptions(bool MatchCase, bool WholeWord, bool UseRegex);

public readonly record struct SearchHit(int Index, int Length)
{
    public int End => Index + Length;
}

/// <summary>
/// All find and replace logic, kept free of UI so it can be tested directly.
/// Plain and regular-expression searches share one path: a literal search is simply an
/// escaped pattern, which keeps whole-word handling and group replacement consistent.
/// </summary>
public static class DocumentSearch
{
    /// <summary>A pathological pattern must not be able to hang the editor.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static Regex? TryBuild(string pattern, SearchOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pattern)) return null;

        var flags = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;

        string source = options.UseRegex ? pattern : Regex.Escape(pattern);

        try
        {
            return new Regex(source, flags, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static bool IsWholeWordAt(string text, int index, int length)
    {
        if (length <= 0) return false;
        bool leftOk = index == 0 || !IsWordChar(text[index - 1]);
        int end = index + length;
        bool rightOk = end >= text.Length || !IsWordChar(text[end]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Every match in order. Zero-length matches are dropped: they cannot be selected,
    /// and they would stall "find next" on patterns such as <c>a*</c>.
    /// </summary>
    public static IEnumerable<SearchHit> FindAll(string text, string pattern, SearchOptions options)
    {
        var regex = TryBuild(pattern, options, out _);
        if (regex is null) return [];
        return Enumerate(regex, text, options);
    }

    private static IEnumerable<SearchHit> Enumerate(Regex regex, string text, SearchOptions options)
    {
        Match m;
        try { m = regex.Match(text); }
        catch (RegexMatchTimeoutException) { yield break; }

        while (m.Success)
        {
            if (m.Length > 0 && (!options.WholeWord || IsWholeWordAt(text, m.Index, m.Length)))
                yield return new SearchHit(m.Index, m.Length);

            int next = m.Index + Math.Max(1, m.Length);
            if (next > text.Length) yield break;

            try { m = regex.Match(text, next); }
            catch (RegexMatchTimeoutException) { yield break; }
        }
    }

    /// <summary>Matches that start inside [from, from + length), for painting the visible region.</summary>
    public static List<SearchHit> FindInRange(string text, string pattern, SearchOptions options, int from, int length)
    {
        var results = new List<SearchHit>();
        var regex = TryBuild(pattern, options, out _);
        if (regex is null) return results;

        from = Math.Clamp(from, 0, text.Length);
        int end = Math.Clamp(from + length, from, text.Length);

        Match m;
        try { m = regex.Match(text, from); }
        catch (RegexMatchTimeoutException) { return results; }

        while (m.Success && m.Index < end)
        {
            if (m.Length > 0 && (!options.WholeWord || IsWholeWordAt(text, m.Index, m.Length)))
                results.Add(new SearchHit(m.Index, m.Length));

            int next = m.Index + Math.Max(1, m.Length);
            if (next > text.Length) break;

            try { m = regex.Match(text, next); }
            catch (RegexMatchTimeoutException) { break; }
        }
        return results;
    }

    public static int Count(string text, string pattern, SearchOptions options)
    {
        int n = 0;
        foreach (var _ in FindAll(text, pattern, options)) n++;
        return n;
    }

    /// <summary>
    /// The next match at or after <paramref name="start"/>, or the last one before it when
    /// searching backwards. Returns null when there is none; wrapping is the caller's call.
    /// </summary>
    public static SearchHit? Find(string text, string pattern, int start, bool backwards, SearchOptions options)
    {
        start = Math.Clamp(start, 0, text.Length);

        if (!backwards)
        {
            foreach (var hit in FindAll(text, pattern, options))
                if (hit.Index >= start) return hit;
            return null;
        }

        SearchHit? best = null;
        foreach (var hit in FindAll(text, pattern, options))
        {
            if (hit.Index >= start) break;
            best = hit;
        }
        return best;
    }

    /// <summary>Wrapping search: continues from the other end when nothing is found.</summary>
    public static SearchHit? FindWrapped(string text, string pattern, int start, bool backwards, SearchOptions options, bool wrap)
    {
        var hit = Find(text, pattern, start, backwards, options);
        if (hit is not null || !wrap) return hit;
        return Find(text, pattern, backwards ? text.Length : 0, backwards, options);
    }

    /// <summary>
    /// Expands the replacement for one match. In regex mode <c>$1</c> and friends resolve
    /// against that match; in plain mode the text is inserted verbatim.
    /// </summary>
    public static string ExpandReplacement(string text, SearchHit hit, string pattern, string replacement, SearchOptions options)
    {
        if (!options.UseRegex) return replacement;

        var regex = TryBuild(pattern, options, out _);
        if (regex is null) return replacement;

        try
        {
            var m = regex.Match(text, hit.Index);
            if (m.Success && m.Index == hit.Index && m.Length == hit.Length) return m.Result(replacement);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or ArgumentException)
        {
        }
        return replacement;
    }

    public static string ReplaceAll(string text, string pattern, string replacement, SearchOptions options, out int replaced)
    {
        replaced = 0;
        var regex = TryBuild(pattern, options, out _);
        if (regex is null) return text;

        int count = 0;
        string result;
        try
        {
            result = regex.Replace(text, m =>
            {
                if (m.Length == 0) return m.Value;
                if (options.WholeWord && !IsWholeWordAt(text, m.Index, m.Length)) return m.Value;
                count++;
                return options.UseRegex ? m.Result(replacement) : replacement;
            });
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or ArgumentException)
        {
            return text;
        }

        replaced = count;
        return result;
    }
}
