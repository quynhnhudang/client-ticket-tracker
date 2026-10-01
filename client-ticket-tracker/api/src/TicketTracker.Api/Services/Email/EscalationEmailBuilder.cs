using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketTracker.Api.Services.Email;

/// <summary>Everything the escalation email shows. Plain values; the builder does all escaping.</summary>
public sealed record EscalationEmailModel(
    int TicketId,
    string Title,
    string Description,
    string ClientName,
    string ClientEmail,
    string Priority,
    string Category,
    string EscalatedBy,
    DateTimeOffset EscalatedAt,
    DateTimeOffset CreatedAt,
    string EscalationNote,
    string TicketUrl);

public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Turns an EscalationEmailModel into a subject, an HTML body and a plain-text body.
///
/// Pure: no SMTP, no database, no clock. That makes it unit-testable, and it's
/// how email/escalation-sample.html was produced.
///
/// The template is an embedded resource (compiled into the DLL; see the .csproj),
/// so it can't go missing from a deployment or be read from the wrong working directory.
/// </summary>
public static partial class EscalationEmailBuilder
{
    private const string TemplateResource = "TicketTracker.Api.Templates.escalation.html";

    private static readonly Lazy<string> Template = new(LoadTemplate);

    public static EmailContent Build(EscalationEmailModel m)
    {
        var values = new Dictionary<string, string>
        {
            ["TicketId"] = m.TicketId.ToString(CultureInfo.InvariantCulture),
            ["Title"] = Html(m.Title),
            ["ClientName"] = Html(m.ClientName),
            ["ClientEmail"] = Html(m.ClientEmail),
            ["Priority"] = Html(m.Priority),
            ["PriorityColor"] = PriorityColor(m.Priority),
            ["Category"] = Html(m.Category),
            ["EscalatedBy"] = Html(m.EscalatedBy),
            ["EscalatedAt"] = Html(FormatUtc(m.EscalatedAt)),
            ["CreatedAt"] = Html(FormatUtc(m.CreatedAt)),
            ["EscalationNote"] = HtmlMultiline(m.EscalationNote),
            ["Description"] = HtmlMultiline(m.Description),
            ["TicketUrl"] = Html(m.TicketUrl),
            ["Preheader"] = Html(Truncate($"{m.Priority} · {m.ClientName}: {m.EscalationNote}", 140)),
        };

        var html = PlaceholderPattern().Replace(StripAuthorComments(Template.Value), match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Template placeholder {match.Value} has no value."));

        return new EmailContent(Subject(m), html, PlainText(m));
    }

    /// <summary>
    /// "[Escalated][Urgent] #42 Password reset email never arrives (Brightwater Credit Union)"
    /// Priority first, so filters and humans can triage from the inbox list.
    /// CR/LF are removed: a newline in a header value is how "header injection"
    /// attacks add extra headers (e.g. a Bcc). MailKit encodes headers safely too,
    /// but the subject is built from client-supplied text, so it's sanitised here as well.
    /// </summary>
    public static string Subject(EscalationEmailModel m)
    {
        var subject = $"[Escalated][{m.Priority}] #{m.TicketId} {m.Title} ({m.ClientName})";
        return Truncate(subject.Replace('\r', ' ').Replace('\n', ' '), 200);
    }

    /// <summary>
    /// The text/plain alternative. Sent alongside the HTML (multipart/alternative):
    /// some people and tools read plain text only, and a message with an HTML part
    /// but no text part scores worse with spam filters.
    /// </summary>
    public static string PlainText(EscalationEmailModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ESCALATED TO ENGINEERING: ticket #{m.TicketId}");
        sb.AppendLine(m.Title);
        sb.AppendLine();
        sb.AppendLine($"Priority:     {m.Priority}");
        sb.AppendLine($"Client:       {m.ClientName} <{m.ClientEmail}>");
        sb.AppendLine($"Category:     {m.Category}");
        sb.AppendLine($"Escalated by: {m.EscalatedBy}");
        sb.AppendLine($"Escalated at: {FormatUtc(m.EscalatedAt)}");
        sb.AppendLine($"Opened:       {FormatUtc(m.CreatedAt)}");
        sb.AppendLine();
        sb.AppendLine("Why it was escalated:");
        sb.AppendLine(m.EscalationNote);
        sb.AppendLine();
        sb.AppendLine("Client's description:");
        sb.AppendLine(m.Description);
        sb.AppendLine();
        sb.AppendLine($"View the ticket: {m.TicketUrl}");
        return sb.ToString();
    }

    /// <summary>
    /// HTML-encodes text from the database. Titles, descriptions and notes are
    /// typed by clients and agents; without encoding, a description containing
    /// &lt;a href="https://phish.example"&gt;Reset your password&lt;/a&gt; would render as a
    /// real link inside an official-looking email. Encoding shows it as text.
    /// </summary>
    private static string Html(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Encode first, THEN turn newlines into &lt;br&gt;, so the only tags that reach the email are ours.</summary>
    private static string HtmlMultiline(string value) =>
        Html(value.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>\n");

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("MMM d, yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string PriorityColor(string priority) => priority switch
    {
        "Urgent" => "#a11d14",
        "High" => "#c2410c",
        "Medium" => "#8a5a00",
        _ => "#4a5566",
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>
    /// Removes the explanatory comments from the template before sending, so they
    /// don't bloat every message (Gmail clips emails over ~102 KB) or show up in
    /// "view source". Keeps Outlook conditional comments, which are functional:
    ///   &lt;!--[if mso]&gt; ... &lt;![endif]--&gt;     (Outlook only)
    ///   &lt;!--[if !mso]&gt;&lt;!--&gt; ... &lt;!--&lt;![endif]--&gt;   (everyone except Outlook)
    /// </summary>
    internal static string StripAuthorComments(string html) => AuthorComment().Replace(html, "");

    private static string LoadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"Embedded resource {TemplateResource} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderPattern();

    // "<!--" not followed by "[if", "<![endif]" or ">" (the "<!-->" in the
    // non-Outlook opener), up to the first "-->". Trailing whitespace too.
    [GeneratedRegex(@"<!--(?!\[if)(?!<!\[endif\])(?!>)[\s\S]*?-->[ \t]*(\r?\n)?")]
    private static partial Regex AuthorComment();
}
