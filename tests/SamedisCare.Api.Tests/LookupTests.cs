using FluentAssertions;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.Query;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>
/// Records every URL asked for and answers from a caller-supplied rule set, so a test can
/// assert both the answer and — just as important for a cascade — which requests were never
/// made.
/// </summary>
internal sealed class FakeClient : IApiClient
{
    public string LastError => string.Empty;
    public bool TestMode => false;

    private readonly Func<string, (int Status, string Body)> _respond;
    public List<string> Requests { get; } = new();

    public FakeClient(Func<string, (int, string)> respond) => _respond = respond;

    /// <summary>
    /// Answers 200 for URLs containing the marker, reporting <paramref name="total"/> matches
    /// while still returning a single-record page — the shape the server sends for
    /// <c>page[limit]=1</c> over a larger result set.
    /// </summary>
    public static FakeClient AnsweringWithTotal(string marker, string id, int total)
        => new(url => url.Contains(marker, StringComparison.Ordinal)
            ? (200, $"{{\"data\":[{{\"id\":\"{id}\"}}],\"meta\":{{\"total\":{total}}}}}")
            : (404, "{}"));

    /// <summary>Answers with a fixed status and body to everything.</summary>
    public static FakeClient AlwaysStatus(int status, string body = "{}")
        => new(_ => (status, body));

    /// <summary>Answers 404 to everything.</summary>
    public static FakeClient NotFound() => Answering();

    /// <summary>
    /// Answers 200 with the given id for URLs containing a marker, 404 otherwise. An empty
    /// marker is rejected: <c>Contains("")</c> is true for every URL, so it would answer
    /// every request and quietly invert the meaning of a test.
    /// </summary>
    public static FakeClient Answering(params (string Marker, string Id)[] hits)
        => hits.Any(h => string.IsNullOrEmpty(h.Marker))
            ? throw new ArgumentException("An empty marker matches every URL.", nameof(hits))
            : new(url =>
        {
            foreach (var (marker, id) in hits)
                if (url.Contains(marker, StringComparison.Ordinal))
                    return (200, $"{{\"data\":[{{\"id\":\"{id}\"}}]}}");
            return (404, "{\"meta\":{\"msg\":{\"error\":\"not found\"}}}");
        });

    public int StatusCode { get; private set; }
    public string LastContent { get; private set; } = string.Empty;

    public string Get(string resource)
    {
        Requests.Add(resource);
        (StatusCode, LastContent) = _respond(resource);
        return LastContent;
    }

    public string Post(string resource, string content) => throw new NotSupportedException();
    public string Put(string resource, string id, string content) => throw new NotSupportedException();
    public string PostDocument(string r, string f, string n) => throw new NotSupportedException();
}

public class ResourceLookupTests
{
    private const string Oid = "507f1f77bcf86cd799439011";

    [Fact]
    public void ById_returns_the_id_the_server_confirms()
    {
        var client = FakeClient.Answering(($"/{Oid}", Oid));
        var lookup = new ResourceLookup(client, "inventories");

        lookup.ById(Oid).Should().Be(Oid);
    }

    // Source data routinely carries free text or a placeholder in an id column. Asking the
    // API about it only costs a round trip, so the shape is checked first.
    [Theory]
    [InlineData("not-an-id")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("507f1f77bcf86cd79943901")]   // 23 chars
    [InlineData("507f1f77bcf86cd799439011a")] // 25 chars
    public void ById_rejects_a_malformed_id_without_asking(string? id)
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.ById(id).Should().BeNull();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ByVia_uses_the_find_by_field_route()
    {
        var client = FakeClient.Answering(("via/external_id/ABC-1", "found"));
        var lookup = new ResourceLookup(client, "inventories");

        lookup.ByVia("external_id", "ABC-1").Should().Be("found");
        client.Requests.Single().Should().Be("inventories/via/external_id/ABC-1");
    }

    // The via value comes from a source system and is interpolated into a path segment.
    [Fact]
    public void ByVia_escapes_the_value()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "staffs");

        lookup.ByVia("email", "a b/c?d#e");

        client.Requests.Single().Should().Be("staffs/via/email/a%20b%2Fc%3Fd%23e");
    }

    [Fact]
    public void ByVia_is_generic_over_the_field_name()
    {
        var client = FakeClient.Answering(("via/employee_no/4711", "staff-1"));
        var lookup = new ResourceLookup(client, "staffs");

        lookup.ByVia("employee_no", "4711").Should().Be("staff-1");
    }

    [Fact]
    public void ByRegulatory_filters_on_the_embedded_field()
    {
        var client = FakeClient.Answering(("filter[regulatory][udi_id]=0403", "model-9"));
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("udi_id", "0403").Should().Be("model-9");
        client.Requests.Single().Should().Contain("filter[regulatory][udi_id]=0403");
    }

    [Fact]
    public void ByRegulatory_passes_extra_query_parameters_through()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("udi_id", "0403", "filter[scope]=public_and_tenant");

        client.Requests.Single().Should().Contain("filter[scope]=public_and_tenant");
    }

    [Fact]
    public void ByRegulatory_can_be_narrowed_by_further_fields()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("udi_id", "0403", null,
                            new (string, string?)[] { ("title", "Perfusor Space") });

        var url = Uri.UnescapeDataString(client.Requests.Single());
        url.Should().Contain("filter[regulatory][udi_id]=0403").And.Contain("\"title\"");
    }

    [Fact]
    public void A_narrowed_regulatory_lookup_is_cached_apart_from_the_plain_one()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("udi_id", "0403");
        lookup.ByRegulatory("udi_id", "0403", null, new (string, string?)[] { ("title", "X") });

        client.Requests.Should().HaveCount(2, "narrowing changes the question");
        lookup.CachedKeys.Should().Be(2);
    }

    // Production has device models sharing a UDI-DI. The first is taken so the sync can
    // proceed, but the caller must be able to see that the answer was not unambiguous.
    [Fact]
    public void A_lookup_that_matched_several_records_is_recorded_as_ambiguous()
    {
        var client = FakeClient.AnsweringWithTotal("filter[regulatory][eudamed_id]", "first-of-two", 2);
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("eudamed_id", "04045928000134").Should().Be("first-of-two");

        lookup.AmbiguousMatches.Should().ContainSingle()
              .Which.Should().Contain("eudamed_id").And.Contain("04045928000134");
    }

    [Fact]
    public void A_unique_match_is_not_recorded_as_ambiguous()
    {
        var client = FakeClient.AnsweringWithTotal("filter[regulatory][emtec_id]", "the-one", 1);
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByRegulatory("emtec_id", "217308").Should().Be("the-one");
        lookup.AmbiguousMatches.Should().BeEmpty();
    }

    [Fact]
    public void An_ambiguous_gridfilter_match_is_recorded_too()
    {
        var client = FakeClient.AnsweringWithTotal("gridfilter", "first", 4);
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByField("title", "elisa 300").Should().Be("first");
        lookup.AmbiguousMatches.Should().ContainSingle();
    }

    [Fact]
    public void ClearCache_also_forgets_the_ambiguity_notes()
    {
        var client = FakeClient.AnsweringWithTotal("gridfilter", "first", 4);
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByField("title", "elisa 300");
        lookup.ClearCache();

        lookup.AmbiguousMatches.Should().BeEmpty();
    }

    // The server slices an unknown label away and then returns NO records, which is
    // indistinguishable from "does not exist" — the one answer that makes a sync create a
    // duplicate. So a bad label must fail here, not there.
    [Fact]
    public void ByRegulatory_rejects_an_unknown_label_without_asking()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        var act = () => lookup.ByRegulatory("udi", "0403");

        act.Should().Throw<ArgumentException>().WithMessage("*not a regulatory label*");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ByRegulatory_checks_the_label_even_for_a_blank_value()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "device_models");

        ((Action)(() => lookup.ByRegulatory("nonsense", null))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_repeated_miss_is_asked_once()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        for (var i = 0; i < 5; i++)
            lookup.ByVia("external_id", "GONE").Should().BeNull();

        client.Requests.Should().HaveCount(1, "a miss is remembered too");
        lookup.CachedKeys.Should().Be(1);
    }

    [Fact]
    public void A_repeated_hit_is_asked_once()
    {
        var client = FakeClient.Answering(("via/external_id/HERE", "id-7"));
        var lookup = new ResourceLookup(client, "inventories");

        for (var i = 0; i < 5; i++)
            lookup.ByVia("external_id", "HERE").Should().Be("id-7");

        client.Requests.Should().HaveCount(1);
    }

    [Fact]
    public void The_cache_separates_lookup_kinds_that_share_a_value()
    {
        var client = FakeClient.Answering(("via/external_id/", "via-hit"));
        var lookup = new ResourceLookup(client, "inventories");

        lookup.ByVia("external_id", "X").Should().Be("via-hit");
        lookup.ByField("device_number", "X").Should().BeNull("a different kind must not read the via entry");
        lookup.CachedKeys.Should().Be(2);
    }

    [Fact]
    public void ClearCache_forgets_everything()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.ByVia("external_id", "A");
        lookup.ClearCache();
        lookup.ByVia("external_id", "A");

        client.Requests.Should().HaveCount(2);
        lookup.CachedKeys.Should().Be(1);
    }

    [Fact]
    public void First_stops_at_the_first_step_that_resolves()
    {
        var reached = new List<int>();
        var lookup = new ResourceLookup(FakeClient.NotFound(), "r");

        var id = lookup.First(
            () => { reached.Add(1); return null; },
            () => { reached.Add(2); return "hit"; },
            () => { reached.Add(3); return "later"; });

        id.Should().Be("hit");
        reached.Should().Equal(1, 2);
    }

    [Fact]
    public void ByFields_combines_the_conditions_and_drops_blank_ones()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByFields(new[] { ("title", "Seca 954"), ("manufacturer_according_to_type_plate", (string?)null) });

        var url = Uri.UnescapeDataString(client.Requests.Single());
        url.Should().Contain("\"title\"").And.NotContain("manufacturer_according_to_type_plate");
    }

    [Fact]
    public void ByFields_asks_nothing_when_every_condition_is_blank()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByFields(new[] { ("title", (string?)null), ("manufacturer", "  ") }).Should().BeNull();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void The_comparator_reaches_the_filter()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        lookup.ByField("title", "seca 954", FilterBuilder.FilterType.Matches);

        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"type\":\"matches\"");
    }
}

public class LookupFailureTests
{
    private const string Oid = "507f1f77bcf86cd799439011";

    // Verified against production: a via lookup on a field the model does not support
    // answers 500, not 4xx (trainings and departments both do). Reading that as "absent"
    // makes a sync create a duplicate of a record that is already there.
    [Fact]
    public void A_server_error_is_not_reported_as_a_missing_record()
    {
        var client = FakeClient.AlwaysStatus(500,
            "{\"meta\":{\"msg\":{\"error\":\"general_error\",\"message\":\"General error\"}}}");
        var lookup = new ResourceLookup(client, "trainings");

        var act = () => lookup.ByVia("external_id", "EXT-1");

        act.Should().Throw<LookupUnavailableException>()
           .Which.Should().Match<LookupUnavailableException>(e =>
               e.StatusCode == 500 && e.Resource == "trainings");
    }

    [Fact]
    public void The_message_names_the_likely_cause()
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(500), "departments");

        ((Action)(() => lookup.ByVia("external_id", "X")))
            .Should().Throw<LookupUnavailableException>()
            .WithMessage("*not supported for this resource*");
    }

    // A 404 is a real answer and must stay one, or nothing could ever be created.
    [Fact]
    public void A_missing_record_stays_a_missing_record()
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(404, RecordNotFound), "inventories");

        lookup.ByVia("external_id", "GONE").Should().BeNull();
    }

    /// <summary>The server's own "no such record", verbatim from the tenant API.</summary>
    private const string RecordNotFound =
        "{\"meta\":{\"msg\":{\"success\":false,\"message\":\"Record not found\",\"error\":\"record_not_found_error\"}}}";

    /// <summary>
    /// What Rails answers for a route it does not have -- no meta envelope, because the
    /// application never saw the request.
    /// </summary>
    private const string RouteNotFound = "{\"status\":404,\"error\":\"Not Found\"}";

    // The distinction is not academic. via/:via_name is mounted on 18 resources of the tenant
    // API and on NONE of the enterprise ones, verified both in config/routes and against the
    // live enterprise API. Counted as absence, every cascade there would drop to its weakest
    // key without a word in the log.
    [Fact]
    public void A_route_that_does_not_exist_is_not_a_record_that_does_not_exist()
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(404, RouteNotFound), "inventories");

        ((Action)(() => lookup.ByVia("external_id", "1400000")))
            .Should().Throw<LookupUnavailableException>()
            .WithMessage("*missing route*");
    }

    [Fact]
    public void An_empty_body_is_treated_as_the_router_talking()
        => ((Action)(() => new ResourceLookup(FakeClient.AlwaysStatus(404, ""), "inventories")
                              .ByVia("external_id", "X")))
               .Should().Throw<LookupUnavailableException>(
                   "nothing in the answer says the application considered the question");

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Every_server_error_class_is_guarded(int status)
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(status), "inventories");

        ((Action)(() => lookup.ByVia("external_id", "X"))).Should().Throw<LookupUnavailableException>();
    }

    // Only 404 answers the question that was asked. A token that lost a permission answers
    // 403, and reading that as "does not exist" would import the whole inventory a second
    // time -- quietly.
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(422)]
    public void Any_other_client_error_is_not_absence(int status)
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(status), "inventories");

        ((Action)(() => lookup.ByVia("external_id", "X")))
            .Should().Throw<LookupUnavailableException>();
    }

    [Fact]
    public void Only_404_is_absence()
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(404, RecordNotFound), "inventories");

        lookup.ByVia("external_id", "X").Should().BeNull();
    }

    [Fact]
    public void The_guard_applies_to_the_id_lookup_as_well()
    {
        var lookup = new ResourceLookup(FakeClient.AlwaysStatus(500), "inventories");

        ((Action)(() => lookup.ById(Oid))).Should().Throw<LookupUnavailableException>();
    }

    [Fact]
    public void The_guard_applies_to_gridfilter_and_regulatory_lookups()
    {
        var byField = new ResourceLookup(FakeClient.AlwaysStatus(500), "device_models");
        var byReg = new ResourceLookup(FakeClient.AlwaysStatus(500), "device_models");

        ((Action)(() => byField.ByField("title", "X"))).Should().Throw<LookupUnavailableException>();
        ((Action)(() => byReg.ByRegulatory("udi_id", "X"))).Should().Throw<LookupUnavailableException>();
    }
}

public class RegulatoryTests
{
    [Theory]
    [InlineData("ce")]
    [InlineData("udi_id")]
    [InlineData("eudamed_di")]
    [InlineData("eudamed_id")]
    [InlineData("emdn_code")]
    [InlineData("emtec_code")]
    [InlineData("emtec_id")]
    [InlineData("umdns_code")]
    [InlineData("gmdn_code")]
    [InlineData("ecri_risk_level")]
    [InlineData("us_fda")]
    [InlineData("eu_mdr")]
    public void Every_label_the_server_lists_is_accepted(string label)
        => Regulatory.Require(label).Should().Be(label);

    [Theory]
    [InlineData("udi")]        // the filter param, not a label
    [InlineData("UDI_ID")]     // the server compares exactly
    [InlineData("udi-id")]
    [InlineData("external_id")]
    public void Anything_else_is_rejected(string label)
        => ((Action)(() => Regulatory.Require(label))).Should().Throw<ArgumentException>();

    // The three groups must together be exactly what the server accepts, and must not
    // overlap — a label in two groups would mean the guidance contradicts itself.
    [Fact]
    public void The_groups_partition_the_accepted_labels()
    {
        var all = Regulatory.DeviceIdentifiers
                            .Concat(Regulatory.NomenclatureCodes)
                            .Concat(Regulatory.Classifications)
                            .ToList();

        all.Should().OnlyHaveUniqueItems();
        all.Should().BeEquivalentTo(Regulatory.Labels);
    }

    // The narrowest identifier first: a UDI-DI names one model, a Basic UDI-DI a family.
    [Fact]
    public void Device_identifiers_run_from_narrowest_to_broadest()
        => Regulatory.DeviceIdentifiers.Should()
                     .Equal("udi_id", "eudamed_id", "emtec_id", "emtec_code", "eudamed_di");

    // These classify rather than identify, so they must not be offered as cascade keys —
    // but they stay filterable, which is why they are still in Labels.
    [Fact]
    public void Nomenclature_codes_are_not_device_identifiers()
    {
        Regulatory.NomenclatureCodes.Should().Equal("emdn_code", "umdns_code", "gmdn_code");
        Regulatory.DeviceIdentifiers.Should().NotIntersectWith(Regulatory.NomenclatureCodes);
        Regulatory.NomenclatureCodes.Should().BeSubsetOf(Regulatory.Labels);
    }

    [Fact]
    public void Identifiers_keeps_the_declared_order_and_drops_what_is_missing()
    {
        var pairs = Regulatory.Identifiers(emtecId: "E-1", udiId: "U-1", eudamedDi: "   ");

        pairs.Select(p => p.Label).Should().Equal("udi_id", "emtec_id");
        pairs.Select(p => p.Value).Should().Equal("U-1", "E-1");
    }

    [Fact]
    public void Identifiers_is_empty_when_the_row_carries_none()
        => Regulatory.Identifiers().Should().BeEmpty();
}

public class CascadeTests
{
    private const string Oid = "507f1f77bcf86cd799439011";

    [Fact]
    public void Inventory_prefers_the_samedis_id()
    {
        var client = FakeClient.Answering(($"/{Oid}", Oid));
        var lookup = new ResourceLookup(client, "inventories");

        Cascades.Inventory(lookup, Oid, "EXT-1", "DEV-1").Should().Be(Oid);
        client.Requests.Should().HaveCount(1, "no weaker key may be consulted after a hit");
    }

    [Fact]
    public void Inventory_falls_back_through_external_id_to_the_device_number()
    {
        var client = FakeClient.Answering(("gridfilter", "by-number"));
        var lookup = new ResourceLookup(client, "inventories");

        Cascades.Inventory(lookup, null, "EXT-1", "DEV-1").Should().Be("by-number");
        client.Requests.Should().HaveCount(2);
        client.Requests[0].Should().Contain("via/external_id/EXT-1");
        client.Requests[1].Should().Contain("gridfilter");
    }

    // This is the invariant external-sync documents. The source may deliver a changed
    // device number for a device whose external_id still matches. Falling through would
    // pick a DIFFERENT record, and the following update would try to move this row's
    // external_id onto it — rejected by the unique index on (tenant_id, external_id).
    [Fact]
    public void A_hit_on_external_id_is_final_even_when_the_device_number_would_also_match()
    {
        var client = FakeClient.Answering(
            ("via/external_id/EXT-1", "the-right-record"),
            ("gridfilter", "a-different-record"));
        var lookup = new ResourceLookup(client, "inventories");

        Cascades.Inventory(lookup, null, "EXT-1", "DEV-CHANGED").Should().Be("the-right-record");
        client.Requests.Should().ContainSingle().Which.Should().Contain("via/external_id");
    }

    [Fact]
    public void Inventory_can_be_told_not_to_use_the_device_number()
    {
        var client = FakeClient.Answering(("gridfilter", "by-number"));
        var lookup = new ResourceLookup(client, "inventories");

        Cascades.Inventory(lookup, null, "EXT-1", "DEV-1", deviceNumberFallback: false).Should().BeNull();
        client.Requests.Should().NotContain(r => r.Contains("gridfilter"));
    }

    // For device models external_id is matched with a gridfilter, not the via route: that
    // route is mounted only on the MDM endpoint for this resource.
    [Fact]
    public void DeviceModel_never_uses_the_via_route()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca",
                             Regulatory.Identifiers(udiId: "0403"), externalId: "EXT-1");

        client.Requests.Should().NotContain(r => r.Contains("/via/"));
    }

    [Fact]
    public void DeviceModel_matches_external_id_with_a_gridfilter()
    {
        var client = FakeClient.Answering(("gridfilter", "by-external"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca", externalId: "EXT-1")
                .Should().Be("by-external");

        var url = Uri.UnescapeDataString(client.Requests.Single());
        url.Should().Contain("\"external_id\"").And.Contain("filter[scope]=public_and_tenant");
    }

    [Fact]
    public void DeviceModel_prefers_external_id_over_the_regulatory_identifiers()
    {
        var client = FakeClient.Answering(
            ("gridfilter", "by-external"),
            ("filter[regulatory]", "by-udi"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca",
                             Regulatory.Identifiers(udiId: "0403"), externalId: "EXT-1")
                .Should().Be("by-external");

        client.Requests.Should().NotContain(r => r.Contains("regulatory"));
    }

    [Fact]
    public void DeviceModel_resolves_by_a_regulatory_identifier_before_the_title_alone()
    {
        var client = FakeClient.Answering(("filter[regulatory][udi_id]=0403", "by-udi"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca", Regulatory.Identifiers(udiId: "0403"))
                .Should().Be("by-udi");

        // A title/manufacturer step would carry no regulatory filter at all.
        client.Requests.Should().OnlyContain(r => r.Contains("filter[regulatory]"));
    }

    // Every device-model request must carry the scope: without it the endpoint answers with
    // the tenant's own catalogs only and misses all public master data.
    [Fact]
    public void Every_device_model_request_carries_the_scope()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca",
                             Regulatory.Identifiers(udiId: "0403", emtecId: "E-1"),
                             externalId: "EXT-1");

        client.Requests.Should().NotBeEmpty();
        client.Requests.Should().OnlyContain(r => r.Contains("filter[scope]=public_and_tenant"));
    }

    // One eudamed_id covers both "Perfusor Space" and "Perfusor Space PCA" in production, so
    // the identifier is tried together with the title before it is trusted on its own.
    [Fact]
    public void DeviceModel_narrows_a_regulatory_identifier_by_the_title_first()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Perfusor Space", null,
                             Regulatory.Identifiers(eudamedId: "04045928000134"));

        var regulatoryRequests = client.Requests.Where(r => r.Contains("filter[regulatory]")).ToList();
        regulatoryRequests.Should().HaveCount(2);
        Uri.UnescapeDataString(regulatoryRequests[0]).Should().Contain("\"title\"");
        regulatoryRequests[1].Should().NotContain("gridfilter");
    }

    [Fact]
    public void DeviceModel_skips_the_narrowed_probe_when_there_is_no_title()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, null, null, Regulatory.Identifiers(udiId: "0403"));

        client.Requests.Where(r => r.Contains("filter[regulatory]")).Should().HaveCount(1);
    }

    // The point of the ordered list: a row may carry any subset, and each is tried in turn.
    [Fact]
    public void DeviceModel_walks_the_regulatory_identifiers_in_the_given_order()
    {
        var client = FakeClient.Answering(("filter[regulatory][emtec_id]=E-1", "by-emtec"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, null, null,
                             Regulatory.Identifiers(udiId: "0403", eudamedId: "EU-1", emtecId: "E-1"))
                .Should().Be("by-emtec");

        client.Requests.Should().HaveCount(3);
        client.Requests[0].Should().Contain("udi_id");
        client.Requests[1].Should().Contain("eudamed_id");
        client.Requests[2].Should().Contain("emtec_id");
    }

    // emtec_id is a device identifier, so it is a legitimate cascade key even though the
    // stored values are known to be patchy — that is a data question, not a capability one.
    [Fact]
    public void DeviceModel_accepts_emtec_as_the_only_identifier()
    {
        var client = FakeClient.Answering(("filter[regulatory][emtec_code]=EC-9", "by-emtec-code"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, null, null, Regulatory.Identifiers(emtecCode: "EC-9"))
                .Should().Be("by-emtec-code");
    }

    [Fact]
    public void DeviceModel_rejects_a_nomenclature_code_as_a_key()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "device_models");

        // The label is valid for filtering, so this reaches the server rather than throwing —
        // the guard against using it as an identity key is Regulatory.DeviceIdentifiers.
        Regulatory.Labels.Should().Contain("emdn_code");
        Regulatory.DeviceIdentifiers.Should().NotContain("emdn_code");
        lookup.ByRegulatory("emdn_code", "A01").Should().BeNull();
    }

    [Fact]
    public void DeviceModel_tries_both_manufacturer_fields()
    {
        var client = FakeClient.Answering(("current_responsible_manufacturer", "by-responsible"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca").Should().Be("by-responsible");

        var urls = client.Requests.Select(Uri.UnescapeDataString).ToList();
        urls.Should().HaveCount(2);
        urls[0].Should().Contain("manufacturer_according_to_type_plate");
        urls[1].Should().Contain("current_responsible_manufacturer");
    }

    [Fact]
    public void DeviceModel_searches_the_public_catalog_as_well_as_the_tenants_models()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "Seca 954", null);

        client.Requests.Single().Should().Contain("filter[scope]=public_and_tenant");
    }

    // Source data and catalog entries routinely differ only in casing.
    [Fact]
    public void DeviceModel_matches_the_title_case_insensitively_by_default()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "seca 954", null);

        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"type\":\"matches\"");
    }

    [Fact]
    public void DeviceModel_can_be_told_to_match_the_title_exactly()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, "seca 954", null, caseInsensitiveTitleMatch: false);

        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"type\":\"equals\"");
    }

    [Fact]
    public void ByTitle_walks_id_then_external_id_then_the_title_field()
    {
        var client = FakeClient.Answering(("gridfilter", "by-title"));
        var lookup = new ResourceLookup(client, "positions");

        Cascades.ByTitle(lookup, null, "EXT-9", "Nurse").Should().Be("by-title");

        client.Requests[0].Should().Contain("via/external_id/EXT-9");
        Uri.UnescapeDataString(client.Requests[1]).Should().Contain("\"title\"");
    }

    [Fact]
    public void ByTitle_can_use_another_field_name()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        Cascades.ByTitle(lookup, null, null, "Ventilator", titleField: "name");

        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"name\"");
    }
}

// Seeding exists so a record this run just created is findable by the next source row. The
// risk it carries is a hand-built key that does not match what the lookup computes -- a cache
// that silently never hits -- so these tests pin seed and lookup to the same key.
public class RememberTests
{
    private const string Oid = "507f1f77bcf86cd799439011";

    [Fact]
    public void A_remembered_id_is_answered_without_asking()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberId(Oid);

        lookup.ById(Oid).Should().Be(Oid);
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void A_remembered_via_value_is_answered_without_asking()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberVia("external_id", "EXT-1", "id-9");

        lookup.ByVia("external_id", "EXT-1").Should().Be("id-9");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void A_remembered_field_is_answered_without_asking()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberField("device_number", "DEV-1", "id-9");

        lookup.ByField("device_number", "DEV-1").Should().Be("id-9");
        client.Requests.Should().BeEmpty();
    }

    // Seeding must not answer a different question.
    [Fact]
    public void A_remembered_field_does_not_answer_another_comparator()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberField("device_number", "DEV-1", "id-9");

        lookup.ByField("device_number", "DEV-1", FilterBuilder.FilterType.Matches).Should().BeNull();
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public void A_remembered_via_value_does_not_answer_another_field()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "staffs");

        lookup.RememberVia("external_id", "X", "id-1");

        lookup.ByVia("employee_no", "X").Should().BeNull();
    }

    [Fact]
    public void Values_are_trimmed_on_both_sides()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberVia("external_id", "  EXT-1  ", "  id-9  ");

        lookup.ByVia("external_id", "EXT-1").Should().Be("id-9");
        client.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-object-id")]
    public void A_malformed_id_is_not_remembered(string? id)
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "inventories");

        lookup.RememberId(id);

        lookup.CachedKeys.Should().Be(0);
    }

    [Fact]
    public void Blank_values_are_not_remembered()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "inventories");

        lookup.RememberVia("external_id", "", "id-1");
        lookup.RememberVia("external_id", "X", "");
        lookup.RememberField("device_number", "", "id-1");

        lookup.CachedKeys.Should().Be(0);
    }

    [Fact]
    public void ClearCache_forgets_what_was_remembered()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "inventories");

        lookup.RememberVia("external_id", "EXT-1", "id-9");
        lookup.ClearCache();

        lookup.ByVia("external_id", "EXT-1").Should().BeNull();
    }
}

public class RememberAliasTests
{
    private const string Source = "507f1f77bcf86cd799439011";
    private const string Target = "507f1f77bcf86cd799439022";

    // A source id can be an alias for a different record. Remembering it as resolving to
    // itself would hand back the wrong id for every later row that uses the source's key.
    [Fact]
    public void An_id_can_resolve_to_a_different_record()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "inventories");

        lookup.RememberId(Source, Target);

        lookup.ById(Source).Should().Be(Target);
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Without_a_target_an_id_resolves_to_itself()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "inventories");

        lookup.RememberId(Source);

        lookup.ById(Source).Should().Be(Source);
    }

    [Fact]
    public void A_blank_target_is_the_same_as_none()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "inventories");

        lookup.RememberId(Source, "   ");

        lookup.ById(Source).Should().Be(Source);
    }
}

public class DeviceModelTitleGuardTests
{
    // A manufacturer is not an identifier. ByFields drops a blank condition, so without a
    // guard a row carrying only a manufacturer would resolve to whichever of that maker's
    // models the server returns first -- and the sync would attach the device to it.
    [Fact]
    public void A_manufacturer_without_a_title_resolves_nothing()
    {
        var client = FakeClient.Answering(("gridfilter", "some-model-by-that-maker"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, null, "seca").Should().BeNull();
        client.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_counts_as_no_title(string title)
    {
        var client = FakeClient.Answering(("gridfilter", "x"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, title, "seca").Should().BeNull();
        client.Requests.Should().BeEmpty();
    }

    // The stronger keys still work on their own -- they identify a device without a title.
    [Fact]
    public void A_regulatory_identifier_still_resolves_without_a_title()
    {
        var client = FakeClient.Answering(("filter[regulatory][udi_id]=0403", "by-udi"));
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.DeviceModel(lookup, null, null, null, Regulatory.Identifiers(udiId: "0403"))
                .Should().Be("by-udi");
    }
}

public class RememberFieldsTests
{
    [Fact]
    public void A_remembered_condition_set_is_answered_without_asking()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "buildings");
        var conditions = new (string, string?)[] { ("title", "Haus A"), ("property_id", "p1") };

        lookup.RememberFields(conditions, "b-1");

        lookup.ByFields(conditions).Should().Be("b-1");
        client.Requests.Should().BeEmpty();
    }

    // Seeding a subset would answer a narrower question with a record found under broader
    // conditions -- a different building on the same property, for instance.
    [Fact]
    public void A_partially_blank_condition_set_is_not_remembered()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "buildings");

        lookup.RememberFields(new (string, string?)[] { ("title", "Haus A"), ("property_id", "") }, "b-1");

        lookup.CachedKeys.Should().Be(0);
    }

    [Fact]
    public void A_remembered_set_does_not_answer_a_different_set()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "buildings");

        lookup.RememberFields(new (string, string?)[] { ("title", "Haus A"), ("property_id", "p1") }, "b-1");

        lookup.ByFields(new (string, string?)[] { ("title", "Haus A"), ("property_id", "p2") })
              .Should().BeNull();
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public void The_single_field_convenience_uses_the_same_key()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "positions");

        lookup.RememberField("title", "Nurse", "p-1");

        lookup.ByField("title", "Nurse").Should().Be("p-1");
        client.Requests.Should().BeEmpty();
    }
}

public class QueryScopedCacheTests
{
    // The same field and value under two scopes are two different questions. Sharing a cache
    // key would hand the tenant-scoped caller the public catalog's record.
    [Fact]
    public void The_same_field_under_two_scopes_is_not_one_cache_entry()
    {
        var client = FakeClient.Answering(
            ("filter[scope]=tenant&", "tenant-record"),
            ("filter[scope]=public_and_tenant", "public-record"));
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByField("title", "Beatmung", FilterBuilder.FilterType.Equals,
                       "filter[scope]=public_and_tenant").Should().Be("public-record");
        lookup.ByField("title", "Beatmung", FilterBuilder.FilterType.Equals,
                       "filter[scope]=tenant&").Should().Be("tenant-record");

        lookup.CachedKeys.Should().Be(2);
    }

    [Fact]
    public void Seeding_is_scoped_the_same_way()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.RememberField("title", "Beatmung", "t-1",
                             FilterBuilder.FilterType.Equals, "filter[scope]=tenant");

        lookup.ByField("title", "Beatmung", FilterBuilder.FilterType.Equals, "filter[scope]=tenant")
              .Should().Be("t-1");
        client.Requests.Should().BeEmpty();

        lookup.ByField("title", "Beatmung").Should().BeNull("that is a different question");
        client.Requests.Should().ContainSingle();
    }
}

public class ByConditionsTests
{
    // The mixed set is the point: "belongs to this tenant AND has no parent" needs one
    // condition that compares a value and one that asserts absence.
    [Fact]
    public void A_mixed_condition_set_reaches_the_filter()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByConditions(new[]
        {
            Condition.Id("tenant_id", "507f1f77bcf86cd799439011"),
            Condition.Empty("parent_id", FilterBuilder.Type.ObjectId),
        });

        var url = Uri.UnescapeDataString(client.Requests.Single());
        url.Should().Contain("\"tenant_id\"").And.Contain("\"object_id\"");
        url.Should().Contain("\"parent_id\"").And.Contain("\"empty\"");
    }

    // The value-less comparators must survive the blank filtering that drops empty values.
    [Fact]
    public void A_value_less_condition_is_not_dropped_as_blank()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByConditions(new[] { Condition.Empty("parent_id", FilterBuilder.Type.ObjectId) });

        client.Requests.Should().ContainSingle();
        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"empty\"");
    }

    [Fact]
    public void A_blank_valued_condition_is_dropped()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByConditions(new[]
        {
            Condition.Text("title", "Beatmung"),
            Condition.Id("tenant_id", "   "),
        });

        Uri.UnescapeDataString(client.Requests.Single()).Should().NotContain("tenant_id");
    }

    [Fact]
    public void Nothing_is_asked_when_every_condition_is_blank()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByConditions(new[] { Condition.Text("title", null) }).Should().BeNull();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void The_scope_is_part_of_the_cache_key()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");
        var conditions = new[] { Condition.Text("title", "Beatmung") };

        lookup.ByConditions(conditions, "filter[scope]=tenant");
        lookup.ByConditions(conditions, "filter[scope]=public_and_tenant");

        client.Requests.Should().HaveCount(2);
        lookup.CachedKeys.Should().Be(2);
    }

    [Fact]
    public void A_repeated_condition_set_is_answered_from_memory()
    {
        var client = FakeClient.Answering(("gridfilter", "t-1"));
        var lookup = new ResourceLookup(client, "device_types");
        var conditions = new[] { Condition.Text("title", "Beatmung") };

        for (var i = 0; i < 4; i++)
            lookup.ByConditions(conditions).Should().Be("t-1");

        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public void Values_are_trimmed_before_they_are_sent()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_types");

        lookup.ByConditions(new[] { Condition.Text("title", "  Beatmung  ") });

        Uri.UnescapeDataString(client.Requests.Single()).Should().Contain("\"Beatmung\"");
    }
}

public class RememberDeviceModelTests
{
    // The seeding has to ask the same question the cascade asks, or it never gets hit. That is
    // exactly why it lives next to the cascade instead of at the call site.
    [Fact]
    public void A_seeded_model_is_found_by_the_cascade_without_a_request()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.RememberDeviceModel(lookup, "Seca 954", "seca", "m-1");

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca").Should().Be("m-1");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void The_case_sensitivity_has_to_match_the_lookup()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.RememberDeviceModel(lookup, "Seca 954", "seca", "m-1", caseInsensitiveTitleMatch: false);

        Cascades.DeviceModel(lookup, null, "Seca 954", "seca", caseInsensitiveTitleMatch: false)
                .Should().Be("m-1");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void A_model_without_a_manufacturer_is_seeded_under_the_title_alone()
    {
        var client = FakeClient.NotFound();
        var lookup = new ResourceLookup(client, "device_models");

        Cascades.RememberDeviceModel(lookup, "Seca 954", null, "m-1");

        Cascades.DeviceModel(lookup, null, "Seca 954", null).Should().Be("m-1");
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_seeded_without_a_title_or_an_id()
    {
        var lookup = new ResourceLookup(FakeClient.NotFound(), "device_models");

        Cascades.RememberDeviceModel(lookup, null, "seca", "m-1");
        Cascades.RememberDeviceModel(lookup, "Seca 954", "seca", null);

        lookup.CachedKeys.Should().Be(0);
    }
}
