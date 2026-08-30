using SamedisCare.Api.Common;

namespace SamedisCare.Api.Lookup;

/// <summary>
/// A lookup could not be answered, so its result must not be read as "no such record".
/// <para>
/// The distinction matters because the two outcomes lead to opposite actions: a record that
/// genuinely does not exist is created, whereas a lookup that failed says nothing about
/// existence — creating in that case duplicates a record that is already there.
/// </para>
/// <para>
/// The server makes this easy to get wrong. A via lookup on a field the model does not
/// support answers <b>HTTP 500</b>, not a 4xx: verified against production, where
/// <c>trainings/via/external_id/...</c> and <c>departments/via/external_id/...</c> both
/// return 500 while the same route on <c>inventories</c> returns a clean 404
/// <c>record_not_found_error</c>. Treating every non-2xx as "absent" therefore turns a
/// misconfigured lookup into a run that silently duplicates everything it touches.
/// </para>
/// </summary>
public sealed class LookupUnavailableException : Exception
{
    public LookupUnavailableException(string resource, int statusCode, string? detail)
        : base($"Lookup on '{resource}' failed with status {statusCode}, so the result cannot be "
             + "read as 'record does not exist'."
             + (string.IsNullOrWhiteSpace(detail) ? "" : $" Server said: {detail}")
             + (statusCode is >= 500 and < 600
                 ? " A 500 here usually means the field is not supported for this resource."
                 : ""))
    {
        Resource = resource;
        StatusCode = statusCode;
        Detail = detail;
    }

    public string Resource { get; }
    public int StatusCode { get; }
    public string? Detail { get; }

    /// <summary>
    /// Passes when the answer is either a record or a genuine "no such record", throws on
    /// anything else. The guard for a lookup that asks whether something exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only 404 is absence.</b> A 401, a 403, a 400 on a malformed filter, a 5xx -- none
    /// of them say anything about existence, and reading them as "not there" is what makes a
    /// sync duplicate a record it already has.
    /// </para>
    /// <para>
    /// <b>And not every 404 is the application's.</b> Rails answers a route it does not have
    /// with a bare <c>{"status":404,"error":"Not Found"}</c>, which carries no
    /// <c>meta.msg</c>. That is the router talking, not the application, and it says nothing
    /// about any record. The enterprise API is exactly this case: <c>via/:via_name/:via_value</c>
    /// is mounted on 18 resources of the tenant API and on none of the enterprise ones, so
    /// every <see cref="ResourceLookup.ByVia"/> there answers 404. Counted as absence, each
    /// cascade would quietly drop to its weakest key -- for inventories the device number,
    /// which the source may have reassigned to a different device.
    /// </para>
    /// </remarks>
    public static void ThrowUnlessAbsent(string resource, int statusCode, string? body)
    {
        if (statusCode == 404 && ApiEnvelope.HasEnvelope(body)) return;

        throw new LookupUnavailableException(resource, statusCode,
            statusCode == 404 && !ApiEnvelope.HasEnvelope(body)
                ? "The endpoint answered without the server's meta.msg envelope, so this is a "
                + "missing route rather than a missing record. Check that the resource offers "
                + "this lookup -- via/:via_name is not mounted on the enterprise API."
                : ApiEnvelope.ErrorDetail(body));
    }

    /// <summary>
    /// Passes only on a successful answer. The guard for a read where absence is not one of
    /// the possible answers.
    /// </summary>
    /// <remarks>
    /// A collection answers "nothing there" as an empty list with 200, never as a 404, so a
    /// 404 on one means the parent record is gone -- not that it has no children. The
    /// distinction decides a write: sync-trainings asks which devices a training already
    /// carries, and an unanswered read counted as "none" attaches every one of them a second
    /// time.
    /// </remarks>
    public static void ThrowUnlessAnswered(string resource, int statusCode, string? body)
    {
        if (JsonApi.IsSuccess(statusCode)) return;
        throw new LookupUnavailableException(resource, statusCode, ApiEnvelope.ErrorDetail(body));
    }
}
