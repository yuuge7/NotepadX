using System.Text;

namespace NotepadX.Tests;

public class EncodingDetectionTests
{
    [Fact]
    public void Utf8BomIsDetectedAndSkipped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("hello")];

        var info = TextFileIo.DetectEncoding(bytes, out int bomLength);

        Assert.Equal("UTF-8 with BOM", info.Name);
        Assert.Equal(3, bomLength);
    }

    [Fact]
    public void Utf16LittleEndianBomIsDetected()
    {
        byte[] bytes = [0xFF, 0xFE, 0x68, 0x00];

        var info = TextFileIo.DetectEncoding(bytes, out int bomLength);

        Assert.Equal("UTF-16 LE", info.Name);
        Assert.Equal(2, bomLength);
    }

    [Fact]
    public void Utf16BigEndianBomIsDetected()
    {
        byte[] bytes = [0xFE, 0xFF, 0x00, 0x68];

        var info = TextFileIo.DetectEncoding(bytes, out int bomLength);

        Assert.Equal("UTF-16 BE", info.Name);
        Assert.Equal(2, bomLength);
    }

    [Fact]
    public void PlainAsciiIsTreatedAsUtf8WithoutBom()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("plain text, nothing exotic");

        var info = TextFileIo.DetectEncoding(bytes, out int bomLength);

        Assert.Equal("UTF-8", info.Name);
        Assert.Equal(0, bomLength);
    }

    [Fact]
    public void BomlessUtf8IsRecognisedByItsByteStructure()
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes("acentuação e emoji 🙂");

        var info = TextFileIo.DetectEncoding(bytes, out _);

        Assert.Equal("UTF-8", info.Name);
    }

    [Fact]
    public void InvalidUtf8FallsBackToAnsi()
    {
        // 0x93 and 0x94 are curly quotes in Windows-1252 and illegal as lone UTF-8 bytes.
        byte[] bytes = [0x48, 0x93, 0x69, 0x94];

        var info = TextFileIo.DetectEncoding(bytes, out _);

        Assert.Equal("ANSI", info.Name);
    }

    [Fact]
    public void BomlessUtf16IsGuessedFromInterleavedZeroBytes()
    {
        byte[] bytes = Encoding.Unicode.GetBytes("a reasonably long ASCII sentence");

        var info = TextFileIo.DetectEncoding(bytes, out int bomLength);

        Assert.Equal("UTF-16 LE", info.Name);
        Assert.Equal(0, bomLength);
    }
}

public class LineEndingTests
{
    [Theory]
    [InlineData("a\r\nb\r\nc", LineEnding.Crlf)]
    [InlineData("a\nb\nc", LineEnding.Lf)]
    [InlineData("a\rb\rc", LineEnding.Cr)]
    [InlineData("no line breaks at all", LineEnding.Crlf)]
    public void DominantStyleWins(string text, LineEnding expected)
    {
        Assert.Equal(expected, TextFileIo.DetectLineEnding(text));
    }

    [Fact]
    public void MixedContentPicksTheMajority()
    {
        Assert.Equal(LineEnding.Crlf, TextFileIo.DetectLineEnding("a\r\nb\r\nc\nd"));
        Assert.Equal(LineEnding.Lf, TextFileIo.DetectLineEnding("a\nb\nc\r\nd"));
    }

    [Fact]
    public void NormalizeCollapsesEveryStyleToLineFeed()
    {
        Assert.Equal("a\nb\nc\nd", TextFileIo.Normalize("a\r\nb\rc\nd"));
    }

    [Fact]
    public void NormalizeLeavesLineFeedOnlyTextUntouched()
    {
        const string text = "already\nnormalised";
        Assert.Same(text, TextFileIo.Normalize(text));
    }

    [Theory]
    [InlineData(LineEnding.Crlf, "a\r\nb")]
    [InlineData(LineEnding.Lf, "a\nb")]
    [InlineData(LineEnding.Cr, "a\rb")]
    public void DenormalizeAppliesTheRequestedStyle(LineEnding eol, string expected)
    {
        Assert.Equal(expected, TextFileIo.Denormalize("a\nb", eol));
    }

    [Fact]
    public void NormalizeAndDenormalizeRoundTrip()
    {
        const string original = "one\r\ntwo\r\nthree\r\n";

        string result = TextFileIo.Denormalize(TextFileIo.Normalize(original), LineEnding.Crlf);

        Assert.Equal(original, result);
    }
}

public class EncodingCapabilityTests
{
    [Fact]
    public void Utf8AcceptsEverything()
    {
        Assert.True(TextFileIo.CanEncode("çãõ – 🙂 日本語", TextFileIo.Utf8, out int count, out _));
        Assert.Equal(0, count);
    }

    [Fact]
    public void EmptyTextIsAlwaysEncodable()
    {
        Assert.True(TextFileIo.CanEncode("", TextFileIo.AnsiInfo, out _, out _));
    }

    [Fact]
    public void AnsiRejectsCharactersOutsideItsCodePage()
    {
        // This is the data-loss case: the default encoder would write '?' silently.
        bool ok = TextFileIo.CanEncode("plain 日本語 text", TextFileIo.AnsiInfo, out int count, out string sample);

        Assert.False(ok);
        Assert.Equal(3, count);
        Assert.NotEqual("", sample);
    }

    [Fact]
    public void AnsiAcceptsPlainAscii()
    {
        Assert.True(TextFileIo.CanEncode("ordinary ASCII", TextFileIo.AnsiInfo, out _, out _));
    }
}

public class SaveRoundTripTests
{
    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-8 with BOM")]
    [InlineData("UTF-16 LE")]
    [InlineData("UTF-16 BE")]
    public void ContentSurvivesSaveAndLoad(string encodingName)
    {
        string path = Path.Combine(Path.GetTempPath(), "notepadx-test-" + Guid.NewGuid().ToString("N") + ".txt");
        const string text = "first line\nsecond line\naccents: áéíóú çãõ\n";

        try
        {
            TextFileIo.Save(path, text, TextFileIo.ByName(encodingName), LineEnding.Crlf);
            var loaded = TextFileIo.Load(path);

            Assert.Equal(text, loaded.Text);
            Assert.Equal(encodingName, loaded.Encoding.Name);
            Assert.Equal(LineEnding.Crlf, loaded.LineEnding);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SavingOverAnExistingFileReplacesItAtomically()
    {
        string path = Path.Combine(Path.GetTempPath(), "notepadx-test-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            TextFileIo.Save(path, "first\n", TextFileIo.Utf8, LineEnding.Lf);
            TextFileIo.Save(path, "second\n", TextFileIo.Utf8, LineEnding.Lf);

            Assert.Equal("second\n", TextFileIo.Load(path).Text);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.nptmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
