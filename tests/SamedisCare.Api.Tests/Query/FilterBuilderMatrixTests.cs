using FluentAssertions;
using System.Web;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Query;
using Xunit;
using FT = SamedisCare.Api.Query.FilterBuilder.FilterType;
using VT = SamedisCare.Api.Query.FilterBuilder.Type;

namespace SamedisCare.Api.Tests.Query;

// The server checks each condition against a closed key whitelist per field type and fails
// the whole request on a mismatch, so a wrong combination is not a degraded filter — it is a
// dead request. These tests pin the table against what the server actually implements.
public class FilterBuilderMatrixTests
{
    // HttpUtility.UrlDecode, not Uri.UnescapeDataString: the payload is form-encoded, so a
    // space travels as '+'. Rack decodes it back to a space — confirmed against production,
    // where a narrowed lookup on the title "Perfusor Space" returned the single expected
    // record. A literal '+' in the data is sent as %2b and therefore survives.
    private static JObject Decode(FilterBuilder b)
        => JObject.Parse(HttpUtility.UrlDecode(b.Get())!);

    private static JObject Entry(FilterBuilder b, string field)
        => (JObject)Decode(b)[field]!;

    [Theory]
    // text has no filterTo key at all, so a range cannot apply
    [InlineData(VT.Text, FT.InRange)]
    [InlineData(VT.Text, FT.GreaterThan)]
    [InlineData(VT.Text, FT.LessThanOrEqual)]
    // object_id likewise
    [InlineData(VT.ObjectId, FT.InRange)]
    [InlineData(VT.ObjectId, FT.Contains)]
    [InlineData(VT.ObjectId, FT.Matches)]
    // the date builders know five comparators, and these are not among them
    [InlineData(VT.Date, FT.Contains)]
    [InlineData(VT.Date, FT.InSet)]
    [InlineData(VT.Date, FT.BeforeToday)]
    [InlineData(VT.DateTime, FT.BeforeNow)]
    [InlineData(VT.DateTime, FT.LessThanOrEqual)]
    // the date-relative comparators live in the boolean builder
    [InlineData(VT.Number, FT.AfterToday)]
    [InlineData(VT.Number, FT.Contains)]
    [InlineData(VT.Bool, FT.Contains)]
    [InlineData(VT.Bool, FT.InRange)]
    // an array field supports emptiness only
    [InlineData(VT.Array, FT.Equals)]
    [InlineData(VT.Array, FT.Contains)]
    public void A_comparator_the_server_rejects_is_refused_here(VT type, FT filter)
    {
        var b = new FilterBuilder();

        var act = () => b.Add("f", filter, type, "x");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*does not apply*");
    }

    [Theory]
    [InlineData(VT.Text, FT.Contains)]
    [InlineData(VT.Text, FT.NotMatches)]
    [InlineData(VT.Text, FT.StartsWith)]
    [InlineData(VT.ObjectId, FT.Equals)]
    [InlineData(VT.ObjectId, FT.GreaterThanOrEqual)]
    [InlineData(VT.Number, FT.LessThanOrEqual)]
    [InlineData(VT.Date, FT.GreaterThan)]
    [InlineData(VT.DateTime, FT.GreaterThan)]
    [InlineData(VT.Bool, FT.AfterNow)]
    [InlineData(VT.Bool, FT.BeforeToday)]
    public void A_comparator_the_server_accepts_goes_through(VT type, FT filter)
    {
        var b = new FilterBuilder();

        ((Action)(() => b.Add("f", filter, type, "x"))).Should().NotThrow();
    }

    // Handled before the server dispatches on filterType, so they work everywhere.
    [Theory]
    [InlineData(VT.Text)]
    [InlineData(VT.Number)]
    [InlineData(VT.ObjectId)]
    [InlineData(VT.Date)]
    [InlineData(VT.DateTime)]
    [InlineData(VT.Bool)]
    [InlineData(VT.Array)]
    public void Emptiness_applies_to_every_type(VT type)
    {
        var b = new FilterBuilder();
        b.Add("a", FT.Empty, type);
        b.Add("z", FT.NotEmpty, type);

        var decoded = Decode(b);
        decoded["a"]!["type"]!.ToString().Should().Be("empty");
        decoded["z"]!["type"]!.ToString().Should().Be("notEmpty");
    }

    [Theory]
    [InlineData(VT.Text)]
    [InlineData(VT.Date)]
    [InlineData(VT.DateTime)]
    public void A_value_less_comparator_carries_no_value_key(VT type)
    {
        var b = new FilterBuilder();
        b.Add("f", FT.Empty, type);

        Entry(b, "f").Properties().Select(p => p.Name)
                     .Should().BeEquivalentTo("filterType", "type");
    }

    // The server raises "missing condition dateTo" instead of treating the range as
    // open-ended, so the whole request dies on an incomplete range.
    [Fact]
    public void A_range_without_an_upper_bound_is_refused()
    {
        var b = new FilterBuilder();

        var act = () => b.Add("f", FT.InRange, VT.Number, 1);

        act.Should().Throw<ArgumentException>().WithMessage("*needs an upper bound*");
    }

    [Fact]
    public void An_upper_bound_without_a_range_is_refused()
    {
        var b = new FilterBuilder();

        var act = () => b.Add("f", FT.Equals, VT.Number, 1, 9);

        act.Should().Throw<ArgumentException>().WithMessage("*only inRange*");
    }

    [Fact]
    public void A_number_range_uses_filter_and_filterTo()
    {
        var b = new FilterBuilder();
        b.Add("count", FT.InRange, VT.Number, 1, 9);

        var e = Entry(b, "count");
        e["filter"]!.ToString().Should().Be("1");
        e["filterTo"]!.ToString().Should().Be("9");
    }

    [Fact]
    public void A_date_range_uses_dateFrom_and_dateTo()
    {
        var b = new FilterBuilder();
        b.Add("d", FT.InRange, VT.Date, new DateTime(2026, 1, 2), new DateTime(2026, 3, 4));

        var e = Entry(b, "d");
        e["dateFrom"]!.ToString().Should().Be("2026-01-02");
        e["dateTo"]!.ToString().Should().Be("2026-03-04");
    }

    [Fact]
    public void A_datetime_range_uses_dateTimeFrom_and_dateTimeTo()
    {
        var b = new FilterBuilder();
        b.Add("t", FT.InRange, VT.DateTime,
              new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
              new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc));

        var e = Entry(b, "t");
        e.Properties().Select(p => p.Name)
         .Should().BeEquivalentTo("filterType", "type", "dateTimeFrom", "dateTimeTo");
    }

    // The server requires a JSON array here; a joined string is rejected outright.
    [Fact]
    public void A_set_comparator_serialises_a_collection_as_an_array()
    {
        var b = new FilterBuilder();
        b.Add("ids", FT.InSet, VT.ObjectId, new[] { "a", "b" });

        Entry(b, "ids")["filter"].Should().BeOfType<JArray>()
            .Which.Select(t => t.ToString()).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("a,b")]
    [InlineData(42)]
    public void A_set_comparator_refuses_anything_that_is_not_a_collection(object value)
    {
        var b = new FilterBuilder();

        var act = () => b.Add("ids", FT.InSet, VT.ObjectId, value);

        act.Should().Throw<ArgumentException>().WithMessage("*needs a collection*");
    }

    [Fact]
    public void The_array_type_maps_to_the_servers_name()
    {
        var b = new FilterBuilder();
        b.Add("tags", FT.NotEmpty, VT.Array);

        Entry(b, "tags")["filterType"]!.ToString().Should().Be("array");
    }

    // Removed on purpose: the server has no 'set' criterion builder, so the old mapping
    // produced a filterType it would reject.
    [Fact]
    public void There_is_no_set_field_type()
        => Enum.GetNames<VT>().Should().NotContain("Set");

    [Fact]
    public void The_table_covers_every_field_type()
        => FilterBuilder.Allowed.Keys.Should().BeEquivalentTo(Enum.GetValues<VT>());

    // The payload is encoded as a whole, so a value must not be pre-escaped. The versions
    // this replaces hand-replaced three characters inside the value and left the JSON raw.
    [Fact]
    public void The_whole_payload_is_encoded_and_survives_a_round_trip()
    {
        var b = new FilterBuilder();
        b.Add("title", FT.Equals, VT.Text, "A&B / C+D #E 100%");

        b.Get().Should().NotContain("&B").And.NotContain("#").And.Contain("%2b");
        Entry(b, "title")["filter"]!.ToString().Should().Be("A&B / C+D #E 100%");
    }

    [Fact]
    public void An_empty_builder_sends_the_web_views_empty_payload()
        => new FilterBuilder().Get().Should().Be("{}");

    [Fact]
    public void ToString_stays_readable_for_logs()
    {
        var b = new FilterBuilder();
        b.Add("f", FT.Equals, VT.Text, "x");

        b.ToString().Should().Contain("\"filter\": \"x\"").And.NotContain("%22");
    }
}
