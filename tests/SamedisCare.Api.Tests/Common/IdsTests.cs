using FluentAssertions;
using SamedisCare.Api.Common;
using Xunit;

namespace SamedisCare.Api.Tests.Common;

// Source data routinely carries a placeholder or free text in an id column, so this check
// decides whether a lookup is worth making at all. Three tools had their own copy.
public class IdsTests
{
    [Theory]
    [InlineData("63f5c0491b57cc000df2b2c7")]
    [InlineData("000000000000000000000000")]
    [InlineData("ABCDEF012345678901234567")]
    [InlineData("  63f5c0491b57cc000df2b2c7  ")]
    public void A_24_character_hex_value_is_an_object_id(string value)
        => Ids.IsObjectId(value).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("63f5c0491b57cc000df2b2c")]        // 23
    [InlineData("63f5c0491b57cc000df2b2c78")]      // 25
    [InlineData("63f5c0491b57cc000df2b2g7")]       // 'g'
    [InlineData("<catalog id>")]
    [InlineData("Seca 954")]
    [InlineData("63f5c049-1b57-cc00-0df2-b2c7")]
    public void Anything_else_is_not(string? value)
        => Ids.IsObjectId(value).Should().BeFalse();
}

// ErrorDetail is what a person reads in the log when a write fails, so it has to survive
// every shape the server and the infrastructure in front of it can produce.
public class ApiEnvelopeErrorDetailTests
{
    [Fact]
    public void All_three_parts_are_joined()
        => ApiEnvelope.ErrorDetail("""
           { "meta": { "msg": { "error": "validation_failed", "message": "Title cannot be blank",
                                "error_details": "title: blank" } } }
           """)
           .Should().Be("validation_failed — Title cannot be blank — title: blank");

    [Fact]
    public void Missing_parts_are_left_out_without_stray_separators()
        => ApiEnvelope.ErrorDetail("""{ "meta": { "msg": { "message": "Access denied" } } }""")
           .Should().Be("Access denied");

    // The server sends these when there is nothing to say; they must not reach the log.
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Placeholder_error_details_are_dropped(string details)
        => ApiEnvelope.ErrorDetail($$"""
           { "meta": { "msg": { "message": "Nope", "error_details": {{(details == "null" ? "null" : $"\"{details}\"")}} } } }
           """)
           .Should().Be("Nope");

    [Fact]
    public void Structured_error_details_are_serialized_rather_than_dropped()
        => ApiEnvelope.ErrorDetail("""
           { "meta": { "msg": { "message": "Nope", "error_details": { "title": ["blank"] } } } }
           """)
           .Should().Contain("title");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{}")]
    [InlineData("""{ "meta": {} }""")]
    public void Junk_and_missing_blocks_yield_an_empty_string(string? body)
        => ApiEnvelope.ErrorDetail(body).Should().BeEmpty();
}
