using FluentAssertions;
using SamedisCare.Api.Common;
using Xunit;

namespace SamedisCare.Api.Tests.Common;

// These envelope readers were hand-rolled in three places: sync-trainings' own API layer
// and, inside this library, Departments and Positions. They run on responses from a live
// server, so tolerating an unexpected shape without throwing is the whole point.
public class JsonApiTests
{
    private const string ListResponse = """
    { "data": [ { "id": "a1", "attributes": { "catalog_id": "c1", "staff_id": "s1" } },
                { "id": "a2", "attributes": { "catalog_id": "c2", "staff_id": "" } } ] }
    """;

    private const string SingleResponse = """
    { "data": { "id": "only", "attributes": { "catalog_id": "cX" } } }
    """;

    [Fact]
    public void FirstData_takes_the_first_entry_of_a_list()
        => JsonApi.FirstData(ListResponse)!["id"]!.ToString().Should().Be("a1");

    [Fact]
    public void FirstData_takes_the_object_itself_for_a_single_record()
        => JsonApi.FirstData(SingleResponse)!["id"]!.ToString().Should().Be("only");

    [Fact]
    public void FirstDataId_reads_the_id_in_both_shapes()
    {
        JsonApi.FirstDataId(ListResponse).Should().Be("a1");
        JsonApi.FirstDataId(SingleResponse).Should().Be("only");
    }

    [Fact]
    public void AttributeSet_collects_across_entries_and_skips_empties()
    {
        JsonApi.AttributeSet(ListResponse, "catalog_id").Should().BeEquivalentTo(new[] { "c1", "c2" });
        JsonApi.AttributeSet(ListResponse, "staff_id").Should().BeEquivalentTo(new[] { "s1" });
    }

    [Fact]
    public void AttributeSet_is_case_insensitive_because_it_is_compared_against_source_data()
        => JsonApi.AttributeSet(ListResponse, "catalog_id").Should().Contain("C1");

    [Fact]
    public void AttributeSet_of_a_single_record_response_is_empty()
        => JsonApi.AttributeSet(SingleResponse, "catalog_id").Should().BeEmpty();

    [Fact]
    public void DataCount_counts_a_list_and_returns_zero_otherwise()
    {
        JsonApi.DataCount(ListResponse).Should().Be(2);
        JsonApi.DataCount(SingleResponse).Should().Be(0);
        JsonApi.DataCount("""{ "data": [] }""").Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("""{ "meta": {} }""")]
    public void Every_reader_tolerates_junk_without_throwing(string? body)
    {
        JsonApi.FirstData(body).Should().BeNull();
        JsonApi.FirstDataId(body).Should().BeNull();
        JsonApi.AttributeSet(body, "x").Should().BeEmpty();
        JsonApi.DataCount(body).Should().Be(0);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    [InlineData(0, false)]
    public void IsSuccess_covers_exactly_the_2xx_range(int status, bool expected)
        => JsonApi.IsSuccess(status).Should().Be(expected);
}
