namespace SamedisCare.Api.Routing;

/// <summary>
/// Erzeugt <see cref="ITenantScope"/>-Instanzen für die drei Pfadfamilien der API V4.
/// </summary>
public static class TenantScope
{
    /// <summary>Standard-API-Version, wenn keine explizit angegeben wird.</summary>
    public const string DefaultApiVersion = "v4";

    /// <summary>
    /// Normale Welt: <c>/api/{version}/tenants/{tenantId}/{resource}</c>
    /// </summary>
    public static ITenantScope Standard(string tenantId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, clientId: null, isEnterprise: false);

    /// <summary>
    /// Enterprise, client-bezogen (Spiegel der normalen Welt, das Sync-Ziel):
    /// <c>/api/{version}/enterprise/tenants/{tenantId}/clients/{clientId}/{resource}</c>
    /// </summary>
    public static ITenantScope Enterprise(string tenantId, string clientId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, Guard(clientId, nameof(clientId)), isEnterprise: true);

    /// <summary>
    /// Enterprise, einrichtungsübergreifendes Aggregat (überwiegend read-only):
    /// <c>/api/{version}/enterprise/tenants/{tenantId}/{resource}</c>
    /// </summary>
    public static ITenantScope EnterpriseTenant(string tenantId, string apiVersion = DefaultApiVersion)
        => new Scope(apiVersion, tenantId, clientId: null, isEnterprise: true);

    private static string Guard(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("darf nicht leer sein", paramName)
            : value.Trim();

    private sealed class Scope : ITenantScope
    {
        private readonly string _prefix;

        internal Scope(string apiVersion, string tenantId, string? clientId, bool isEnterprise)
        {
            ApiVersion = Guard(apiVersion, nameof(apiVersion)).TrimStart('/');
            TenantId = Guard(tenantId, nameof(tenantId));
            ClientId = clientId;
            IsEnterprise = isEnterprise;

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

        public string Resource(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("darf nicht leer sein", nameof(resource));

            return $"{_prefix}/{resource.Trim().TrimStart('/')}";
        }

        public override string ToString() => _prefix;
    }
}
