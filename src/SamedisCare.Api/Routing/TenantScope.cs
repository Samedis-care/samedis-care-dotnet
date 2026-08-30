namespace SamedisCare.Api.Routing;

/// <summary>
/// Creates <see cref="ITenantScope"/> instances for the three path families of API V4.
/// </summary>
public static class TenantScope
{
    /// <summary>Default API version used when none is given explicitly.</summary>
    public const string DefaultApiVersion = "v4";

    /// <summary>
    /// Normal world: <c>/api/{version}/tenants/{tenantId}/{resource}</c>
    /// </summary>
    public static ITenantScope Standard(string tenantId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, clientId: null, isEnterprise: false, KeyLookup.Route);

    /// <summary>
    /// Enterprise, client-scoped (the mirror of the normal world, and the sync target):
    /// <c>/api/{version}/enterprise/tenants/{tenantId}/clients/{clientId}/{resource}</c>
    /// </summary>
    public static ITenantScope Enterprise(string tenantId, string clientId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, Guard(clientId, nameof(clientId)), isEnterprise: true, KeyLookup.Filter);

    /// <summary>
    /// Enterprise, cross-facility aggregate (mostly read-only):
    /// <c>/api/{version}/enterprise/tenants/{tenantId}/{resource}</c>
    /// </summary>
    public static ITenantScope EnterpriseTenant(string tenantId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, clientId: null, isEnterprise: true, KeyLookup.Filter);

    private static string Guard(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("must not be empty", paramName)
            : value.Trim();

    private sealed class Scope : ITenantScope
    {
        private readonly string _prefix;

        internal Scope(string apiVersion, string tenantId, string? clientId, bool isEnterprise,
                       KeyLookup keyLookup)
        {
            ApiVersion = Guard(apiVersion, nameof(apiVersion)).TrimStart('/');
            TenantId = Guard(tenantId, nameof(tenantId));
            ClientId = clientId;
            IsEnterprise = isEnterprise;
            KeyLookup = keyLookup;

            _prefix = (isEnterprise, clientId) switch
            {
                (true, not null) => $"/api/{ApiVersion}/enterprise/tenants/{TenantId}/clients/{clientId}",
                (true, null) => $"/api/{ApiVersion}/enterprise/tenants/{TenantId}",
                _ => $"/api/{ApiVersion}/tenants/{TenantId}",
            };
        }

        public string ApiVersion { get; }
        public string TenantId { get; }
        public string? ClientId { get; }
        public bool IsEnterprise { get; }
        public KeyLookup KeyLookup { get; }
        public string Root => _prefix;

        public string Resource(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("must not be empty", nameof(resource));

            return $"{_prefix}/{resource.Trim().TrimStart('/')}";
        }

        public override string ToString() => _prefix;
    }
}
