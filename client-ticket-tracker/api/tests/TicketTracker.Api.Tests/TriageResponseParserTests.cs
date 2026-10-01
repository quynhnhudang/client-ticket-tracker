using TicketTracker.Api.Domain;
using TicketTracker.Api.Services.Triage;

namespace TicketTracker.Api.Tests;

public class TriageResponseParserTests
{
    [Fact]
    public void Parses_plain_json()
    {
        var s = TriageResponseParser.Parse("""{"category":"Bug","priority":"High","reason":"Login broken for many."}""");

        Assert.NotNull(s);
        Assert.Equal(TicketCategory.Bug, s.Category);
        Assert.Equal(TicketPriority.High, s.Priority);
        Assert.Equal("Login broken for many.", s.Reason);
    }

    [Fact]
    public void Tolerates_prose_fences_and_casing()
    {
        var s = TriageResponseParser.Parse("Sure!\n```json\n{\"category\": \"data fix\", \"priority\": \"urgent\", \"reason\": \"x\"}\n```");

        Assert.NotNull(s);
        Assert.Equal(TicketCategory.DataFix, s.Category);
        Assert.Equal(TicketPriority.Urgent, s.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I think it's a bug, high priority.")]                          // no JSON
    [InlineData("""{"category":"Feature","priority":"High"}""")]                 // unknown category
    [InlineData("""{"category":"Bug","priority":"Critical"}""")]                 // unknown priority
    [InlineData("""{"category":"Bug","priority":"3"}""")]                        // number disguised as a name
    [InlineData("""{"category":"Bug,Question","priority":"Low"}""")]             // enum combination
    [InlineData("""{"category":1,"priority":"Low"}""")]                          // not a string
    [InlineData("""{"category":"Bug" "priority":"Low"}""")]                      // malformed JSON
    public void Rejects_anything_invalid(string? text)
    {
        Assert.Null(TriageResponseParser.Parse(text));
    }

    [Fact]
    public void Long_reasons_are_truncated()
    {
        var s = TriageResponseParser.Parse($$"""{"category":"Bug","priority":"Low","reason":"{{new string('x', 1000)}}"}""");

        Assert.NotNull(s);
        Assert.Equal(TriageResponseParser.MaxReasonLength, s.Reason.Length);
    }
}
