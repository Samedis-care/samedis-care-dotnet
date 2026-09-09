using FluentAssertions;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.V4.Common;
using SamedisCare.Helper.Logging;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>
/// Two flags decide how every inventory of a run is written: whether the tenant uses the
/// extended location hierarchy, and whether it uses profit centres. Guessing them writes a
/// whole import into the wrong shape and reports a clean run, which is why an unreadable
/// answer stops instead of falling back — as samedis-care-external-sync's README has said all
/// along, while the code quietly did the opposite.
/// </summary>
public class TenantSettingsTests
{
    private static Tenant.Settings Read(int status, string body)
        => Tenant.GetSettings(new FakeClient(_ => (status, body)), "v4", "T1", new ConsoleSyncLog(0));

    private const string Settings =
        "{\"data\":{\"id\":\"T1\",\"attributes\":{\"name\":\"SyncTest\",\"tenant_id\":\"T1\"," +
        "\"use_extended_device_locations\":true,\"use_profit_centers\":true}}}";

    [Fact]
    public void The_flags_are_read_from_the_response()
    {
        var settings = Read(200, Settings);

        settings.Name.Should().Be("SyncTest");
        settings.UseExtendedDeviceLocations.Should().BeTrue();
        settings.UseProfitCenters.Should().BeTrue();
        settings.LocationMode.Should().Be("property");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public void An_unreadable_answer_stops_rather_than_guessing(int status)
        => ((Action)(() => Read(status, "{}")))
               .Should().Throw<LookupUnavailableException>(
                   "defaulting both flags to false sends every inventory down the wrong path");

    // A 200 that carries nothing is the same problem wearing a success code.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"id\":\"T1\"}}")]
    public void A_response_without_attributes_is_not_an_answer_either(string body)
        => ((Action)(() => Read(200, body))).Should().Throw<LookupUnavailableException>();

    // Absent flags mean the tenant does not use these features. That IS an answer.
    [Fact]
    public void A_tenant_that_uses_neither_feature_reads_as_false()
    {
        var settings = Read(200, "{\"data\":{\"attributes\":{\"name\":\"Plain\"}}}");

        settings.UseExtendedDeviceLocations.Should().BeFalse();
        settings.UseProfitCenters.Should().BeFalse();
        settings.LocationMode.Should().Be("standard");
    }
}
