using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Tests;

public class TicketStatusRulesTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Escalated, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Escalated, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    public void Allowed_transitions_pass_when_required_text_is_present(TicketStatus from, TicketStatus to)
    {
        var error = TicketStatusRules.Validate(from, to, note: "a note", resolutionNotes: "fixed it");

        Assert.Null(error);
    }

    [Theory]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Escalated)]
    [InlineData(TicketStatus.Escalated, TicketStatus.Open)]
    public void Disallowed_transitions_are_rejected(TicketStatus from, TicketStatus to)
    {
        var error = TicketStatusRules.Validate(from, to, note: "a note", resolutionNotes: "fixed it");

        Assert.NotNull(error);
        Assert.Contains("Cannot move", error);
    }

    [Fact]
    public void Same_status_is_rejected()
    {
        Assert.NotNull(TicketStatusRules.Validate(TicketStatus.Open, TicketStatus.Open, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Escalation_requires_a_note(string? note)
    {
        var error = TicketStatusRules.Validate(TicketStatus.InProgress, TicketStatus.Escalated, note, null);

        Assert.NotNull(error);
        Assert.Contains("escalation", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Resolving_requires_resolution_notes(string? notes)
    {
        var error = TicketStatusRules.Validate(TicketStatus.InProgress, TicketStatus.Resolved, null, notes);

        Assert.NotNull(error);
        Assert.Contains("Resolution notes", error);
    }

    [Fact]
    public void Every_status_has_at_least_one_way_out()
    {
        // Guards against adding a new status and forgetting to give it transitions,
        // which would leave tickets stuck in it forever.
        foreach (var status in Enum.GetValues<TicketStatus>())
            Assert.NotEmpty(TicketStatusRules.AllowedNext(status));
    }
}
