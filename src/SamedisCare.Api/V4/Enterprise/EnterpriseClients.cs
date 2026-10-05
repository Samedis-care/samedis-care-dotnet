using Newtonsoft.Json;
using SamedisCare.Api.Http;
using SamedisCare.Api.Routing;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.V4.Enterprise;

/// <summary>
/// The client list of a service world: which tenants a service provider looks after.
///
/// <c>GET /api/{v}/enterprise/tenants/{provider}/clients</c>
///
/// This is the counterpart to <c>/user/tenants</c> in direct access. The difference is not
/// cosmetic: an external service provider is as a rule <b>not a member</b> of its clients'
/// tenants and does not appear in their user lists — it sees its clients only through this
/// endpoint.
/// </summary>
public static class EnterpriseClients
{
    public class Attributes
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("name2")] public string? Name2 { get; set; }
        [JsonProperty("town")] public string? Town { get; set; }
        [JsonProperty("short_name")] public string? ShortName { get; set; }
    }

    public class Data
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("attributes")] public Attributes? Attributes { get; set; }
    }

    public class Meta
    {
        [JsonProperty("total")] public int? Total { get; set; }
    }

    public class Root
    {
        [JsonProperty("data")] public List<Data>? Data { get; set; }
        [JsonProperty("meta")] public Meta? Meta { get; set; }
    }

    /// <summary>A client of the service world, reduced to what a picker shows.</summary>
    public record Client(string TenantId, string Name);

    private const int PageLimit = 200;

    /// <summary>
    /// All clients of the service world <paramref name="enterpriseTenantId"/>, paged through
    /// completely. Throws when the id is empty or the request fails — a client picker has
    /// nothing useful to show either way.
    /// </summary>
    public static IReadOnlyList<Client> List(
        RequestData samedis, string apiVersion, string enterpriseTenantId, ISyncLog log)
    {
        if (string.IsNullOrWhiteSpace(enterpriseTenantId))
            throw new InvalidOperationException(
                "samedis.enterprise_tenant_id is not set — without the service world there is no client list.");

        var scope = TenantScope.EnterpriseTenant(enterpriseTenantId, apiVersion);
        var clients = new List<Client>();
        var page = 1;

        while (true)
        {
            var resource = scope.Resource($"clients?page[number]={page}&page[limit]={PageLimit}");

            var response = samedis.Get(resource);
            if (samedis.StatusCode is < 200 or >= 300)
                throw new InvalidOperationException(
                    $"GET {resource} -> HTTP {samedis.StatusCode}. " +
                    "Does the account have access to this service world, and is the module " +
                    "'samedis-care-enterprise' active for the tenant?");

            var batch = ParsePage(response);
            clients.AddRange(batch.Clients);

            if (batch.Count < PageLimit) break;
            page++;
        }

        log.Info($"Service world {enterpriseTenantId}: {clients.Count} client tenant(s).");
        return clients;
    }

    /// <summary>
    /// One page of the client list. <c>Count</c> is the raw number of entries, so paging stops
    /// on a short page even when entries without an id were skipped.
    /// </summary>
    public static (IReadOnlyList<Client> Clients, int Count) ParsePage(string? json)
    {
        var batch = string.IsNullOrWhiteSpace(json)
            ? new List<Data>()
            : JsonConvert.DeserializeObject<Root>(json)?.Data ?? new List<Data>();

        var clients = new List<Client>();
        foreach (var c in batch)
        {
            var id = c.Id ?? c.Attributes?.Id;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = new[] { c.Attributes?.Name, c.Attributes?.Name2 }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .DefaultIfEmpty(id)
                .First()!;

            clients.Add(new Client(id, name));
        }

        return (clients, batch.Count);
    }
}
