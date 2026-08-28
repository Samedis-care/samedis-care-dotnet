using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Http;

namespace SamedisCare.Api.Common;

/// <summary>
/// Minimal JSON:API envelope for reading the <c>meta.msg</c> block that Samedis returns on
/// errors. Deliberately independent of any resource — the previous implementations
/// deserialized whichever resource model happened to be at hand (e.g. <c>Staffs.Root</c>)
/// just to reach these two fields.
/// </summary>
public class ApiEnvelope
{
    [JsonProperty("meta")] public MetaBlock? Meta { get; set; }

    public class MetaBlock
    {
        [JsonProperty("msg")] public MsgBlock? Msg { get; set; }
    }

    public class MsgBlock
    {
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("message")] public string? Message { get; set; }
    }

    /// <summary>
    /// The fullest error text the server offers: <c>error</c>, <c>message</c> and
    /// <c>error_details</c> from <c>meta.msg</c>, joined with an em dash and with empty
    /// parts left out. Returns an empty string when there is nothing to report.
    /// <para>
    /// Prefer this over <see cref="ErrorMessage"/> for anything a person reads:
    /// <c>error_details</c> is where validation failures name the offending field.
    /// </para>
    /// </summary>
    public static string ErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            var msg = JObject.Parse(body)["meta"]?["msg"];
            if (msg == null) return string.Empty;

            var parts = new List<string>();
            foreach (var key in new[] { "error", "message" })
            {
                var v = msg[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) parts.Add(v);
            }

            var details = msg["error_details"];
            if (details != null && details.Type != JTokenType.Null)
            {
                var d = details.Type == JTokenType.String ? details.ToString() : details.ToString(Formatting.None);
                // The server sends these placeholders when there is nothing to say.
                if (!string.IsNullOrWhiteSpace(d) && d is not ("null" or "{}" or "[]")) parts.Add(d);
            }

            return string.Join(" — ", parts);
        }
        catch (JsonException) { return string.Empty; }
    }

    /// <summary>
    /// Extracts <c>meta.msg.message</c> from a response body, or null when the body is
    /// empty or not the expected shape. Never throws — it is used on error paths.
    /// </summary>
    public static string? ErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonConvert.DeserializeObject<ApiEnvelope>(body)?.Meta?.Msg?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Result of a capability probe. <see cref="Allowed"/> is the only thing most callers
/// need; the status code and message are there for logging and for deciding whether to
/// abort or to carry on with a reduced feature set.
/// </summary>
public readonly record struct CapabilityResult(bool Allowed, int StatusCode, string? Message)
{
    public override string ToString()
        => Allowed ? "allowed" : $"denied ({StatusCode}{(Message is null ? "" : $": {Message}")})";
}

/// <summary>
/// Checks whether the authenticated user may read a resource, by requesting it with
/// <c>?limit=0</c> and inspecting the status code.
/// <para>
/// This returns a result instead of terminating. The implementations this replaces called
/// <c>Environment.Exit(1)</c> from inside a helper, which a library must never do — it
/// takes the decision away from the host and makes the behaviour untestable. Callers that
/// want the old abort-on-denied behaviour should check <see cref="CapabilityResult.Allowed"/>
/// and stop themselves.
/// </para>
/// </summary>
public static class Capability
{
    public static CapabilityResult Probe(RequestData client, string resource)
    {
        var body = client.Get(resource + "?limit=0");
        if (client.StatusCode < 400)
            return new CapabilityResult(true, client.StatusCode, null);

        return new CapabilityResult(false, client.StatusCode,
                                    ApiEnvelope.ErrorMessage(body) ?? "Unknown error");
    }
}
