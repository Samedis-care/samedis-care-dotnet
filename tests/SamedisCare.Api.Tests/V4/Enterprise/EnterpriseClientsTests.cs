using FluentAssertions;
using SamedisCare.Api.V4.Enterprise;
using Xunit;

namespace SamedisCare.Api.Tests.V4.Enterprise;

public class EnterpriseClientsTests
{
    [Fact]
    public void Name_falls_back_to_name2_and_then_to_the_id()
    {
        const string json = """
        { "data": [
          { "id": "t1", "type": "tenants", "attributes": { "name": "Klinikum Nord" } },
          { "id": "t2", "type": "tenants", "attributes": { "name": "", "name2": "MVZ Süd" } },
          { "id": "t3", "type": "tenants", "attributes": { } }
        ] }
        """;

        var (clients, count) = EnterpriseClients.ParsePage(json);

        count.Should().Be(3);
        clients.Should().Equal(
            new EnterpriseClients.Client("t1", "Klinikum Nord"),
            new EnterpriseClients.Client("t2", "MVZ Süd"),
            new EnterpriseClients.Client("t3", "t3"));
    }

    // Paging stops on a short page, so the raw count must include entries that were skipped.
    [Fact]
    public void Entries_without_an_id_are_skipped_but_still_counted()
    {
        const string json = """
        { "data": [ { "type": "tenants", "attributes": { "name": "ohne Id" } } ] }
        """;

        var (clients, count) = EnterpriseClients.ParsePage(json);

        clients.Should().BeEmpty();
        count.Should().Be(1);
    }

    [Fact]
    public void An_empty_body_is_an_empty_page()
        => EnterpriseClients.ParsePage("").Count.Should().Be(0);
}
