namespace SamedisCare.Mail;

/// <summary>
/// Everything the tools' <c>config.yml</c> says about mail. The shape is the one both
/// log-monitor and requests-to-mail already had, property for property, so an existing
/// configuration file keeps working unchanged.
/// </summary>
public sealed class MailSettings
{
    /// <summary>When false, nothing is sent and <c>SendAsync</c> reports it.</summary>
    public bool Enabled { get; set; }

    /// <summary><c>smtp</c>, <c>graph</c> or <c>gmail</c>. Compared case-insensitively.</summary>
    public string? Provider { get; set; } = "smtp";

    public string? From { get; set; }

    public List<string>? Recipients { get; set; }

    /// <summary>Subject template, where the tool uses one. Not read here.</summary>
    public string? Subject { get; set; }

    public SmtpSettings Smtp { get; set; } = new();
    public GraphSettings Graph { get; set; } = new();
    public GmailSettings Gmail { get; set; } = new();
}

/// <summary>A plain SMTP server.</summary>
public sealed class SmtpSettings
{
    public string? Server { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Implicit TLS from the first byte, the usual choice on port 465.</summary>
    public bool UseSsl { get; set; }

    /// <summary>Upgrade an open connection, the usual choice on port 587.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>
    /// Accept any server certificate. For an internal relay with a self-signed certificate;
    /// it disables the check that the server is who it claims to be.
    /// </summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>Kept so an existing config.yml still parses. Not read.</summary>
    public bool UseStartTlsLegacy { get; set; }
}

/// <summary>Microsoft Graph, sending as a named mailbox with an app registration.</summary>
public sealed class GraphSettings
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>The mailbox to send as, e.g. <c>noreply@example.org</c>.</summary>
    public string? SenderUserPrincipalName { get; set; }
}

/// <summary>The Gmail API, sending as an impersonated user via a service account.</summary>
public sealed class GmailSettings
{
    public string? ServiceAccountJsonPath { get; set; }
    public string? ImpersonatedUser { get; set; }
}
