namespace SamedisCare.Mail;

/// <summary>A file to attach.</summary>
/// <param name="FileName">The name the recipient sees.</param>
/// <param name="Content">The bytes.</param>
/// <param name="ContentType">MIME type, e.g. <c>application/pdf</c>.</param>
public sealed record MailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// One mail, in the form all three transports can carry.
/// </summary>
/// <param name="From">Sender address.</param>
/// <param name="To">Recipients. Empty means there is nothing to send.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">The HTML body. This is the body the recipient normally sees.</param>
/// <param name="TextBody">
/// A plain-text alternative, sent alongside the HTML as <c>multipart/alternative</c> so a
/// client that does not render HTML still shows something readable. Optional.
/// </param>
/// <param name="Attachments">Files to attach, or null.</param>
/// <remarks>
/// The two tools modelled this differently -- one carried <c>HtmlBody</c> plus an optional
/// <c>TextBody</c>, the other a single <c>Body</c> with an <c>IsHtml</c> flag -- and that one
/// difference accounted for nearly all of the divergence between their otherwise identical
/// transports. The richer form wins because the other is a special case of it:
/// <see cref="PlainText"/> builds it.
/// </remarks>
public sealed record MailMessage(
    string From,
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    IReadOnlyList<MailAttachment>? Attachments = null)
{
    /// <summary>A mail with no HTML part.</summary>
    /// <remarks>
    /// Graph is told the body is text rather than HTML, and the MIME build carries only the
    /// text part -- otherwise a plain body would arrive wrapped in a bodiless HTML alternative.
    /// </remarks>
    public static MailMessage PlainText(string from, IReadOnlyList<string> to, string subject,
                                        string body, IReadOnlyList<MailAttachment>? attachments = null)
        => new(from, to, subject, HtmlBody: string.Empty, TextBody: body, attachments) { IsHtml = false };

    /// <summary>Whether <see cref="HtmlBody"/> carries the body.</summary>
    public bool IsHtml { get; private init; } = true;
}
