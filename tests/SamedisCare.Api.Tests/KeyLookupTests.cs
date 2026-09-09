using FluentAssertions;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.Routing;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>
/// Whether a record is found through the server's find-by-field route or through a gridfilter
/// is a property of the backend, not of the call site. The tenant API mounts
/// <c>via/:via_name/:via_value</c> on 18 resources; the enterprise API mounts it on none.
/// Verified against config/routes and live: the same inventory answered 200 through the route
/// under the tenant path, 404 under the enterprise path, and was found by gridfilter in both.
/// </summary>
public class KeyLookupTests
{
    private const string Found = "{\"data\":[{\"id\":\"inv-1\"}],\"meta\":{\"total\":1}}";
    private const string NotFound =
        "{\"meta\":{\"msg\":{\"success\":false,\"error\":\"record_not_found_error\"}}}";

    private static (FakeClient Client, ResourceLookup Lookup) For(ITenantScope scope)
    {
        var client = new FakeClient(_ => (200, Found));
        return (client, Cascades.For(client, scope, "inventories"));
    }

    [Fact]
    public void The_tenant_scope_asks_through_the_route()
    {
        var (client, lookup) = For(TenantScope.Standard("T1"));

        lookup.ByUniqueField("external_id", "1400000").Should().Be("inv-1");

        client.Requests.Single().Should().Contain("/inventories/via/external_id/1400000");
    }

    [Fact]
    public void The_enterprise_scope_asks_through_a_gridfilter()
    {
        var (client, lookup) = For(TenantScope.Enterprise("T1", "C1"));

        lookup.ByUniqueField("external_id", "1400000").Should().Be("inv-1");

        var asked = client.Requests.Single();
        asked.Should().NotContain("/via/", "the route is mounted on no enterprise resource");
        asked.Should().Contain("gridfilter=");
        asked.Should().Contain("external_id");
    }

    [Fact]
    public void The_enterprise_tenant_scope_asks_the_same_way_as_its_clients()
    {
        var (client, _) = For(TenantScope.EnterpriseTenant("T1"));
        Cascades.For(client, TenantScope.EnterpriseTenant("T1"), "inventories")
                .ByUniqueField("external_id", "1400000");

        client.Requests.Should().NotContain(r => r.Contains("/via/"));
    }

    // Both mechanisms answer the same question, so a cascade must reach the same record
    // whichever one the scope selects.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cascade_resolves_the_same_record_under_either_mechanism(bool enterprise)
    {
        var scope = enterprise ? TenantScope.Enterprise("T1", "C1") : TenantScope.Standard("T1");
        var client = new FakeClient(url => url.Contains("1400000") ? (200, Found) : (404, NotFound));

        Cascades.Inventory(Cascades.For(client, scope, "inventories"),
                           id: null, externalId: "1400000", deviceNumber: "320000")
                .Should().Be("inv-1");
    }

    // A miss must stay a miss in both worlds -- and must not throw under Filter, where a 404
    // cannot occur at all because a gridfilter answers an empty list with 200.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unknown_key_resolves_to_nothing_under_either_mechanism(bool enterprise)
    {
        var scope = enterprise ? TenantScope.Enterprise("T1", "C1") : TenantScope.Standard("T1");
        var client = new FakeClient(url => url.Contains("gridfilter")
            ? (200, "{\"data\":[],\"meta\":{\"total\":0}}")
            : (404, NotFound));

        Cascades.For(client, scope, "inventories")
                .ByUniqueField("external_id", "GIBT-ES-NICHT").Should().BeNull();
    }

    // Seeding has to dispatch the same way the lookup does, or the run fills one cache and
    // asks the other -- a cache that never hits and never says so.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_seeded_key_is_answered_from_memory_under_either_mechanism(bool enterprise)
    {
        var scope = enterprise ? TenantScope.Enterprise("T1", "C1") : TenantScope.Standard("T1");
        var client = new FakeClient(_ => (500, "{}"));
        var lookup = Cascades.For(client, scope, "inventories");

        lookup.RememberUniqueField("external_id", "1400000", "inv-1");

        lookup.ByUniqueField("external_id", "1400000").Should().Be("inv-1");
        client.Requests.Should().BeEmpty("a seeded key must not reach the server at all");
    }

    [Fact]
    public void The_scope_states_the_mechanism_separately_from_the_path_family()
    {
        TenantScope.Standard("T1").KeyLookup.Should().Be(KeyLookup.Route);
        TenantScope.Enterprise("T1", "C1").KeyLookup.Should().Be(KeyLookup.Filter);
        TenantScope.EnterpriseTenant("T1").KeyLookup.Should().Be(KeyLookup.Filter);
    }
}
