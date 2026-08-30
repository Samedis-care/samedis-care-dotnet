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
    /// Whether the body carries the server's own <c>meta.msg</c> envelope, i.e. whether the
    /// application answered at all.
    /// </summary>
    /// <remarks>
    /// This is how a real answer is told from the router's. Rails answers an unmounted route
    /// with a bare <c>{"status":404,"error":"Not Found"}</c>, while the application's own
    /// "no such record" carries the full envelope with
    /// <c>meta.msg.error = record_not_found_error</c>. Both are 404, and without this
    /// distinction a lookup against an endpoint that does not exist reads as "the record is
    /// not there" -- verified against the enterprise API, where <c>via/external_id</c> is
    /// mounted on no resource at all and every such lookup would silently resolve to null.
    /// </remarks>
    public static bool HasEnvelope(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try { return JObject.Parse(body)["meta"]?["msg"] != null; }
        catch (JsonException) { return false; }
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
    /// The id of the record a rejected create collided with, when the server named one.
    /// </summary>
    /// <param name="body">The response body of the rejected request.</param>
    /// <param name="kind">
    /// Which of the two the id came from: <c>duplicate_of</c> for a record the facility
    /// already owns, <c>public_duplicate_of</c> for one in the shared catalog.
    /// </param>
    /// <remarks>
    /// A create that fails because the record already exists is not really a failure: the
    /// server puts the existing record's id in <c>meta.msg.error_details</c>, and using it is
    /// what the caller wanted in the first place. The facility's own record takes precedence
    /// over the public one, because that is the one the facility can edit.
    /// </remarks>
    public static string? DuplicateOf(string? body, out string kind)
    {
        kind = string.Empty;
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            var details = JObject.Parse(body)["meta"]?["msg"]?["error_details"];
            if (details is not JObject obj) return null;

            foreach (var key in new[] { "duplicate_of", "public_duplicate_of" })
            {
                var value = obj[key]?.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                kind = key;
                return value;
            }
        }
        catch (JsonException) { /* not the expected shape */ }

        return null;
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
