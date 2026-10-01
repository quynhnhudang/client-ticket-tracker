using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TicketTracker.Api.Services.Email;

public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> to, EmailContent content, CancellationToken ct);
}

/// <summary>
/// Sends mail over SMTP with MailKit.
///
/// Why MailKit and not System.Net.Mail.SmtpClient? Microsoft's own docs mark
/// SmtpClient as not recommended for new development: it doesn't support many
/// modern protocols. MailKit is the library those docs point to instead.
///
/// SMTP rather than a provider's HTTP API keeps the app provider-neutral:
/// switching from Mailtrap (testing) to SendGrid or any other provider is a
/// configuration change, not a code change.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(IReadOnlyList<string> to, EmailContent content, CancellationToken ct)
    {
        var o = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        foreach (var address in to)
            message.To.Add(MailboxAddress.Parse(address));
        if (!string.IsNullOrWhiteSpace(o.ReplyToAddress))
            message.ReplyTo.Add(MailboxAddress.Parse(o.ReplyToAddress));
        message.Subject = content.Subject;

        // BodyBuilder with both bodies produces multipart/alternative: the mail
        // client shows the best part it can render (HTML), others fall back to text.
        message.Body = new BodyBuilder { HtmlBody = content.HtmlBody, TextBody = content.TextBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15_000 }; // ms; don't hang a request on a slow server

        var security = o.Security switch
        {
            // StartTls, not StartTlsWhenAvailable: if the server doesn't offer TLS,
            // fail instead of sending the password in plain text.
            SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.None,
        };

        await client.ConnectAsync(o.Host, o.Port, security, ct);
        if (!string.IsNullOrEmpty(o.Username))
            await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Sent \"{Subject}\" to {Recipients}.", content.Subject, string.Join(", ", to));
    }
}
