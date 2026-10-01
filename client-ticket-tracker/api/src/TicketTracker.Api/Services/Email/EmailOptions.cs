using System.ComponentModel.DataAnnotations;

namespace TicketTracker.Api.Services.Email;

/// <summary>
/// Bound from the "Email" configuration section. Secrets (Username/Password)
/// come from user-secrets in development and the server's environment file in
/// production (Email__Password=...), never from appsettings.json.
///
/// If Email:Host is empty, email is off and escalations are only logged
/// (see Program.cs), so the app still runs without an SMTP account.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>e.g. sandbox.smtp.mailtrap.io or smtp.sendgrid.net</summary>
    [Required] public string Host { get; set; } = "";

    [Range(1, 65535)] public int Port { get; set; } = 587;

    /// <summary>
    /// StartTls (port 587/2525: connect in plain text, then upgrade, and REQUIRE the upgrade),
    /// SslOnConnect (port 465: TLS from the first byte), or None (local test servers only).
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }
    public string? Password { get; set; }

    [Required, EmailAddress] public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Client Ticket Tracker";

    /// <summary>Who gets escalation emails. Comma-separated for several addresses.</summary>
    [Required] public string EngineeringAddresses { get; set; } = "";

    /// <summary>Optional: replies go here (e.g. the support team's shared inbox) instead of the no-reply From address.</summary>
    [EmailAddress] public string? ReplyToAddress { get; set; }

    /// <summary>Base URL of the web app, for the "View ticket" link, e.g. http://203.0.113.10 or https://tickets.example.com</summary>
    [Required, Url] public string AppBaseUrl { get; set; } = "";

    public IReadOnlyList<string> EngineeringAddressList =>
        EngineeringAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public enum SmtpSecurity
{
    StartTls,
    SslOnConnect,
    None,
}
