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
/// <c>via/:via_name/:via_value</c> is mounted on 18 resources of the tenant API
/// (config/routes/v4.rb) and, since the payload-parity change of 2026-09-02, on four of the
/// enterprise ones (config/routes/v4_enterprise.rb): <c>inventories</c> and
/// <c>device_locations</c> with show/update/destroy, <c>buildings</c> and <c>floors</c> with
/// show only. <c>issues</c>, <c>incidents</c>, <c>departments</c> and every other enterprise
/// resource have no via route, so the one mechanism that answers everywhere in that scope is
/// the gridfilter. Verified on 2026-08-30 against app.samedis.test, before that change: the
/// same inventory answered 200 through the route under the tenant path and 404 under the
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
    /// the enterprise API apart from its four via-capable resources, and the device-model
    /// sync endpoint of the tenant API.
    /// </summary>
    /// <remarks>
    /// Weaker in one respect worth knowing: the route answers about a field the model
    /// declares as uniquely indexed, while a filter matches whatever the field happens to
    /// contain. Where a value is not in fact unique, the filter takes the first match --
    /// <see cref="Lookup.ResourceLookup.AmbiguousMatches"/> records that it did.
    /// </remarks>
    Filter,
}
