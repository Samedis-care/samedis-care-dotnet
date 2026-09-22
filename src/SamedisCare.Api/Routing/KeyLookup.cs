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
/// (config/routes/v4.rb) and, since the payload-parity change of 2026-09-02, on four
/// <b>client-scoped</b> enterprise ones (config/routes/v4_enterprise.rb): <c>inventories</c>
/// and <c>device_locations</c> with show/update/destroy, <c>buildings</c> and <c>floors</c>
/// with show only. <c>issues</c>, <c>incidents</c>, <c>departments</c> and every other
/// resource under <c>clients/</c> have none.
/// </para>
/// <para>
/// The word client-scoped is load-bearing: all four sit inside
/// <c>resources :clients … scope module: :clients</c>. The aggregate enterprise paths below
/// it -- <c>enterprise/tenants/{tenantId}/inventories|issues|…</c>, what
/// <see cref="TenantScope.EnterpriseTenant"/> addresses -- mount no via route at all, so
/// <c>inventories</c> answers the route under one enterprise scope and the router's 404
/// under the other. The gridfilter is the one mechanism that answers under both, which is
/// why it is what each enterprise scope hands out.
/// </para>
/// <para>
/// Verified on 2026-08-30 against app.samedis.test, before that change: the same inventory
/// answered 200 through the route under the tenant path and 404 under the enterprise path,
/// while a gridfilter on <c>external_id</c> found it under both.
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
    /// Through a gridfilter on the field. What both enterprise scopes hand out, and what the
    /// device-model sync endpoint of the tenant API needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chosen per scope, not per resource: an enterprise scope answers <c>Filter</c> for
    /// <c>inventories</c> too, although the client-scoped path does mount a via route on it.
    /// One mechanism per scope is the point -- a caller picks a scope, not a lookup strategy
    /// per resource -- so it has to be the one that answers on every resource that scope
    /// reaches, and only the gridfilter does.
    /// </para>
    /// <para>
    /// Weaker in one respect worth knowing: the route answers about a field the model
    /// declares as uniquely indexed, while a filter matches whatever the field happens to
    /// contain. Where a value is not in fact unique, the filter takes the first match --
    /// <see cref="Lookup.ResourceLookup.AmbiguousMatches"/> records that it did.
    /// </para>
    /// </remarks>
    Filter,
}
