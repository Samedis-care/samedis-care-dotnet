namespace SamedisCare.Api.Http;

/// <summary>
/// HTTP and proxy settings used by <see cref="SamedisCare.Api.Auth.Authenticate"/> and <see cref="RequestData"/>.
/// </summary>
public class HttpSettings
{
    public string? Proxy { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
    public bool ValidateCertificate { get; set; } = true;

    /// <summary>
    /// Hard timeout per HTTP request, in seconds. Default 30s.
    /// Prevents the UI/worker from hanging indefinitely when the auth/API endpoint is unreachable.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
