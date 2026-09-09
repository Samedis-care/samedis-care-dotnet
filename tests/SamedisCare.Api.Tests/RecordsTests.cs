using FluentAssertions;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
using SamedisCare.Helper.Logging;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>Records what was posted, and answers with a caller-supplied status and body.</summary>
internal sealed class FakeWriter : IApiClient
{
    public string LastError => string.Empty;
    public bool TestMode => false;

    private readonly int _status;
    private readonly string _body;

    public FakeWriter(int status, string body)
    {
        _status = status;
        _body = body;
    }

    public List<(string Resource, string Content)> Posts { get; } = new();

    public int StatusCode { get; private set; }
    public string LastContent { get; private set; } = string.Empty;

    public string Post(string resource, string content)
    {
        Posts.Add((resource, content));
        StatusCode = _status;
        LastContent = _body;
        return _body;
    }

    public string Get(string resource) => throw new NotSupportedException();
    public string Put(string r, string i, string c) => throw new NotSupportedException();
    public string PostDocument(string r, string f, string n) => throw new NotSupportedException();
}

public class RecordCreateTests
{
    private static readonly Dictionary<string, object?> Attributes = new() { ["title"] = "Radiologie" };

    private static string? Create(FakeWriter writer, RecordingSyncLog log)
        => Records.Create(writer, "departments", Attributes, log, "department 'Radiologie'");

    [Fact]
    public void A_created_record_returns_its_id()
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"d-1\"}}");

        Create(writer, new RecordingSyncLog()).Should().Be("d-1");
    }

    // Samedis reads params[:data][:field] directly -- there is no attributes subkey.
    [Fact]
    public void The_attributes_travel_inside_a_data_envelope()
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"d-1\"}}");

        Create(writer, new RecordingSyncLog());

        var posted = JObject.Parse(writer.Posts.Single().Content);
        posted["data"]!["title"]!.ToString().Should().Be("Radiologie");
        posted["data"]!["attributes"].Should().BeNull();
    }

    [Fact]
    public void A_rejected_create_yields_null()
    {
        var writer = new FakeWriter(422, "{\"meta\":{\"msg\":{\"error\":\"validation\"}}}");

        Create(writer, new RecordingSyncLog()).Should().BeNull();
    }

    // The failure has to name what could not be created and why, or a failed run leaves
    // nothing to act on.
    [Fact]
    public void A_rejected_create_is_logged_with_the_status_and_the_servers_reason()
    {
        var log = new RecordingSyncLog();
        var writer = new FakeWriter(422,
            "{\"meta\":{\"msg\":{\"error\":\"validation failed\",\"error_details\":\"title is taken\"}}}");

        Create(writer, log);

        var text = log.ToText();
        text.Should().Contain("department 'Radiologie'")
            .And.Contain("422")
            .And.Contain("validation failed")
            .And.Contain("title is taken");
    }

    // A body that is not the expected envelope must not be swallowed -- it is all the
    // diagnosis there is.
    [Fact]
    public void An_unrecognised_error_body_is_reported_verbatim()
    {
        var log = new RecordingSyncLog();
        var writer = new FakeWriter(500, "<html>Gateway Timeout</html>");

        Create(writer, log);

        log.ToText().Should().Contain("Gateway Timeout");
    }

    [Fact]
    public void An_empty_error_body_is_reported_as_such()
    {
        var log = new RecordingSyncLog();

        Create(new FakeWriter(503, ""), log);

        log.ToText().Should().Contain("empty");
    }

    [Fact]
    public void A_very_long_error_body_is_truncated()
    {
        var log = new RecordingSyncLog();

        Create(new FakeWriter(500, new string('x', 5000)), log);

        log.ToText().Length.Should().BeLessThan(1000);
    }

    // "Accepted, but here is nothing" leaves the caller with no id to reference, and carrying
    // on would attach later records to an empty one.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("")]
    public void A_success_without_an_id_counts_as_a_failure(string body)
    {
        var log = new RecordingSyncLog();

        Records.Create(new FakeWriter(201, body), "departments", Attributes, log, "department")
               .Should().BeNull();

        log.ToText().Should().Contain("no id");
    }
}

public class FindOrCreateTests
{
    private static readonly Dictionary<string, object?> Attributes = new() { ["title"] = "Radiologie" };

    [Fact]
    public void An_existing_record_is_not_created_again()
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}");

        Records.FindOrCreate(writer, "departments", () => "existing", Attributes,
                             new RecordingSyncLog(), "department")
               .Should().Be("existing");

        writer.Posts.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_record_is_created()
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}");

        Records.FindOrCreate(writer, "departments", () => null, Attributes,
                             new RecordingSyncLog(), "department")
               .Should().Be("new");

        writer.Posts.Should().ContainSingle();
    }

    // A caller whose master data is maintained elsewhere wants an unknown value reported,
    // not invented.
    [Fact]
    public void Creation_can_be_switched_off()
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}");

        Records.FindOrCreate(writer, "departments", () => null, Attributes,
                             new RecordingSyncLog(), "department", create: false)
               .Should().BeNull();

        writer.Posts.Should().BeEmpty();
    }

    [Fact]
    public void The_new_id_is_handed_to_the_seeding_callback()
    {
        string? seeded = null;
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}");

        Records.FindOrCreate(writer, "departments", () => null, Attributes,
                             new RecordingSyncLog(), "department", remember: id => seeded = id);

        seeded.Should().Be("new");
    }

    [Fact]
    public void Nothing_is_seeded_when_the_create_failed()
    {
        var seeded = false;

        Records.FindOrCreate(new FakeWriter(422, "{}"), "departments", () => null, Attributes,
                             new RecordingSyncLog(), "department", remember: _ => seeded = true);

        seeded.Should().BeFalse();
    }

    [Fact]
    public void The_cascade_is_consulted_exactly_once()
    {
        var calls = 0;

        Records.FindOrCreate(new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}"), "departments",
                             () => { calls++; return null; }, Attributes,
                             new RecordingSyncLog(), "department");

        calls.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_answer_from_the_cascade_counts_as_not_found(string found)
    {
        var writer = new FakeWriter(201, "{\"data\":{\"id\":\"new\"}}");

        Records.FindOrCreate(writer, "departments", () => found, Attributes,
                             new RecordingSyncLog(), "department")
               .Should().Be("new");
    }
}

/// <summary>
/// A create rejected because the record already exists. Samedis answers with the existing
/// record's id in meta.msg.error_details, and using it is what the caller wanted.
/// </summary>
public class DuplicateRecoveryTests
{
    private static readonly Dictionary<string, object?> Attributes = new() { ["title"] = "Seca 954" };

    private static string Rejected(string details)
        => "{\"meta\":{\"msg\":{\"error\":\"already exists\",\"error_details\":" + details + "}}}";

    [Fact]
    public void The_facilitys_own_duplicate_is_reused()
    {
        var log = new RecordingSyncLog();
        var writer = new FakeWriter(422, Rejected("{\"duplicate_of\":\"own-1\"}"));

        Records.Create(writer, "device_models", Attributes, log, "device model 'Seca 954'")
               .Should().Be("own-1");
    }

    [Fact]
    public void A_public_duplicate_is_reused_too()
    {
        var writer = new FakeWriter(422, Rejected("{\"public_duplicate_of\":\"public-1\"}"));

        Records.Create(writer, "device_models", Attributes, new RecordingSyncLog(), "device model")
               .Should().Be("public-1");
    }

    // The facility's own record is the one it can edit, so it wins.
    [Fact]
    public void The_facilitys_own_record_takes_precedence_over_the_public_one()
    {
        var writer = new FakeWriter(422,
            Rejected("{\"duplicate_of\":\"own-1\",\"public_duplicate_of\":\"public-1\"}"));

        Records.Create(writer, "device_models", Attributes, new RecordingSyncLog(), "device model")
               .Should().Be("own-1");
    }

    // Reusing is not an error, and logging it as one would train operators to ignore errors.
    [Fact]
    public void Reusing_is_reported_as_information_not_as_a_failure()
    {
        var log = new RecordingSyncLog();
        var writer = new FakeWriter(422, Rejected("{\"duplicate_of\":\"own-1\"}"));

        Records.Create(writer, "device_models", Attributes, log, "device model 'Seca 954'");

        log.Entries.Should().NotContain(e => e.Item1 == "ERROR");
        log.ToText().Should().Contain("already existed").And.Contain("own-1");
    }

    [Fact]
    public void A_rejection_that_names_nothing_is_still_a_failure()
    {
        var log = new RecordingSyncLog();
        var writer = new FakeWriter(422, Rejected("{}"));

        Records.Create(writer, "device_models", Attributes, log, "device model").Should().BeNull();
        log.Entries.Should().Contain(e => e.Item1 == "ERROR");
    }

    [Theory]
    [InlineData("{\"duplicate_of\":null}")]
    [InlineData("{\"duplicate_of\":\"\"}")]
    public void A_blank_duplicate_id_does_not_count(string details)
    {
        Records.Create(new FakeWriter(422, Rejected(details)), "device_models", Attributes,
                       new RecordingSyncLog(), "device model")
               .Should().BeNull();
    }

    [Fact]
    public void An_unrelated_error_body_is_unaffected()
    {
        Records.Create(new FakeWriter(500, "<html>boom</html>"), "device_models", Attributes,
                       new RecordingSyncLog(), "device model")
               .Should().BeNull();
    }

    [Fact]
    public void FindOrCreate_seeds_the_lookup_with_a_reused_duplicate()
    {
        string? seeded = null;
        var writer = new FakeWriter(422, Rejected("{\"duplicate_of\":\"own-1\"}"));

        Records.FindOrCreate(writer, "device_models", () => null, Attributes,
                             new RecordingSyncLog(), "device model", remember: id => seeded = id)
               .Should().Be("own-1");

        seeded.Should().Be("own-1", "the record exists, so later rows should find it from memory");
    }
}
