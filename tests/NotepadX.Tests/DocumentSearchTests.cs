namespace NotepadX.Tests;

public class PlainSearchTests
{
    private static readonly SearchOptions Plain = new(MatchCase: false, WholeWord: false, UseRegex: false);
    private static readonly SearchOptions CaseSensitive = new(MatchCase: true, WholeWord: false, UseRegex: false);
    private static readonly SearchOptions WholeWords = new(MatchCase: false, WholeWord: true, UseRegex: false);

    [Fact]
    public void FindsTheFirstMatchAtOrAfterTheStart()
    {
        var hit = DocumentSearch.Find("one two one", "one", start: 1, backwards: false, Plain);

        Assert.NotNull(hit);
        Assert.Equal(8, hit!.Value.Index);
        Assert.Equal(3, hit.Value.Length);
    }

    [Fact]
    public void FindsBackwardsFromTheStartPosition()
    {
        var hit = DocumentSearch.Find("one two one", "one", start: 8, backwards: true, Plain);

        Assert.NotNull(hit);
        Assert.Equal(0, hit!.Value.Index);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoMatch()
    {
        Assert.Null(DocumentSearch.Find("abc", "zzz", 0, false, Plain));
    }

    [Fact]
    public void IsCaseInsensitiveByDefault()
    {
        Assert.Equal(3, DocumentSearch.Count("Foo foo FOO", "foo", Plain));
    }

    [Fact]
    public void MatchCaseNarrowsTheResults()
    {
        Assert.Equal(1, DocumentSearch.Count("Foo foo FOO", "foo", CaseSensitive));
    }

    [Fact]
    public void WholeWordRejectsMatchesInsideLongerWords()
    {
        // "concatenate" contains "cat" but must not count.
        Assert.Equal(2, DocumentSearch.Count("cat concatenate cat.", "cat", WholeWords));
        Assert.Equal(3, DocumentSearch.Count("cat concatenate cat.", "cat", Plain));
    }

    [Fact]
    public void UnderscoreCountsAsPartOfAWord()
    {
        Assert.Equal(0, DocumentSearch.Count("my_value", "value", WholeWords));
    }

    [Fact]
    public void PlainSearchTreatsRegexCharactersLiterally()
    {
        Assert.Equal(1, DocumentSearch.Count("a.c abc", "a.c", Plain));
    }

    [Fact]
    public void WrappingRestartsFromTheOtherEnd()
    {
        var hit = DocumentSearch.FindWrapped("one two", "one", start: 5, backwards: false, Plain, wrap: true);

        Assert.NotNull(hit);
        Assert.Equal(0, hit!.Value.Index);
    }

    [Fact]
    public void WithoutWrappingTheSearchStops()
    {
        Assert.Null(DocumentSearch.FindWrapped("one two", "one", 5, false, Plain, wrap: false));
    }

    [Fact]
    public void FindInRangeOnlyReturnsMatchesStartingInsideTheWindow()
    {
        const string text = "aaa bbb aaa bbb aaa";

        var hits = DocumentSearch.FindInRange(text, "aaa", Plain, from: 4, length: 10);

        Assert.Single(hits);
        Assert.Equal(8, hits[0].Index);
    }
}

public class RegexSearchTests
{
    private static readonly SearchOptions Regex = new(MatchCase: false, WholeWord: false, UseRegex: true);

    [Fact]
    public void MatchesAPattern()
    {
        Assert.Equal(3, DocumentSearch.Count("a1 b22 c333", @"\d+", Regex));
    }

    [Fact]
    public void ReportsTheMatchLength()
    {
        var hit = DocumentSearch.Find("value = 4200;", @"\d+", 0, false, Regex);

        Assert.NotNull(hit);
        Assert.Equal(8, hit!.Value.Index);
        Assert.Equal(4, hit.Value.Length);
    }

    [Fact]
    public void AnchorsWorkPerLineNotPerDocument()
    {
        // Multiline is on, so ^ matches at the start of each line, not just the document.
        Assert.Equal(2, DocumentSearch.Count("alpha\nbeta", @"^\w+", Regex));
        Assert.Equal(2, DocumentSearch.Count("alpha\nbeta", @"\w+$", Regex));
    }

    [Fact]
    public void ZeroLengthMatchesAreSkippedSoFindNextCannotStall()
    {
        Assert.Equal(0, DocumentSearch.Count("aaa", "b*", Regex));
    }

    [Fact]
    public void AnInvalidPatternReportsAnErrorInsteadOfThrowing()
    {
        var regex = DocumentSearch.TryBuild("(unclosed", Regex, out string? error);

        Assert.Null(regex);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void InvalidPatternsYieldNoMatches()
    {
        Assert.Equal(0, DocumentSearch.Count("anything", "[", Regex));
    }

    [Fact]
    public void GroupReferencesExpandInTheReplacement()
    {
        string result = DocumentSearch.ReplaceAll("John Smith", @"(\w+) (\w+)", "$2, $1", Regex, out int replaced);

        Assert.Equal(1, replaced);
        Assert.Equal("Smith, John", result);
    }

    [Fact]
    public void ExpandReplacementResolvesGroupsForASingleMatch()
    {
        const string text = "key=value";
        var hit = DocumentSearch.Find(text, @"(\w+)=(\w+)", 0, false, Regex)!.Value;

        string expanded = DocumentSearch.ExpandReplacement(text, hit, @"(\w+)=(\w+)", "$2=$1", Regex);

        Assert.Equal("value=key", expanded);
    }
}

public class ReplaceAllTests
{
    private static readonly SearchOptions Plain = new(false, false, false);
    private static readonly SearchOptions WholeWords = new(false, true, false);

    [Fact]
    public void ReplacesEveryOccurrenceAndReportsTheCount()
    {
        string result = DocumentSearch.ReplaceAll("a a a", "a", "b", Plain, out int replaced);

        Assert.Equal(3, replaced);
        Assert.Equal("b b b", result);
    }

    [Fact]
    public void LeavesTextAloneWhenNothingMatches()
    {
        string result = DocumentSearch.ReplaceAll("hello", "zzz", "x", Plain, out int replaced);

        Assert.Equal(0, replaced);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void WholeWordSkipsPartialMatchesButKeepsThemInTheOutput()
    {
        string result = DocumentSearch.ReplaceAll("cat concatenate", "cat", "dog", WholeWords, out int replaced);

        Assert.Equal(1, replaced);
        Assert.Equal("dog concatenate", result);
    }

    [Fact]
    public void PlainModeInsertsTheReplacementVerbatim()
    {
        string result = DocumentSearch.ReplaceAll("name", "name", "$1 literal", Plain, out _);

        Assert.Equal("$1 literal", result);
    }
}
