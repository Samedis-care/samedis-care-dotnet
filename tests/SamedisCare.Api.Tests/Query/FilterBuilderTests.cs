using System.Web;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Query;
using Xunit;

namespace SamedisCare.Api.Tests.Query;

public class FilterBuilderTests
{
    // DateParseHandling.None matters here: JObject.Parse would otherwise turn an ISO
    // string into a JTokenType.Date, and ToString() on that renders it in the current
    // culture - which makes an assertion about the wire format read as a failure when the
    // wire format is in fact correct.
    internal static JObject Decode(string encoded)
        => JsonApiDecode(HttpUtility.UrlDecode(encoded)!);

    internal static JObject JsonApiDecode(string json)
    {
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json))
        {
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
        };
        return JObject.Load(reader);
    }

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
    private static JObject Decode(string encoded) => FilterBuilderTests.Decode(encoded);

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

// Date vs DateTime is not cosmetic. Verified against the backend's gridfilter parser in
// samedis-care-backend app/models/application_document.rb:
//
//   filterType "date"     -> Date.parse(value); greaterThan compares $gt end_of_day
//   filterType "dateTime" -> Time.parse(value); greaterThan compares $gt the instant
//
// So a "changed since <last run>" filter sent as a date drops every record from the day
// of the last run. Both the key and the value format have to be right.
public class FilterBuilderDateVsDateTimeTests
{
    private static JObject Decode(string encoded) => FilterBuilderTests.Decode(encoded);

    private static readonly DateTime Instant =
        new(2026, 8, 27, 14, 30, 15, 123, DateTimeKind.Utc);

    [Fact]
    public void Date_sends_dateFrom_with_a_day_only_value()
    {
        var b = new FilterBuilder();
        b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.Date, Instant);

        var f = Decode(b.Get())["updated_at"]!;
        f["filterType"]!.ToString().Should().Be("date");
        f["dateFrom"]!.ToString().Should().Be("2026-08-27");
        f["dateTimeFrom"].Should().BeNull();
        f["filter"].Should().BeNull("the backend whitelists exactly one value key per filterType");
    }

    [Fact]
    public void DateTime_sends_dateTimeFrom_with_the_full_utc_instant()
    {
        var b = new FilterBuilder();
        b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.DateTime, Instant);

        var f = Decode(b.Get())["updated_at"]!;
        f["filterType"]!.ToString().Should().Be("dateTime");
        f["dateTimeFrom"]!.ToString().Should().Be("2026-08-27T14:30:15.123Z");
        f["dateFrom"].Should().BeNull();
        f["filter"].Should().BeNull();
    }

    [Fact]
    public void A_local_time_is_converted_to_utc_rather_than_sent_as_is()
    {
        var local = new DateTimeOffset(2026, 8, 27, 16, 30, 0, TimeSpan.FromHours(2));

        var b = new FilterBuilder();
        b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.DateTime, local);

        Decode(b.Get())["updated_at"]!["dateTimeFrom"]!.ToString()
            .Should().Be("2026-08-27T14:30:00.000Z");
    }

    // This is the regression that matters: an incremental sync must not lose the time.
    [Fact]
    public void The_two_types_do_not_produce_the_same_payload()
    {
        var asDate = new FilterBuilder();
        asDate.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.Date, Instant);
        var asDateTime = new FilterBuilder();
        asDateTime.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.DateTime, Instant);

        asDate.Get().Should().NotBe(asDateTime.Get());
    }
}

// empty / notEmpty compare against nothing. The backend maps them to $in [nil, ""] and
// $nin [nil, ""], and validates the condition's keys against a closed whitelist.
public class FilterBuilderEmptyTests
{
    private static JObject Decode(string encoded) => FilterBuilderTests.Decode(encoded);

    [Theory]
    [InlineData(FilterBuilder.FilterType.Empty, "empty")]
    [InlineData(FilterBuilder.FilterType.NotEmpty, "notEmpty")]
    public void They_carry_no_value_key_at_all(FilterBuilder.FilterType type, string expected)
    {
        var b = new FilterBuilder();
        b.Add("external_id", type, FilterBuilder.Type.Text);

        var f = Decode(b.Get())["external_id"]!;
        f["type"]!.ToString().Should().Be(expected);
        f["filter"].Should().BeNull();
        f["dateFrom"].Should().BeNull();
        ((Newtonsoft.Json.Linq.JObject)f).Properties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "filterType", "type" });
    }

    [Fact]
    public void A_value_passed_with_them_is_ignored_rather_than_sent()
    {
        var b = new FilterBuilder();
        b.Add("external_id", FilterBuilder.FilterType.Empty, FilterBuilder.Type.Text, "ignored");

        Decode(b.Get())["external_id"]!["filter"].Should().BeNull();
    }
}
