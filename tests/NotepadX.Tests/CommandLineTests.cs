namespace NotepadX.Tests;

public class CommandLineParsingTests
{
    [Fact]
    public void APlainPathIsAFileWithNoLine()
    {
        var parsed = CommandLine.Parse([@"C:\notes\todo.txt"]);

        Assert.Single(parsed.Files);
        Assert.Equal(@"C:\notes\todo.txt", parsed.Files[0].Path);
        Assert.Null(parsed.Files[0].Line);
    }

    [Fact]
    public void ATrailingColonNumberIsALineNumber()
    {
        var parsed = CommandLine.Parse([@"C:\notes\todo.txt:42"]);

        Assert.Equal(@"C:\notes\todo.txt", parsed.Files[0].Path);
        Assert.Equal(42, parsed.Files[0].Line);
    }

    [Fact]
    public void TheDriveColonIsNotMistakenForALineNumber()
    {
        var parsed = CommandLine.Parse([@"C:\file.txt"]);

        Assert.Equal(@"C:\file.txt", parsed.Files[0].Path);
        Assert.Null(parsed.Files[0].Line);
    }

    [Fact]
    public void ABareDriveWithDigitsIsStillAPath()
    {
        // "C:42" would be a relative path on drive C, not line 42 of a file named "C".
        var parsed = CommandLine.Parse(["C:42"]);

        Assert.Equal("C:42", parsed.Files[0].Path);
        Assert.Null(parsed.Files[0].Line);
    }

    [Fact]
    public void ZeroIsNotAValidLineNumber()
    {
        var parsed = CommandLine.Parse(["notes.txt:0"]);

        Assert.Equal("notes.txt:0", parsed.Files[0].Path);
        Assert.Null(parsed.Files[0].Line);
    }

    [Theory]
    [InlineData("/p")]
    [InlineData("--print")]
    public void PrintSwitchIsRecognised(string arg)
    {
        var parsed = CommandLine.Parse([arg, "file.txt"]);

        Assert.True(parsed.Print);
        Assert.Single(parsed.Files);
    }

    [Theory]
    [InlineData("-n")]
    [InlineData("--new-window")]
    public void NewWindowSwitchIsRecognised(string arg)
    {
        Assert.True(CommandLine.Parse([arg]).NewWindow);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("/?")]
    public void HelpSwitchIsRecognised(string arg)
    {
        Assert.True(CommandLine.Parse([arg]).ShowHelp);
    }

    [Fact]
    public void AUnixStylePathIsNotTreatedAsASwitch()
    {
        var parsed = CommandLine.Parse(["/home/user/notes.txt"]);

        Assert.Single(parsed.Files);
        Assert.Empty(parsed.Unknown);
    }

    [Fact]
    public void UnknownSwitchesAreCollectedRatherThanOpened()
    {
        var parsed = CommandLine.Parse(["--nonsense", "real.txt"]);

        Assert.Single(parsed.Files);
        Assert.Single(parsed.Unknown);
        Assert.Equal("real.txt", parsed.Files[0].Path);
    }

    [Fact]
    public void EmptyAndWhitespaceArgumentsAreIgnored()
    {
        var parsed = CommandLine.Parse(["", "   ", "file.txt"]);

        Assert.Single(parsed.Files);
    }

    [Fact]
    public void MultipleFilesKeepTheirOrder()
    {
        var parsed = CommandLine.Parse(["a.txt", "b.txt:7", "c.txt"]);

        Assert.Equal(3, parsed.Files.Count);
        Assert.Equal("a.txt", parsed.Files[0].Path);
        Assert.Equal(7, parsed.Files[1].Line);
        Assert.Equal("c.txt", parsed.Files[2].Path);
    }
}
