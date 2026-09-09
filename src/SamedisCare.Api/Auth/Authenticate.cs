using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SamedisCare.Api.Http;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.Auth;

/// <summary>
/// OAuth password-grant against the Samedis identity service.
///
/// Endpoint: POST {baseUrl}/api/v1/samedis.care/oauth/token
///   form:  grant_type=password, email=&lt;clientId&gt;, password=&lt;clientSecret&gt;
///   response: meta.token = bearer, meta.refresh_token = refresh
///
/// Adapted from Samedis-care/samedis-care-external-sync `Samedis.cs`.
/// </summary>
public class Authenticate
{
    public int StatusCode;
    public HttpStatusCode Status;
    public string BearerToken = "";
    public string RefreshToken = "";
    public string User = "";

    /// <summary>
    /// Reports what the base URL's host resolves to, before anything is sent.
    /// </summary>
    /// <remarks>
    /// When authentication fails at a customer site the first question is whether the name
    /// resolves at all — a proxy, a split-horizon DNS or a firewall shows up here and nowhere
    /// else in the log. Failure to resolve is reported and not thrown: the request that
    /// follows will fail on its own and with a better message.
    /// </remarks>
    private static void LogNameResolution(string baseUrl, ISyncLog log)
    {
        if (log.Level < 2) return;

        try
        {
            var host = new Uri(baseUrl).Host;
            var addresses = Dns.GetHostAddresses(host);
            log.Debug($"Auth DNS {host} -> {string.Join(", ", addresses.Select(a => a.ToString()))}");
        }
        catch (Exception ex) when (ex is SocketException or UriFormatException or ArgumentException)
        {
            log.Debug($"Auth DNS resolution failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public Authenticate(string baseUrl, string clientId, string clientSecret, HttpSettings httpSettings, ISyncLog log)
    {
        try
        {
            const string resource = "api/v1/samedis.care/oauth/token";
            var fullUrl = SafeUri(baseUrl, resource);

            log.Debug($"Auth BaseUrl={baseUrl}");
            log.Debug($"Auth FullUrl={fullUrl}");
            log.Debug($"Auth ValidateCertificate={httpSettings.ValidateCertificate}");
            log.Debug($"Auth Proxy={httpSettings.Proxy}");
            log.Debug($"Auth ClientId(email)={clientId}");
            log.Debug($"Auth ClientSecret length={(clientSecret?.Length ?? 0)} (redacted)");

            LogNameResolution(baseUrl, log);

            var options = new RestClientOptions(baseUrl)
            {
                Timeout = TimeSpan.FromSeconds(httpSettings.TimeoutSeconds)
            };
            if (!httpSettings.ValidateCertificate)
            {
                options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                log.Debug("WARNING: Certificate validation DISABLED for auth request.");
            }

            if (!string.IsNullOrEmpty(httpSettings.Proxy))
            {
                var proxy = new WebProxy(httpSettings.Proxy);
                if (!string.IsNullOrEmpty(httpSettings.ProxyUsername))
                    proxy.Credentials = new NetworkCredential(httpSettings.ProxyUsername, httpSettings.ProxyPassword);
                options.Proxy = proxy;
            }

            options.ConfigureMessageHandler = handler =>
            {
                if (handler is HttpClientHandler http)
                {
                    if (!string.IsNullOrEmpty(httpSettings.Proxy))
                    {
                        if (!Uri.TryCreate(httpSettings.Proxy.Trim(), UriKind.Absolute, out var proxyUri))
                        {
                            var withScheme = "http://" + httpSettings.Proxy.Trim();
                            if (!Uri.TryCreate(withScheme, UriKind.Absolute, out proxyUri))
                                throw new UriFormatException($"Invalid proxy URI: '{httpSettings.Proxy}'.");
                        }
                        var p = new WebProxy(proxyUri) { BypassProxyOnLocal = false, UseDefaultCredentials = false };
                        if (!string.IsNullOrEmpty(httpSettings.ProxyUsername))
                            p.Credentials = new NetworkCredential(httpSettings.ProxyUsername, httpSettings.ProxyPassword);
                        http.Proxy = p;
                        http.UseProxy = true;
                    }

                    if (!httpSettings.ValidateCertificate)
                        http.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                return handler;
            };

            using var client = new RestClient(options);
            var request = new RestRequest(resource, Method.Post)
                .AddHeader("accept", "application/json")
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("grant_type", "password")
                .AddParameter("email", clientId)
                .AddParameter("password", clientSecret);

            log.Info("Sending auth request...");
            var sw = Stopwatch.StartNew();
            var response = client.ExecutePost(request);
            sw.Stop();
            log.Debug($"Auth completed in {sw.ElapsedMilliseconds} ms, status={(int)response.StatusCode}");

            Status = response.StatusCode;
            StatusCode = (int)Status;

            if (!string.IsNullOrEmpty(response.Content))
            {
                var root = JsonConvert.DeserializeObject<JObject>(response.Content);
                if (root != null)
                {
                    var meta = root["meta"];
                    var data = root["data"];
                    BearerToken = meta?["token"]?.ToString() ?? string.Empty;
                    RefreshToken = meta?["refresh_token"]?.ToString() ?? string.Empty;
                    User = data?["attributes"]?["email"]?.ToString() ?? string.Empty;
                }
            }

            log.Debug($"Parsed BearerToken={(string.IsNullOrEmpty(BearerToken) ? "<empty>" : Redact.Token(BearerToken))}");
            log.Debug($"Parsed User={User}");
        }
        catch (Exception ex)
        {
            Status = 0;
            StatusCode = 0;
            log.Error("Authenticate threw an exception", ex);
            throw;
        }
    }

    private static string SafeUri(string baseUrl, string resource)
        => baseUrl.EndsWith('/') ? baseUrl + resource.TrimStart('/') : baseUrl + "/" + resource.TrimStart('/');
}
