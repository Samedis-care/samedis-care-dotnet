namespace SamedisCare.Api.Routing;

/// <summary>
/// Builds resource paths for the Samedis.care API V4.
/// <para>
/// Encapsulates the only structural difference between the normal world and the
/// enterprise world ("service world"): the path prefix. Payload (params, gridfilter,
/// body) and response are identical for the resources both worlds support, so a sync
/// only has to swap the scope, never its mapping logic.
/// </para>
/// </summary>
public interface ITenantScope
{
    /// <summary>API version used in the path, e.g. <c>v4</c>.</summary>
    string ApiVersion { get; }

    /// <summary>The tenant this scope refers to.</summary>
    string TenantId { get; }

    /// <summary>
    /// The client (facility) inside an enterprise tenant, or <c>null</c> when the scope
    /// is not client-scoped.
    /// </summary>
    string? ClientId { get; }

    /// <summary>Whether this scope points at the enterprise path family.</summary>
    bool IsEnterprise { get; }

    /// <summary>
    /// The scope's path prefix without a resource, e.g.
    /// <c>/api/v4/tenants/{tenantId}</c>. Use this for the tenant record itself;
    /// <see cref="Resource"/> rejects an empty resource on purpose, so that a missing
    /// resource name cannot silently produce this path.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Returns the full path for a resource, e.g. <c>inventories</c> or
    /// <c>inventories/{id}/uploads</c>. Leading slashes in the argument are tolerated.
    /// </summary>
    string Resource(string resource);
}
