using FluentAssertions;
using SamedisCare.Api.Routing;
using Xunit;

namespace SamedisCare.Api.Tests.Routing;

public class TenantScopeTests
{
    private const string Tenant = "6512ab3c9f1e4d0001a2b3c4";
    private const string Client = "70aa11bb22cc33dd44ee55ff";

    [Fact]
    public void Standard_baut_den_normalen_Tenant_Pfad()
        => TenantScope.Standard(Tenant).Resource("inventories")
            .Should().Be($"/api/v4/tenants/{Tenant}/inventories");

    [Fact]
    public void Enterprise_baut_den_client_bezogenen_Pfad()
        => TenantScope.Enterprise(Tenant, Client).Resource("inventories")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/clients/{Client}/inventories");

    [Fact]
    public void EnterpriseTenant_baut_den_uebergreifenden_Aggregat_Pfad()
        => TenantScope.EnterpriseTenant(Tenant).Resource("issues")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/issues");

    [Fact]
    public void Verschachtelte_Resourcen_bleiben_erhalten()
        => TenantScope.Enterprise(Tenant, Client).Resource("inventories/abc/uploads")
            .Should().Be($"/api/v4/enterprise/tenants/{Tenant}/clients/{Client}/inventories/abc/uploads");

    [Theory]
    [InlineData("inventories")]
    [InlineData("/inventories")]
    [InlineData("  inventories  ")]
    public void Fuehrende_Slashes_und_Whitespace_erzeugen_keinen_Doppel_Slash(string resource)
        => TenantScope.Standard(Tenant).Resource(resource)
            .Should().Be($"/api/v4/tenants/{Tenant}/inventories");

    [Fact]
    public void ApiVersion_ist_ueberschreibbar()
        => TenantScope.Standard(Tenant, "v5").Resource("inventories")
            .Should().Be($"/api/v5/tenants/{Tenant}/inventories");

    [Fact]
    public void Standard_ist_nicht_Enterprise_und_hat_keine_ClientId()
    {
        var scope = TenantScope.Standard(Tenant);
        scope.IsEnterprise.Should().BeFalse();
        scope.ClientId.Should().BeNull();
    }

    [Fact]
    public void Enterprise_meldet_Scope_und_ClientId()
    {
        var scope = TenantScope.Enterprise(Tenant, Client);
        scope.IsEnterprise.Should().BeTrue();
        scope.ClientId.Should().Be(Client);
    }

    [Fact]
    public void EnterpriseTenant_ist_Enterprise_aber_ohne_ClientId()
    {
        var scope = TenantScope.EnterpriseTenant(Tenant);
        scope.IsEnterprise.Should().BeTrue();
        scope.ClientId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Leerer_TenantId_wird_abgelehnt(string? tenantId)
    {
        var act = () => TenantScope.Standard(tenantId!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Leere_ClientId_wird_abgelehnt(string? clientId)
    {
        var act = () => TenantScope.Enterprise(Tenant, clientId!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Leere_Resource_wird_abgelehnt(string? resource)
    {
        var act = () => TenantScope.Standard(Tenant).Resource(resource!);
        act.Should().Throw<ArgumentException>();
    }
}
