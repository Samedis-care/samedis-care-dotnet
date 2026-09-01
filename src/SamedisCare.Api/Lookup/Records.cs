using Newtonsoft.Json;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.Lookup;

/// <summary>
/// Creating a record, and the find-or-create that every sync performs for master data it
/// references but does not own — departments, device types, manufacturers, buildings.
/// <para>
/// Each tool had its own copy of the same twenty lines: wrap the attributes in a
/// <c>data</c> envelope, POST, check the status, read the id back, note it in a cache and log
/// the outcome. What differs between them is only the cascade used to look first and the
/// attributes to send, so those are the parameters and the rest lives here.
/// </para>
/// </summary>
public static class Records
{
    /// <summary>
    /// Creates a record and returns its id, or null with the reason logged.
    /// </summary>
    /// <param name="client">The API client.</param>
    /// <param name="resource">The collection path to POST to.</param>
    /// <param name="attributes">
    /// The record's fields. Wrapped in the <c>data</c> envelope here — Samedis reads
    /// <c>params[:data][:field]</c> directly, without an <c>attributes</c> subkey.
    /// </param>
    /// <param name="log">Where the outcome is reported.</param>
    /// <param name="what">
    /// How to name the record in the log, e.g. <c>department 'Radiologie'</c>. Written into
    /// both the success and the failure line, so it should identify the record to a person
    /// reading the log without the surrounding context.
    /// </param>
    /// <remarks>
    /// A 2xx with no id in the body is treated as a failure, not as success. The server
    /// answering "fine" without saying what it created leaves the caller with nothing to
    /// reference, and carrying on would attach later records to an empty id.
    /// </remarks>
    public static string? Create(IApiClient client, string resource,
                                 IReadOnlyDictionary<string, object?> attributes,
                                 ISyncLog log, string what)
    {
        var payload = JsonConvert.SerializeObject(new { data = attributes });
        var response = client.Post(resource, payload);

        if (!JsonApi.IsSuccess(client.StatusCode))
        {
            // A create rejected because the record already exists is not a failure: the server
            // names the record it collided with, and that is the one the caller was after.
            var duplicate = ApiEnvelope.DuplicateOf(response, out var kind);
            if (!string.IsNullOrWhiteSpace(duplicate))
            {
                log.Info($"{what} already existed; reusing the record the server named ({kind}) -> {duplicate}");
                return duplicate;
            }

            log.Error($"Failed to create {what}: status {client.StatusCode}. {Explain(response)}");
            return null;
        }

        var id = JsonApi.ExtractDataId(response);
        if (string.IsNullOrWhiteSpace(id))
        {
            log.Error($"Failed to create {what}: the request was accepted but the response carried no id.");
            return null;
        }

        log.Debug($"Created {what} -> {id}");
        return id;
    }

    /// <summary>
    /// Returns the id the cascade resolves, and creates the record when it resolves nothing.
    /// </summary>
    /// <param name="client">The API client.</param>
    /// <param name="resource">The collection path.</param>
    /// <param name="find">
    /// The lookup cascade. Called once; whatever it returns is final, so it must not fall
    /// through after a match — see <see cref="ResourceLookup"/> for why.
    /// </param>
    /// <param name="attributes">The fields to send if the record has to be created.</param>
    /// <param name="log">Where the outcome is reported.</param>
    /// <param name="what">How to name the record in the log.</param>
    /// <param name="create">
    /// Whether a missing record may be created. False makes this a pure lookup, which is what
    /// a caller wants when the master data is maintained elsewhere and an unknown value should
    /// be reported rather than invented.
    /// </param>
    /// <param name="remember">
    /// Called with the new id after a create, to seed the lookup so a later row naming the
    /// same record is answered from memory instead of asking for what this run just wrote.
    /// </param>
    public static string? FindOrCreate(IApiClient client, string resource,
                                       Func<string?> find,
                                       IReadOnlyDictionary<string, object?> attributes,
                                       ISyncLog log, string what,
                                       bool create = true,
                                       Action<string>? remember = null)
    {
        var found = find();
        if (!string.IsNullOrWhiteSpace(found)) return found;

        return create ? CreateAndRemember(client, resource, attributes, log, what, remember) : null;
    }

    /// <summary>
    /// Returns the id the cascade resolves, updating that record with the given attributes;
    /// creates it when the cascade resolves nothing.
    /// </summary>
    /// <param name="client">The API client.</param>
    /// <param name="resource">The collection path.</param>
    /// <param name="find">The lookup cascade. Called once.</param>
    /// <param name="attributes">The fields to write, on create and on update alike.</param>
    /// <param name="log">Where the outcome is reported.</param>
    /// <param name="what">How to name the record in the log.</param>
    /// <param name="create">Whether a missing record may be created.</param>
    /// <param name="remember">Called with the new id after a create.</param>
    /// <remarks>
    /// A failed update returns the existing id rather than null: the record is there and the
    /// caller can still reference it, which is a different situation from "does not exist".
    /// The failure is logged either way.
    /// </remarks>
    public static string? Upsert(IApiClient client, string resource,
                                 Func<string?> find,
                                 IReadOnlyDictionary<string, object?> attributes,
                                 ISyncLog log, string what,
                                 bool create = true,
                                 Action<string>? remember = null)
    {
        var found = find();
        if (string.IsNullOrWhiteSpace(found))
            return create ? CreateAndRemember(client, resource, attributes, log, what, remember) : null;

        var payload = JsonConvert.SerializeObject(new { data = attributes });
        var response = client.Put(resource, found, payload);

        if (!JsonApi.IsSuccess(client.StatusCode))
            log.Warn($"Failed to update {what} (id='{found}'): status {client.StatusCode}. {Explain(response)}");

        return found;
    }

    private static string? CreateAndRemember(IApiClient client, string resource,
                                             IReadOnlyDictionary<string, object?> attributes,
                                             ISyncLog log, string what, Action<string>? remember)
    {
        var id = Create(client, resource, attributes, log, what);
        if (!string.IsNullOrWhiteSpace(id)) remember?.Invoke(id);
        return id;
    }

    /// <summary>
    /// The server's own explanation of a failure, falling back to the raw body when the
    /// response is not the expected envelope — losing the body entirely would leave a failed
    /// create with nothing to diagnose.
    /// </summary>
    private static string Explain(string? response)
    {
        var detail = ApiEnvelope.ErrorDetail(response);
        if (!string.IsNullOrWhiteSpace(detail)) return detail;

        var body = (response ?? string.Empty).Trim();
        if (body.Length == 0) return "The response was empty.";

        return body.Length <= 500 ? body : body[..500] + "…";
    }
}
