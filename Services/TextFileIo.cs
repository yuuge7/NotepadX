using System.Text;

namespace NotepadX.Services;

public enum LineEnding { Crlf, Lf, Cr }

public sealed class TextEncodingInfo
{
    public required string Name { get; init; }
    public required Encoding Encoding { get; init; }
    public bool WriteBom { get; init; }

    public override string ToString() => Name;
}

public sealed class LoadedFile
{
    public required string Text { get; init; }          // always normalised to \n
    public required TextEncodingInfo Encoding { get; init; }
    public required LineEnding LineEnding { get; init; }
}

public static class TextFileIo
{
    private static Encoding? _ansi;

    public static Encoding Ansi
    {
        get
        {
            if (_ansi is not null) return _ansi;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _ansi = Encoding.GetEncoding(0); // system ANSI code page
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                _ansi = Encoding.Latin1;
            }
            return _ansi;
        }
    }

    public static TextEncodingInfo Utf8 => new() { Name = "UTF-8", Encoding = new UTF8Encoding(false), WriteBom = false };
    public static TextEncodingInfo Utf8Bom => new() { Name = "UTF-8 with BOM", Encoding = new UTF8Encoding(true), WriteBom = true };
    public static TextEncodingInfo Utf16Le => new() { Name = "UTF-16 LE", Encoding = new UnicodeEncoding(false, true), WriteBom = true };
    public static TextEncodingInfo Utf16Be => new() { Name = "UTF-16 BE", Encoding = new UnicodeEncoding(true, true), WriteBom = true };
    public static TextEncodingInfo AnsiInfo => new() { Name = "ANSI", Encoding = Ansi, WriteBom = false };

    public static IReadOnlyList<TextEncodingInfo> All => new[] { Utf8, Utf8Bom, Utf16Le, Utf16Be, AnsiInfo };

    public static TextEncodingInfo ByName(string? name) => name switch
    {
        "UTF-8 with BOM" => Utf8Bom,
        "UTF-16 LE" => Utf16Le,
        "UTF-16 BE" => Utf16Be,
        "ANSI" => AnsiInfo,
        _ => Utf8
    };

    public static LoadedFile Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var enc = DetectEncoding(bytes, out int bomLength);
        string raw = enc.Encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        var eol = DetectLineEnding(raw);
        return new LoadedFile { Text = Normalize(raw), Encoding = enc, LineEnding = eol };
    }

    public static void Save(string path, string text, TextEncodingInfo encoding, LineEnding eol)
    {
        string outText = Denormalize(text, eol);

        // Write to a sibling temp file first so a failure mid-write cannot destroy the original.
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".nptmp");

        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (encoding.WriteBom)
                {
                    var preamble = encoding.Encoding.GetPreamble();
                    if (preamble.Length > 0) fs.Write(preamble, 0, preamble.Length);
                }
                var data = encoding.Encoding.GetBytes(outText);
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
            }
            else
            {
                try
                {
                    File.Replace(tmp, path, null, ignoreMetadataErrors: true);
                }
                catch (IOException)
                {
                    // File.Replace is unavailable on some network shares and FAT volumes.
                    File.Copy(tmp, path, overwrite: true);
                }
            }
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }
    }

    public static TextEncodingInfo DetectEncoding(byte[] b, out int bomLength)
    {
        bomLength = 0;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { bomLength = 3; return Utf8Bom; }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { bomLength = 2; return Utf16Le; }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { bomLength = 2; return Utf16Be; }

        if (LooksLikeUtf16(b, out bool bigEndian))
            return bigEndian ? Utf16Be : Utf16Le;

        return IsValidUtf8(b) ? Utf8 : AnsiInfo;
    }

    /// <summary>BOM-less UTF-16 guess: lots of zero bytes in one alternating position.</summary>
    private static bool LooksLikeUtf16(byte[] b, out bool bigEndian)
    {
        bigEndian = false;
        int limit = Math.Min(b.Length, 4096);
        if (limit < 16 || limit % 2 != 0) return false;

        int zerosEven = 0, zerosOdd = 0;
        for (int i = 0; i < limit; i += 2)
        {
            if (b[i] == 0) zerosEven++;
            if (b[i + 1] == 0) zerosOdd++;
        }

        int pairs = limit / 2;
        if (zerosOdd > pairs * 0.3 && zerosEven < pairs * 0.05) { bigEndian = false; return true; }
        if (zerosEven > pairs * 0.3 && zerosOdd < pairs * 0.05) { bigEndian = true; return true; }
        return false;
    }

    private static bool IsValidUtf8(byte[] b)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }

        if (crlf == 0 && lf == 0 && cr == 0) return LineEnding.Crlf;
        if (crlf >= lf && crlf >= cr) return LineEnding.Crlf;
        return lf >= cr ? LineEnding.Lf : LineEnding.Cr;
    }

    /// <summary>Collapses every flavour of line break to \n for the in-memory buffer.</summary>
    public static string Normalize(string text)
    {
        if (text.IndexOf('\r') < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                sb.Append('\n');
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Denormalize(string text, LineEnding eol)
    {
        string normalized = Normalize(text);
        return eol switch
        {
            LineEnding.Crlf => normalized.Replace("\n", "\r\n"),
            LineEnding.Cr => normalized.Replace("\n", "\r"),
            _ => normalized
        };
    }

    public static string Label(LineEnding eol) => eol switch
    {
        LineEnding.Crlf => "Windows (CRLF)",
        LineEnding.Lf => "Unix (LF)",
        _ => "Macintosh (CR)"
    };
}
