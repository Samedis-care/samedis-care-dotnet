using System.IO;
using System.Net;
using RestSharp;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.Http;

/// <summary>
/// Authenticated GET/POST/PUT against Samedis API.
/// File-upload helpers post to 'data[document]' on /issues/{id}/uploads.
///
/// Adapted from Samedis-care/samedis-care-external-sync `Samedis.cs`.
/// </summary>
public class RequestData
{
    public int StatusCode;
    public HttpStatusCode Status;
    public string LastError = string.Empty;
    public string LastResponseStatus = string.Empty;

    /// <summary>
    /// Body of the last response. Kept because callers need to read <c>meta.msg</c> out of
    /// a failed call after the fact, without having held on to the return value.
    /// </summary>
    public string LastContent = string.Empty;

    /// <summary>
    /// Dry-run marker for the consuming tool. This class suppresses no request on its
    /// own — the only behaviour it changes here is the diagnostic GET dump, which is
    /// written when TestMode is set AND the log level is debug (<see cref="ISyncLog.Level"/> &gt;= 2).
    /// </summary>
    public bool TestMode { get; set; }

    /// <summary>
    /// Target file for the diagnostic GET dump. A relative path resolves against the
    /// working directory. Only used when <see cref="TestMode"/> and debug logging are on.
    /// </summary>
    public string DebugCsvPath { get; set; } = "debug_get_requests.csv";

    private readonly string _baseUrl;
    private readonly string _token;
    private readonly RestClientOptions _options;
    private readonly HttpSettings _httpSettings;
    private readonly ISyncLog _log;

    public RequestData(string baseUrl, string token, HttpSettings httpSettings, ISyncLog log, bool testMode = false)
    {
        _baseUrl = baseUrl;
        _token = token;
        _httpSettings = httpSettings;
        _log = log;
        TestMode = testMode;

        _options = new RestClientOptions(_baseUrl)
        {
            Timeout = TimeSpan.FromSeconds(_httpSettings.TimeoutSeconds)
        };
        if (!_httpSettings.ValidateCertificate)
            _options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        if (!string.IsNullOrEmpty(_httpSettings.Proxy))
        {
            var proxy = new WebProxy(_httpSettings.Proxy);
            if (!string.IsNullOrEmpty(_httpSettings.ProxyUsername))
                proxy.Credentials = new NetworkCredential(_httpSettings.ProxyUsername, _httpSettings.ProxyPassword);
            _options.Proxy = proxy;
        }
    }

    public string Get(string resource)
    {
        using var client = new RestClient(_options);
        var request = new RestRequest(resource, Method.Get)
            .AddHeader("accept", "application/json")
            .AddHeader("Content-Type", "application/json")
            .AddHeader("Authorization", $"Bearer {_token}");

        var response = client.ExecuteGet(request);
        response = HandleRetry(response, request, client.ExecuteGet);
        Capture(response);
        if (TestMode && _log.Level >= 2)
            WriteDebugGetCsv(resource, response);
        return response.Content ?? string.Empty;
    }

    public string Post(string resource, string content)
    {
        using var client = new RestClient(_options);
        var request = new RestRequest(resource, Method.Post)
            .AddHeader("accept", "application/json")
            .AddHeader("Content-Type", "application/json")
            .AddHeader("Authorization", $"Bearer {_token}")
            .AddStringBody(content, DataFormat.Json);

        var response = client.ExecutePost(request);
        response = HandleRetry(response, request, client.ExecutePost);
        Capture(response);
        return response.Content ?? string.Empty;
    }

    public string Put(string resource, string id, string content)
    {
        var putResource = resource;
        var queryIndex = putResource.IndexOf('?');
        if (queryIndex >= 0)
        {
            var path = putResource[..queryIndex];
            var query = putResource[queryIndex..];
            putResource = path + "/" + id + query;
        }
        else
        {
            putResource = putResource + "/" + id;
        }

        using var client = new RestClient(_options);
        var request = new RestRequest(putResource, Method.Put)
            .AddHeader("accept", "application/json")
            .AddHeader("Content-Type", "application/json")
            .AddHeader("Authorization", $"Bearer {_token}")
            .AddStringBody(content, DataFormat.Json);

        var response = client.ExecutePut(request);
        response = HandleRetry(response, request, client.ExecutePut);
        Capture(response);
        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Uploads a file to an uploads endpoint, e.g. <c>/issues/{id}/uploads</c> or
    /// <c>/trainings/{id}/uploads</c>.
    /// <para>
    /// The field name is <c>data[document]</c>, alongside <c>data[name]</c> for the display
    /// name, per the Samedis API docs. That is the only field name the endpoint accepts —
    /// identical for PDF and PNG, so there is no separate image call. The server validates
    /// multipart strictly; <c>data[file]</c> or <c>data[image]</c> yield
    /// "File cannot be blank". The MIME type travels in the file part's Content-Type.
    /// </para>
    /// <para>
    /// IMPORTANT: do NOT set an explicit <c>Content-Type</c> header — RestSharp sets
    /// <c>multipart/form-data; boundary=...</c> correctly on its own, and setting it
    /// manually without a boundary results in a 400 "File cannot be blank".
    /// </para>
    /// </summary>
    public string PostDocument(string resource, string filePath, string fileName)
    {
        using var client = new RestClient(_options);
        var request = new RestRequest(resource, Method.Post)
            .AddHeader("accept", "application/json")
            .AddHeader("Authorization", $"Bearer {_token}")
            .AddParameter("data[name]", fileName)
            .AddFile("data[document]", filePath, GuessContentType(filePath));
        var response = client.ExecutePost(request);
        response = HandleRetry(response, request, client.ExecutePost);
        Capture(response);
        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Returns a MIME type for a file based on its extension. RestSharp.AddFile accepts
    /// an optional ContentType — some servers (Rails in particular) are stricter when
    /// parsing multipart if the file part carries no Content-Type.
    /// </summary>
    private static string GuessContentType(string filePath)
    {
        var ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => "application/pdf",
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            _       => "application/octet-stream"
        };
    }

    private void Capture(RestResponse response)
    {
        Status = response.StatusCode;
        StatusCode = (int)Status;
        LastResponseStatus = response.ResponseStatus.ToString();
        LastError = response.ErrorMessage ?? response.ErrorException?.Message ?? string.Empty;
        LastContent = response.Content ?? string.Empty;
    }

    /// <summary>Honour Retry-After on 429 once, then bubble up the response.</summary>
    private static RestResponse HandleRetry(RestResponse response, RestRequest request, Func<RestRequest, RestResponse> execute)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        var retryAfterHeader = response.Headers?.FirstOrDefault(h =>
            h.Name?.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) == true);
        if (retryAfterHeader?.Value != null && int.TryParse(retryAfterHeader.Value.ToString(), out var seconds))
        {
            Thread.Sleep(seconds * 1000);
            return execute(request);
        }
        return response;
    }

    private static readonly object DebugCsvLock = new();

    private void WriteDebugGetCsv(string resource, RestResponse response)
        => AppendDebugCsvRow(DebugCsvPath, "GET", resource, (int)response.StatusCode, response.Content);

    /// <summary>
    /// Appends one semicolon-separated row to the diagnostic dump, creating the file and
    /// its header on first write. The response body is truncated to
    /// <see cref="DebugBodyPreviewLength"/> characters and newlines are flattened, so one
    /// request always stays one CSV row.
    /// </summary>
    internal static void AppendDebugCsvRow(string path, string method, string resource, int statusCode, string? body)
    {
        var preview = body ?? string.Empty;
        if (preview.Length > DebugBodyPreviewLength)
            preview = preview.Substring(0, DebugBodyPreviewLength);

        var row = new[]
        {
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            method,
            resource ?? string.Empty,
            statusCode.ToString(),
            preview.Replace("\r", " ").Replace("\n", " "),
        };

        lock (DebugCsvLock)
        {
            var needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var sw = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
            if (needsHeader)
                sw.WriteLine(string.Join(";", DebugCsvHeaders.Select(EscapeCsv)));
            sw.WriteLine(string.Join(";", row.Select(EscapeCsv)));
        }
    }

    internal const int DebugBodyPreviewLength = 2000;

    internal static readonly string[] DebugCsvHeaders =
        { "Timestamp", "Method", "Resource", "StatusCode", "ResponseBody" };

    /// <summary>
    /// Quotes a CSV field when it contains a quote, the semicolon separator, or a newline,
    /// doubling embedded quotes as the format requires.
    /// </summary>
    internal static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var needsQuotes = value.Contains('"') || value.Contains(';') || value.Contains('\r') || value.Contains('\n');
        var sanitized = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{sanitized}\"" : sanitized;
    }
}
