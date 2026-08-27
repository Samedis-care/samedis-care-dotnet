namespace SamedisCare.Api.Routing;

/// <summary>
/// Baut die Resource-Pfade der Samedis.care API V4 auf.
/// <para>
/// Kapselt den einzigen strukturellen Unterschied zwischen der normalen Welt und der
/// Enterprise-Welt ("Service-Welt"): das Pfad-Prefix. Payload (params, gridfilter, body)
/// und Response sind für die gemeinsam unterstützten Resourcen identisch, deshalb muss
/// ein Sync nur den Scope tauschen, nicht seine Mapping-Logik.
/// </para>
/// </summary>
public interface ITenantScope
{
    /// <summary>API-Version im Pfad, z. B. <c>v4</c>.</summary>
    string ApiVersion { get; }

    /// <summary>Tenant, auf den sich der Scope bezieht.</summary>
    string TenantId { get; }

    /// <summary>
    /// Client (Einrichtung) innerhalb eines Enterprise-Tenants, oder <c>null</c> wenn der
    /// Scope nicht client-bezogen ist.
    /// </summary>
    string? ClientId { get; }

    /// <summary>Ob der Scope auf die Enterprise-Pfadfamilie zeigt.</summary>
    bool IsEnterprise { get; }

    /// <summary>
    /// Liefert den vollständigen Pfad für eine Resource, z. B. <c>inventories</c> oder
    /// <c>inventories/{id}/uploads</c>. Führende Slashes im Argument werden toleriert.
    /// </summary>
    string Resource(string resource);
}
