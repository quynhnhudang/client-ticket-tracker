using TicketTracker.Api.Data.Queries;

namespace TicketTracker.Api.Tests;

public class SearchInputTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("#42", 42)]
    [InlineData("  17  ", 17)]
    public void Numbers_are_ticket_ids(string input, int expected)
    {
        Assert.True(SearchInput.TryParseTicketId(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("42abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1 2")]
    [InlineData("1,000")]
    [InlineData("99999999999")] // larger than int.MaxValue
    public void Everything_else_is_not_a_ticket_id(string input)
    {
        Assert.False(SearchInput.TryParseTicketId(input, out _));
    }

    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("50%", @"50\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData("[x]", @"\[x]")]
    [InlineData(@"C:\temp", @"C:\\temp")]
    [InlineData(@"\%", @"\\\%")] // backslash escaped first, so it isn't double-escaped
    public void Like_wildcards_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, SearchInput.EscapeLike(input));
    }

    [Theory]
    [InlineData("login timeout", "\"login*\" AND \"timeout*\"")]
    [InlineData("  Login   TIMEOUT!! ", "\"login*\" AND \"timeout*\"")]
    [InlineData("export export", "\"export*\"")]                  // duplicates removed
    [InlineData("a b report", "\"report*\"")]                     // one-letter words dropped
    [InlineData("\"NEAR(x,y)\" OR 1=1", "\"near*\" AND \"or*\"")] // no operator/quote survives; 1-letter tokens dropped
    [InlineData("café", "\"café*\"")]                            // accented letters are letters
    public void Full_text_terms_are_built_safely(string input, string expected)
    {
        Assert.Equal(expected, SearchInput.BuildFullTextTerm(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!! ??? ...")]
    [InlineData("a b c")]
    public void Full_text_term_is_null_when_nothing_usable_remains(string input)
    {
        Assert.Null(SearchInput.BuildFullTextTerm(input));
    }

    [Fact]
    public void Full_text_term_is_capped()
    {
        var input = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"word{i}"));

        var term = SearchInput.BuildFullTextTerm(input)!;

        Assert.Equal(SearchInput.MaxKeywordTerms, term.Split(" AND ").Length);
    }
}
