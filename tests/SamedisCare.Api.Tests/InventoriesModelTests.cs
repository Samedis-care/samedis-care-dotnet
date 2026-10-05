using FluentAssertions;
using Newtonsoft.Json;
using SamedisCare.Api.V4.Public;
using Xunit;

namespace SamedisCare.Api.Tests;

public class InventoriesModelTests
{
    // The index sends service_intervals in its compatibility shape: a bare number for
    // maintenance in months, "value:unit:category" otherwise.
    [Fact]
    public void Deserializes_service_intervals_in_both_shapes()
    {
        const string json = """
        { "data": [ { "id": "i1", "type": "inventories",
          "attributes": { "device_number": "320000",
            "service_intervals": { "STK": 24, "SIP": "6:month:inspection" } } } ] }
        """;

        var map = JsonConvert.DeserializeObject<Inventories.Root>(json)!.Data![0].Attributes!.ServiceIntervals;

        map.Should().HaveCount(2);
        Convert.ToInt32(map!["STK"]).Should().Be(24);
        map["SIP"].Should().Be("6:month:inspection");
    }

    [Fact]
    public void Service_intervals_are_null_when_absent()
    {
        const string json = """{ "data": { "id": "i1", "type": "inventories", "attributes": { } } }""";

        JsonConvert.DeserializeObject<Inventories.Root>(json)!.Data![0].Attributes!.ServiceIntervals
            .Should().BeNull();
    }
}
