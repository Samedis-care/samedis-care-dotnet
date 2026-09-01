using System.Text;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Mail;

/// <summary>
/// Sends a <see cref="MailMessage"/> over whichever transport the configuration names.
/// </summary>
/// <remarks>
/// <para>
/// log-monitor and requests-to-mail each carried this, and a line-by-line comparison found
/// the SMTP path identical to the character and the Graph and Gmail paths within four lines
/// of each other. What differed was how they modelled the body -- see
/// <see cref="MailMessage"/> -- and the wording of their log messages.
/// </para>
/// <para>
/// A failed send is reported and returns false rather than throwing. Sending a report is not
/// what the tools are for, and a mail server that is down should not lose a run that has
/// already done its work. The caller decides whether that matters; the exit code is its
/// business, not this class's.
/// </para>
/// </remarks>
public sealed class Mailer
{
    private readonly MailSettings _settings;
    private readonly ISyncLog _log;
    private readonly string _applicationName;

    /// <param name="settings">The <c>mail</c> section of the tool's configuration.</param>
    /// <param name="log">Where a failed send is reported.</param>
    /// <param name="applicationName">
    /// Identifies the caller to the Gmail API. Shows up in Google's audit log, so it should
    /// name the tool rather than the library.
    /// </param>
    public Mailer(MailSettings settings, ISyncLog log, string applicationName)
    {
        _settings = settings;
        _log = log;
        _applicationName = applicationName;
    }

    /// <summary>
    /// The configured recipients, trimmed, de-duplicated and without the blanks a
    /// hand-edited config.yml collects.
    /// </summary>
    public IReadOnlyList<string> Recipients()
        => (_settings.Recipients ?? new List<string>())
           .Where(r => !string.IsNullOrWhiteSpace(r))
           .Select(r => r.Trim())
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .ToList();

    /// <summary>
    /// Sends the message. False when mail is switched off, the configuration is incomplete,
    /// or the transport refused it -- each reported through the log.
    /// </summary>
    /// <param name="message">What to send.</param>
    /// <param name="label">
    /// What the log calls this mail, e.g. "log monitor report". Appears in both the success
    /// and the failure line, so an operator can tell which mail did not go out.
    /// </param>
    public async Task<bool> SendAsync(MailMessage message, string label)
    {
        if (!_settings.Enabled)
        {
            _log.Debug($"Mail is switched off; {label} not sent.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.From) || message.To.Count == 0)
        {
            _log.Error($"Cannot send {label}: sender or recipients missing.");
            return false;
        }

        var provider = (_settings.Provider ?? "smtp").Trim().ToLowerInvariant();
        _log.Info($"Sending {label} using provider: {provider}");

        try
        {
            switch (provider)
            {
                case "smtp": await SendViaSmtpAsync(message); break;
                case "graph": await SendViaGraphAsync(message); break;
                case "gmail": await SendViaGmailAsync(message); break;
                default:
                    _log.Error($"Unknown mail provider '{provider}'.");
                    return false;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Sending {label} failed", ex);
            return false;
        }

        _log.Info($"{label} sent successfully.");
        return true;
    }

    private async Task SendViaSmtpAsync(MailMessage message)
    {
        var smtp = _settings.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Server) || smtp.Port <= 0)
            throw new InvalidOperationException("SMTP server/port not configured.");

        var security = smtp.UseSsl ? SecureSocketOptions.SslOnConnect
                     : smtp.UseStartTls ? SecureSocketOptions.StartTls
                     : SecureSocketOptions.None;

        using var client = new SmtpClient();
        if (smtp.IgnoreCertificateErrors)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        await client.ConnectAsync(smtp.Server, smtp.Port, security);

        if (!string.IsNullOrWhiteSpace(smtp.Username))
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty);

        await client.SendAsync(BuildMimeMessage(message));
        await client.DisconnectAsync(true);
    }

    private async Task SendViaGraphAsync(MailMessage message)
    {
        var graph = _settings.Graph;
        if (string.IsNullOrWhiteSpace(graph.TenantId)
            || string.IsNullOrWhiteSpace(graph.ClientId)
            || string.IsNullOrWhiteSpace(graph.ClientSecret)
            || string.IsNullOrWhiteSpace(graph.SenderUserPrincipalName))
            throw new InvalidOperationException("Graph mail configuration is incomplete.");

        var credential = new ClientSecretCredential(graph.TenantId, graph.ClientId, graph.ClientSecret);
        var client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });

        var outgoing = new Microsoft.Graph.Models.Message
        {
            Subject = message.Subject,
            Body = new ItemBody
            {
                ContentType = message.IsHtml ? BodyType.Html : BodyType.Text,
                Content = message.IsHtml ? message.HtmlBody : message.TextBody ?? string.Empty,
            },
            ToRecipients = message.To
                .Select(to => new Recipient { EmailAddress = new EmailAddress { Address = to } })
                .ToList(),
        };

        if (message.Attachments is { Count: > 0 })
            outgoing.Attachments = message.Attachments
                .Select(a => (Attachment)new FileAttachment
                {
                    OdataType = "#microsoft.graph.fileAttachment",
                    Name = a.FileName,
                    ContentType = a.ContentType,
                    ContentBytes = a.Content,
                })
                .ToList();

        await client.Users[graph.SenderUserPrincipalName]
                    .SendMail.PostAsync(new SendMailPostRequestBody
                    {
                        Message = outgoing,
                        SaveToSentItems = true,
                    });
    }

    private async Task SendViaGmailAsync(MailMessage message)
    {
        var gmail = _settings.Gmail;
        if (string.IsNullOrWhiteSpace(gmail.ServiceAccountJsonPath)
            || string.IsNullOrWhiteSpace(gmail.ImpersonatedUser))
            throw new InvalidOperationException("Gmail mail configuration is incomplete.");

        if (!File.Exists(gmail.ServiceAccountJsonPath))
            throw new FileNotFoundException("Gmail service account JSON not found.",
                                            gmail.ServiceAccountJsonPath);

        var credential = CredentialFactory.FromFile<ServiceAccountCredential>(gmail.ServiceAccountJsonPath)
            .ToGoogleCredential()
            .CreateScoped(GmailService.Scope.GmailSend)
            .CreateWithUser(gmail.ImpersonatedUser);

        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _applicationName,
        });

        await service.Users.Messages
                     .Send(new Google.Apis.Gmail.v1.Data.Message { Raw = Base64Url(message) }, "me")
                     .ExecuteAsync();
    }

    /// <summary>
    /// The MIME message, base64url-encoded the way the Gmail API wants its <c>raw</c> field:
    /// the URL-safe alphabet and no padding.
    /// </summary>
    private static string Base64Url(MailMessage message)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMimeMessage(message).ToString()))
                  .Replace("+", "-").Replace("/", "_").Replace("=", "");

    internal static MimeMessage BuildMimeMessage(MailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From));
        foreach (var to in message.To)
            mime.To.Add(MailboxAddress.Parse(to));

        mime.Subject = message.Subject;

        var body = new BodyBuilder();
        if (message.IsHtml)
        {
            body.HtmlBody = message.HtmlBody;
            // A plain-text alternative next to the HTML, so a client that does not render
            // the HTML part still shows something readable (multipart/alternative).
            if (!string.IsNullOrWhiteSpace(message.TextBody))
                body.TextBody = message.TextBody;
        }
        else
        {
            // No empty HTML part: an alternative with nothing in it is what some clients
            // choose to display, and the recipient then sees a blank mail.
            body.TextBody = message.TextBody ?? string.Empty;
        }

        foreach (var attachment in message.Attachments ?? Array.Empty<MailAttachment>())
            body.Attachments.Add(attachment.FileName, attachment.Content,
                                 MimeKit.ContentType.Parse(attachment.ContentType));

        mime.Body = body.ToMessageBody();
        return mime;
    }
}
