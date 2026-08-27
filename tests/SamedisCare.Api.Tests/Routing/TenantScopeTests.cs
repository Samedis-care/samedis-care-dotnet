using FluentAssertions;
using SamedisCare.Api.Routing;
using Xunit;

namespace SamedisCare.Api.Tests.Routing;

public class TenantScopeTests
{
    private const string Tenant = "6512ab3c9f1e4d0001a2b3c4";
    private const string Client = "70aa11bb22cc33dd44ee55ff";

    [Fact]
    public void Standard_builds_the_normal_tenant_path()
        => TenantScope.Standard(Tenant).Resource("inventories")
            .Should().Be($"/api/v4/tenants/{Tenant}/inventories");

    [Fact]
    public void Enterprise_builds_the_client_scoped_path()
        => TenantScope.Enterprise(Tenant, Client).Resource("inventories")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/clients/{Client}/inventories");

    [Fact]
    public void EnterpriseTenant_builds_the_cross_facility_aggregate_path()
        => TenantScope.EnterpriseTenant(Tenant).Resource("issues")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/issues");

    [Fact]
    public void Nested_resources_are_preserved()
        => TenantScope.Enterprise(Tenant, Client).Resource("inventories/abc/uploads")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/clients/{Client}/inventories/abc/uploads");

    [Theory]
    [InlineData("inventories")]
    [InlineData("/inventories")]
    [InlineData("  inventories  ")]
    public void Leading_slashes_and_whitespace_do_not_produce_a_double_slash(string resource)
        => TenantScope.Standard(Tenant).Resource(resource)
            .Should().Be($"/api/v4/tenants/{Tenant}/inventories");

    [Fact]
    public void ApiVersion_can_be_overridden()
        => TenantScope.Standard(Tenant, "v5").Resource("inventories")
            .Should().Be($"/api/v5/tenants/{Tenant}/inventories");

    [Fact]
    public void Standard_is_not_enterprise_and_has_no_client_id()
    {
        var scope = TenantScope.Standard(Tenant);
        scope.IsEnterprise.Should().BeFalse();
        scope.ClientId.Should().BeNull();
    }

    [Fact]
    public void Enterprise_reports_scope_and_client_id()
    {
        var scope = TenantScope.Enterprise(Tenant, Client);
        scope.IsEnterprise.Should().BeTrue();
        scope.ClientId.Should().Be(Client);
    }

    [Fact]
    public void EnterpriseTenant_is_enterprise_but_has_no_client_id()
    {
        var scope = TenantScope.EnterpriseTenant(Tenant);
        scope.IsEnterprise.Should().BeTrue();
        scope.ClientId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_tenant_id_is_rejected(string? tenantId)
    {
        var act = () => TenantScope.Standard(tenantId!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_client_id_is_rejected(string? clientId)
    {
        var act = () => TenantScope.Enterprise(Tenant, clientId!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_resource_is_rejected(string? resource)
    {
        var act = () => TenantScope.Standard(Tenant).Resource(resource!);
        act.Should().Throw<ArgumentException>();
    }
}
