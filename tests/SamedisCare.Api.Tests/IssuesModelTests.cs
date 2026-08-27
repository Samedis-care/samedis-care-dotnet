using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace SamedisCare.Api.Tests;

// Taken from samedis-care-spl-sync (IssuesModelTests.cs). The PickServiceInterval tests
// from that file did NOT come along — they exercise DownloadEngine, i.e. SPL-specific
// selection logic, and stay in spl-sync.
public class IssuesModelTests
{
    [Fact]
    public void Deserializes_with_service_intervals_value_and_unit()
    {
        // Download contract: the service interval arrives as with_service_intervals[]
        // (value + unit day/week/month/year), see samedis-public.yaml.
        const string json = """
        { "data": [ { "id": "abc", "type": "issues",
          "attributes": { "issue_type": "maintenance", "title": "STK nach DGUV V3",
            "with_service_intervals": [
              { "category": "maintenance", "label": "STK", "value": 2, "unit": "year" }
            ] } } ] }
        """;

        var root = JsonConvert.DeserializeObject<Issues.Root>(json);

        var si = root!.Data![0].Attributes!.WithServiceIntervals;
        si.Should().HaveCount(1);
        si![0].Value.Should().Be(2);
        si[0].Unit.Should().Be("year");
        si[0].Category.Should().Be("maintenance");
    }

    [Fact]
    public void With_service_intervals_is_null_when_absent()
    {
        const string json = """
        { "data": [ { "id": "abc", "type": "issues",
          "attributes": { "issue_type": "maintenance", "title": "Wartung" } } ] }
        """;

        var root = JsonConvert.DeserializeObject<Issues.Root>(json);
        root!.Data![0].Attributes!.WithServiceIntervals.Should().BeNull();
    }
}
