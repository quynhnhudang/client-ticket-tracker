using TicketTracker.Api.Services.Email;

namespace TicketTracker.Api.Tests;

public class EscalationEmailBuilderTests
{
    private static EscalationEmailModel Model(string description = "Members can't reset passwords.\nSince 9am.") => new(
        TicketId: 42,
        Title: "Password reset email never arrives",
        Description: description,
        ClientName: "Brightwater Credit Union",
        ClientEmail: "support@brightwater.example",
        Priority: "Urgent",
        Category: "Bug",
        EscalatedBy: "Sam Agent",
        EscalatedAt: new DateTimeOffset(2026, 9, 25, 18, 46, 0, TimeSpan.Zero),
        CreatedAt: new DateTimeOffset(2026, 9, 25, 9, 5, 0, TimeSpan.Zero),
        EscalationNote: "Mail queue looks stuck.",
        TicketUrl: "https://tickets.example.com/tickets/42");

    [Fact]
    public void Every_placeholder_is_filled()
    {
        var html = EscalationEmailBuilder.Build(Model()).HtmlBody;

        Assert.DoesNotContain("{{", html);
        Assert.Contains("#42", html);
        Assert.Contains("https://tickets.example.com/tickets/42", html);
        Assert.Contains("Sep 25, 2026 18:46 UTC", html);
    }

    [Fact]
    public void Html_follows_the_email_rules()
    {
        var html = EscalationEmailBuilder.Build(Model()).HtmlBody;

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:flex", html.Replace(" ", ""));
        Assert.DoesNotContain("display:grid", html.Replace(" ", ""));
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Author_comments_are_stripped_but_outlook_conditionals_kept()
    {
        var html = EscalationEmailBuilder.Build(Model()).HtmlBody;

        Assert.DoesNotContain("NOTE:", html);
        Assert.Contains("<!--[if mso]>", html);
        Assert.Contains("<!--[if !mso]><!-->", html);
        Assert.Contains("<!--<![endif]-->", html);
        Assert.Contains("v:roundrect", html);
        // The non-Outlook button must survive the comment stripping.
        Assert.Contains("<a href=\"https://tickets.example.com/tickets/42\"", html);
    }

    [Fact]
    public void User_text_is_html_encoded()
    {
        var evil = "<a href=\"https://phish.example\">Reset your password</a><script>x()</script>";

        var html = EscalationEmailBuilder.Build(Model(description: evil)).HtmlBody;

        Assert.DoesNotContain("<a href=\"https://phish.example\"", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;a href=&quot;https://phish.example&quot;&gt;", html);
    }

    [Fact]
    public void Placeholder_syntax_in_user_text_is_not_expanded()
    {
        var html = EscalationEmailBuilder.Build(Model(description: "see {{TicketUrl}}")).HtmlBody;

        Assert.Contains("see {{TicketUrl}}", html);
    }

    [Fact]
    public void Newlines_become_line_breaks_after_encoding()
    {
        var html = EscalationEmailBuilder.Build(Model(description: "line one\nline <two>")).HtmlBody;

        Assert.Contains("line one<br>\nline &lt;two&gt;", html);
    }

    [Fact]
    public void Subject_leads_with_priority_and_has_no_line_breaks()
    {
        var subject = EscalationEmailBuilder.Build(Model() with { Title = "Broken\r\nBcc: attacker@example.com" }).Subject;

        Assert.StartsWith("[Escalated][Urgent] #42 ", subject);
        Assert.DoesNotContain("\n", subject);
        Assert.DoesNotContain("\r", subject);
    }

    [Fact]
    public void Plain_text_part_has_the_essentials()
    {
        var text = EscalationEmailBuilder.Build(Model()).TextBody;

        Assert.Contains("ticket #42", text);
        Assert.Contains("Mail queue looks stuck.", text);
        Assert.Contains("https://tickets.example.com/tickets/42", text);
    }
}
