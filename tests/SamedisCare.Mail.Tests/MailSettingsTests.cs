using FluentAssertions;
using SamedisCare.Helper.Config;
using SamedisCare.Mail;
using Xunit;

namespace SamedisCare.Mail.Tests;

/// <summary>
/// The tools' own SmtpConfig defaulted the port to 587; the package's did not, so a config.yml
/// without <c>port:</c> loaded as 0 and every report mail failed with "SMTP server/port not
/// configured". See samedis-care-issues#2875. Loaded through ConfigStore, the way the tools do.
/// </summary>
public class MailSettingsTests
{
    private class ToolConfig
    {
        public MailSettings Mail { get; set; } = new();
    }

    private static ToolConfig Parse(string yaml)
        => ConfigStore.Parse<ToolConfig>(yaml, ignoreUnmatchedProperties: true);

    [Fact]
    public void A_smtp_section_without_a_port_means_587()
        => Parse("mail:\n  smtp:\n    server: relay.example.org\n").Mail.Smtp.Port.Should().Be(587);

    [Fact]
    public void An_empty_smtp_section_means_587()
        => Parse("mail:\n  smtp:\n").Mail.Smtp.Port.Should().Be(587);

    [Fact]
    public void A_missing_mail_section_means_587()
        => Parse("").Mail.Smtp.Port.Should().Be(587);

    [Fact]
    public void A_configured_port_wins()
        => Parse("mail:\n  smtp:\n    port: 465\n").Mail.Smtp.Port.Should().Be(465);
}
