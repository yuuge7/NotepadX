namespace NotepadX.Tests;

public class LineToolTests
{
    [Fact]
    public void SortsLinesAlphabetically()
    {
        Assert.Equal("apple\nbanana\ncherry", TextTools.SortLines("cherry\napple\nbanana"));
    }

    [Fact]
    public void SortsDescendingWhenAsked()
    {
        Assert.Equal("cherry\nbanana\napple", TextTools.SortLines("apple\ncherry\nbanana", descending: true));
    }

    [Fact]
    public void PreservesCarriageReturnLineFeeds()
    {
        Assert.Equal("a\r\nb\r\nc", TextTools.SortLines("c\r\na\r\nb"));
    }

    [Fact]
    public void ATrailingBlankLineStaysAtTheBottom()
    {
        Assert.Equal("a\nb\n", TextTools.SortLines("b\na\n"));
    }

    [Fact]
    public void ReversesLineOrder()
    {
        Assert.Equal("c\nb\na", TextTools.ReverseLines("a\nb\nc"));
    }

    [Fact]
    public void RemovesDuplicatesKeepingTheFirstOccurrence()
    {
        Assert.Equal("a\nb\nc", TextTools.RemoveDuplicateLines("a\nb\na\nc\nb"));
    }

    [Fact]
    public void DuplicateRemovalIsCaseSensitiveByDefault()
    {
        Assert.Equal("a\nA", TextTools.RemoveDuplicateLines("a\nA"));
        Assert.Equal("a", TextTools.RemoveDuplicateLines("a\nA", ignoreCase: true));
    }

    [Fact]
    public void RemovesBlankAndWhitespaceOnlyLines()
    {
        Assert.Equal("a\nb", TextTools.RemoveEmptyLines("a\n\n   \nb"));
    }

    [Fact]
    public void TrimsTrailingSpacesAndTabsOnly()
    {
        Assert.Equal("  indented\nb", TextTools.TrimTrailingWhitespace("  indented   \nb\t"));
    }

    [Fact]
    public void JoinsLinesWithASingleSpace()
    {
        Assert.Equal("one two three", TextTools.JoinLines("one\ntwo\nthree"));
    }

    [Fact]
    public void JoiningKeepsATrailingNewline()
    {
        Assert.Equal("one two\n", TextTools.JoinLines("one\ntwo\n"));
    }
}

public class CaseToolTests
{
    [Fact]
    public void UppercasesAndLowercases()
    {
        Assert.Equal("HELLO", TextTools.ToUpper("Hello"));
        Assert.Equal("hello", TextTools.ToLower("HeLLo"));
    }

    [Fact]
    public void TitleCaseCapitalisesEachWord()
    {
        Assert.Equal("The Quick Brown Fox", TextTools.ToTitleCase("the quick brown fox"));
    }

    [Fact]
    public void TitleCaseLowercasesTheRestOfEachWord()
    {
        Assert.Equal("Shouting Text", TextTools.ToTitleCase("SHOUTING TEXT"));
    }

    [Fact]
    public void TitleCaseKeepsApostrophesInsideWords()
    {
        Assert.Equal("Don't Stop", TextTools.ToTitleCase("don't stop"));
    }

    [Fact]
    public void InvertCaseSwapsEveryLetter()
    {
        Assert.Equal("hELLO wORLD", TextTools.ToggleCase("Hello World"));
    }

    [Fact]
    public void NonLettersAreLeftAlone()
    {
        Assert.Equal("A1-B2", TextTools.ToggleCase("a1-b2"));
    }
}

public class WordCountTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   \t\n  ", 0)]
    [InlineData("one", 1)]
    [InlineData("one two three", 3)]
    [InlineData("  padded   words  ", 2)]
    [InlineData("across\nlines\ttoo", 3)]
    public void CountsWhitespaceSeparatedRuns(string text, int expected)
    {
        Assert.Equal(expected, TextTools.CountWords(text));
    }
}
