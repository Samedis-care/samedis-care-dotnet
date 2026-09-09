using FluentAssertions;
using MimeKit;
using SamedisCare.Helper.Logging;
using SamedisCare.Mail;
using Xunit;

namespace SamedisCare.Mail.Tests;

/// <summary>Collects what was logged, so a test can assert what an operator would read.</summary>
internal sealed class CapturingLog : ISyncLog
{
    public int Level => 2;
    public List<string> Lines { get; } = new();

    public void Info(string message) => Lines.Add($"INFO {message}");
    public void Warn(string message) => Lines.Add($"WARN {message}");
    public void Error(string message, Exception? ex = null)
        => Lines.Add($"ERROR {message}{(ex == null ? "" : ": " + ex.Message)}");
    public void Debug(string message) => Lines.Add($"DEBUG {message}");
}

/// <summary>
/// What reaches the wire. The transports themselves need a server, but the message they build
/// does not — and that is where the two tools differed, so it is what these tests hold.
/// </summary>
public class MimeBuildingTests
{
    private static readonly string[] To = { "a@example.org", "b@example.org" };

    private static MimeMessage Build(MailMessage message) => Mailer.BuildMimeMessage(message);

    [Fact]
    public void An_html_mail_carries_the_html_and_the_text_alternative()
    {
        var mime = Build(new MailMessage("from@example.org", To, "Report",
                                         "<p>Bericht</p>", "Bericht"));

        mime.HtmlBody.Should().Be("<p>Bericht</p>");
        mime.TextBody.Should().Be("Bericht");
        mime.To.Count.Should().Be(2);
        mime.Subject.Should().Be("Report");
    }

    [Fact]
    public void An_html_mail_without_a_text_alternative_carries_only_html()
    {
        var mime = Build(new MailMessage("from@example.org", To, "Report", "<p>Bericht</p>"));

        mime.HtmlBody.Should().Be("<p>Bericht</p>");
        mime.TextBody.Should().BeNull();
    }

    // The case the other tool modelled as IsHtml=false. An empty HTML part must not be
    // written: some clients pick the alternative they are given and show a blank mail.
    [Fact]
    public void A_plain_text_mail_has_no_html_part_at_all()
    {
        var mime = Build(MailMessage.PlainText("from@example.org", To, "Report", "Bericht"));

        mime.TextBody.Should().Be("Bericht");
        mime.HtmlBody.Should().BeNull("an empty HTML alternative is what some clients display");
    }

    [Fact]
    public void Attachments_keep_their_name_and_type()
    {
        var mime = Build(new MailMessage("from@example.org", To, "Report", "<p>x</p>",
            Attachments: new[] { new MailAttachment("bericht.pdf", new byte[] { 1, 2, 3 },
                                                    "application/pdf") }));

        var attachment = mime.Attachments.Single();
        attachment.ContentType.MimeType.Should().Be("application/pdf");
        attachment.ContentDisposition!.FileName.Should().Be("bericht.pdf");
    }
}

/// <summary>
/// What the sender does before it reaches a transport. A run that has done its work must not
/// be lost because a mail server is down, so every refusal is reported and returns false.
/// </summary>
public class SendingGuardTests
{
    private static MailMessage Message
        => new("from@example.org", new[] { "to@example.org" }, "Report", "<p>x</p>");

    private static (Mailer Mailer, CapturingLog Log) For(MailSettings settings)
    {
        var log = new CapturingLog();
        return (new Mailer(settings, log, "Tests"), log);
    }

    [Fact]
    public async Task Mail_switched_off_sends_nothing_and_says_so()
    {
        var (mailer, log) = For(new MailSettings { Enabled = false });

        (await mailer.SendAsync(Message, "report")).Should().BeFalse();
        log.Lines.Should().ContainSingle(l => l.Contains("switched off"));
    }

    [Fact]
    public async Task Without_recipients_nothing_is_sent()
    {
        var (mailer, log) = For(new MailSettings { Enabled = true, Provider = "smtp" });

        (await mailer.SendAsync(Message with { To = Array.Empty<string>() }, "report"))
            .Should().BeFalse();
        log.Lines.Should().ContainSingle(l => l.StartsWith("ERROR"));
    }

    [Fact]
    public async Task An_unknown_provider_is_named_in_the_log()
    {
        var (mailer, log) = For(new MailSettings { Enabled = true, Provider = "carrier-pigeon" });

        (await mailer.SendAsync(Message, "report")).Should().BeFalse();
        log.Lines.Should().Contain(l => l.Contains("carrier-pigeon"));
    }

    // The configuration is incomplete rather than the server unreachable, but both come back
    // the same way: reported, false, and the run carries on.
    [Fact]
    public async Task An_incomplete_transport_configuration_is_reported_not_thrown()
    {
        var (mailer, log) = For(new MailSettings { Enabled = true, Provider = "graph" });

        (await mailer.SendAsync(Message, "report")).Should().BeFalse();
        log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("report"));
    }

    [Theory]
    [InlineData("SMTP")]
    [InlineData(" Graph ")]
    [InlineData("gmail")]
    public async Task The_provider_name_is_read_case_and_space_insensitively(string provider)
    {
        var (mailer, log) = For(new MailSettings { Enabled = true, Provider = provider });

        await mailer.SendAsync(Message, "report");

        log.Lines.Should().NotContain(l => l.Contains("Unknown mail provider"));
    }

    [Fact]
    public void Recipients_are_trimmed_deduplicated_and_stripped_of_blanks()
    {
        var (mailer, _) = For(new MailSettings
        {
            Recipients = new List<string> { " a@example.org ", "A@Example.org", "", "  ", "b@example.org" },
        });

        mailer.Recipients().Should().Equal("a@example.org", "b@example.org");
    }
}
