using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Query;
using SamedisCare.Api.Routing;

namespace SamedisCare.Api.V4.Public;

/// <summary>
/// Lookups against <c>/device_models</c>. Only what the syncs need to resolve a catalog id
/// from source data — the full model is not mapped here yet.
/// </summary>
public static class DeviceModels
{
    /// <summary>
    /// Both scopes: the tenant's own device models and the public catalog. A source system
    /// may reference either, so a lookup restricted to one of them would miss matches.
    /// </summary>
    private const string BothScopes = "public_and_tenant";

    /// <summary>
    /// Resolves a catalog id by device title and, optionally, manufacturer. Returns an
    /// empty string when nothing matches.
    /// <para>
    /// The manufacturer is tried against two fields, because source data uses them
    /// interchangeably: the type-plate manufacturer first, then the currently responsible
    /// one, which often differs. Matching is case-insensitive
    /// (<see cref="FilterBuilder.FilterType.Matches"/>) so "seca 954" finds "Seca 954" —
    /// an equals filter would not.
    /// </para>
    /// </summary>
    public static string FindCatalogId(RequestData client, ITenantScope scope,
                                       string title, string? manufacturer = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        if (string.IsNullOrWhiteSpace(manufacturer))
            return Lookup(client, scope, (Field: "title", Value: title));

        var byTypePlate = Lookup(client, scope,
            (Field: "title", Value: title),
            (Field: "manufacturer_according_to_type_plate", Value: manufacturer));

        return string.IsNullOrEmpty(byTypePlate)
            ? Lookup(client, scope,
                (Field: "title", Value: title),
                (Field: "current_responsible_manufacturer", Value: manufacturer))
            : byTypePlate;
    }

    private static string Lookup(RequestData client, ITenantScope scope,
                                 params (string Field, string Value)[] conditions)
    {
        var filter = new FilterBuilder();
        foreach (var (field, value) in conditions)
            filter.Add(field, FilterBuilder.FilterType.Matches, FilterBuilder.Type.Text, value);

        var resource = $"{scope.Resource("device_models")}"
                     + $"?filter[scope]={BothScopes}&page[limit]=1&gridfilter={filter.Get()}";

        var content = client.Get(resource);
        return JsonApi.IsSuccess(client.StatusCode) ? JsonApi.FirstDataId(content) ?? "" : "";
    }
}
