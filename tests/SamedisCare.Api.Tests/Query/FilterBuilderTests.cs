using System.Web;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Query;
using Xunit;

namespace SamedisCare.Api.Tests.Query;

public class FilterBuilderTests
{
    private static JObject Decode(string encoded)
        => JObject.Parse(HttpUtility.UrlDecode(encoded)!);

    [Fact]
    public void An_empty_builder_yields_the_empty_object_the_webview_uses()
        => new FilterBuilder().Get().Should().Be("{}");

    [Fact]
    public void A_text_equals_filter_has_filterType_and_type_the_right_way_round()
    {
        var b = new FilterBuilder();
        b.Add("title", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, "Station A");

        var f = Decode(b.Get())["title"]!;
        f["filterType"]!.ToString().Should().Be("text", "filterType is the field's data type");
        f["type"]!.ToString().Should().Be("equals", "type is the comparator");
        f["filter"]!.ToString().Should().Be("Station A");
    }

    [Fact]
    public void Matches_maps_to_the_case_insensitive_comparator()
    {
        var b = new FilterBuilder();
        b.Add("title", FilterBuilder.FilterType.Matches, FilterBuilder.Type.Text, "seca 954");

        Decode(b.Get())["title"]!["type"]!.ToString().Should().Be("matches");
    }

    [Fact]
    public void A_date_filter_uses_dateFrom_rather_than_filter()
    {
        var b = new FilterBuilder();
        b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.Date,
              new DateTime(2026, 8, 27));

        var f = Decode(b.Get())["updated_at"]!;
        f["dateFrom"].Should().NotBeNull();
        f["filter"].Should().BeNull();
    }

    // This is what the hand-built gridfilter strings in Departments and Positions got
    // wrong: they escaped quotes but did not URL-encode, so a title containing '&' or '#'
    // terminated the query parameter and the server filtered on a truncated value.
    [Theory]
    [InlineData("Chirurgie & Orthopädie")]
    [InlineData("Station #3")]
    [InlineData("A+B")]
    [InlineData("say \"hi\"")]
    [InlineData("50% Auslastung")]
    public void A_value_with_query_string_metacharacters_survives_encoding(string value)
    {
        var b = new FilterBuilder();
        b.Add("title", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, value);
        var encoded = b.Get();

        encoded.Should().NotContain("&", "an unencoded '&' would end the query parameter");
        encoded.Should().NotContain("#", "an unencoded '#' would start a fragment");
        Decode(encoded)["title"]!["filter"]!.ToString().Should().Be(value);
    }

    [Fact]
    public void Several_fields_end_up_in_one_payload()
    {
        var b = new FilterBuilder();
        b.Add("title", FilterBuilder.FilterType.Matches, FilterBuilder.Type.Text, "seca");
        b.Add("manufacturer_according_to_type_plate", FilterBuilder.FilterType.Matches,
              FilterBuilder.Type.Text, "Seca GmbH");

        Decode(b.Get()).Properties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "title", "manufacturer_according_to_type_plate" });
    }

    [Fact]
    public void Clear_empties_the_builder_for_reuse()
    {
        var b = new FilterBuilder();
        b.Add("title", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, "x");
        b.Clear();

        b.Get().Should().Be("{}");
    }
}

// staff-sync filtered staff by id with filterType "object_id" via a hand-built template
// string. Text would not have matched, so the type has to be expressible here.
public class FilterBuilderObjectIdTests
{
    private static Newtonsoft.Json.Linq.JObject Decode(string encoded)
        => Newtonsoft.Json.Linq.JObject.Parse(System.Web.HttpUtility.UrlDecode(encoded)!);

    [Fact]
    public void ObjectId_maps_to_the_field_type_the_api_expects()
    {
        var b = new FilterBuilder();
        b.Add("id", FilterBuilder.FilterType.Equals, FilterBuilder.Type.ObjectId,
              "63f5c0491b57cc000df2b2c7");

        var f = Decode(b.Get())["id"]!;
        f["filterType"]!.ToString().Should().Be("object_id");
        f["filter"]!.ToString().Should().Be("63f5c0491b57cc000df2b2c7");
    }

    [Fact]
    public void An_unmapped_value_type_throws_instead_of_falling_back_to_text()
    {
        var b = new FilterBuilder();
        var act = () => b.Add("x", FilterBuilder.FilterType.Equals, (FilterBuilder.Type)99, "v");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_unmapped_filter_type_throws_too()
    {
        var b = new FilterBuilder();
        var act = () => b.Add("x", (FilterBuilder.FilterType)99, FilterBuilder.Type.Text, "v");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
