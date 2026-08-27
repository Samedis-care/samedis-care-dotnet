using FluentAssertions;
using SamedisCare.Api.Common;
using Xunit;

namespace SamedisCare.Api.Tests.Common;

// ApiEnvelope replaces the habit of deserializing an unrelated resource model just to read
// meta.msg.message. It runs on error paths, so it must never throw.
public class ApiEnvelopeTests
{
    [Fact]
    public void Reads_the_error_message_from_the_meta_block()
    {
        const string json = """
        { "meta": { "msg": { "error": "forbidden", "message": "Access denied" } } }
        """;

        ApiEnvelope.ErrorMessage(json).Should().Be("Access denied");
    }

    [Fact]
    public void Works_regardless_of_which_resource_the_body_belongs_to()
    {
        // The point of the neutral envelope: a body full of unrelated fields still yields
        // the message, without needing that resource's model.
        const string json = """
        { "data": [ { "id": "x", "type": "staffs", "attributes": { "employee_no": "1" } } ],
          "meta": { "msg": { "message": "Sync stopped" } } }
        """;

        ApiEnvelope.ErrorMessage(json).Should().Be("Sync stopped");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{ "meta": {} }""")]
    [InlineData("""{ "meta": { "msg": {} } }""")]
    public void Returns_null_when_there_is_no_message(string? body)
        => ApiEnvelope.ErrorMessage(body).Should().BeNull();

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{ broken")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void Malformed_bodies_return_null_instead_of_throwing(string body)
    {
        // A proxy or gateway error page is a realistic response on a failing call, and it
        // must not turn a denied probe into an unhandled exception.
        var act = () => ApiEnvelope.ErrorMessage(body);
        act.Should().NotThrow();
        ApiEnvelope.ErrorMessage(body).Should().BeNull();
    }

    [Fact]
    public void CapabilityResult_describes_itself_for_logging()
    {
        new CapabilityResult(true, 200, null).ToString().Should().Be("allowed");
        new CapabilityResult(false, 403, "Access denied").ToString()
            .Should().Be("denied (403: Access denied)");
        new CapabilityResult(false, 500, null).ToString().Should().Be("denied (500)");
    }
}
