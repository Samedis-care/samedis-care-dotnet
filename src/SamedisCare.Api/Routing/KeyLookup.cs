namespace SamedisCare.Api.Routing;

/// <summary>
/// How a backend lets a record be found by a field that is not its id -- an
/// <c>external_id</c>, an employee number, an incident number.
/// </summary>
/// <remarks>
/// <para>
/// Both mechanisms answer the same question and return the same record. They differ only in
/// which one a given API offers, which is why this is a property of the scope and not a
/// decision each call site makes.
/// </para>
/// <para>
/// Verified on 2026-08-30 against app.samedis.test: <c>via/:via_name/:via_value</c> is
/// mounted on 18 resources of the tenant API (config/routes/v4.rb) and on none of the
/// enterprise ones (config/routes/v4_enterprise.rb carries only <c>concerns: :changelogs</c>).
/// The same inventory answered 200 through the route under the tenant path and 404 under the
/// enterprise path, while a gridfilter on <c>external_id</c> found it under both.
/// </para>
/// </remarks>
public enum KeyLookup
{
    /// <summary>
    /// Through the server's find-by-field route, <c>{resource}/via/{field}/{value}</c>.
    /// One request, no filter to build, and the server decides what counts as unique.
    /// </summary>
    Route,

    /// <summary>
    /// Through a gridfilter on the field. The fallback wherever the route is not mounted --
    /// the whole enterprise API, and the device-model sync endpoint of the tenant API.
    /// </summary>
    /// <remarks>
    /// Weaker in one respect worth knowing: the route answers about a field the model
    /// declares as uniquely indexed, while a filter matches whatever the field happens to
    /// contain. Where a value is not in fact unique, the filter takes the first match --
    /// <see cref="Lookup.ResourceLookup.AmbiguousMatches"/> records that it did.
    /// </remarks>
    Filter,
}
