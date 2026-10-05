using FluentAssertions;
using SamedisCare.Api.V4.Enterprise;
using SamedisCare.Helper.Logging;
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

    private static string Page(int from, int count)
        => "{\"data\":[" + string.Join(",", Enumerable.Range(from, count)
            .Select(i => $"{{\"id\":\"t{i}\",\"type\":\"tenants\",\"attributes\":{{\"name\":\"Kunde {i}\"}}}}")) + "]}";

    // A full page (200) asks for the next one; the short page after it ends the loop.
    [Fact]
    public void List_pages_until_a_short_page()
    {
        var client = new FakeClient(url => url.Contains("page[number]=1&", StringComparison.Ordinal)
            ? (200, Page(1, 200))
            : (200, Page(201, 3)));

        var clients = EnterpriseClients.List(client, "v4", "p1", new NullSyncLog());

        clients.Should().HaveCount(203);
        client.Requests.Should().Equal(
            "/api/v4/enterprise/tenants/p1/clients?page[number]=1&page[limit]=200",
            "/api/v4/enterprise/tenants/p1/clients?page[number]=2&page[limit]=200");
    }

    [Fact]
    public void A_single_short_page_needs_one_request()
    {
        var client = new FakeClient(_ => (200, Page(1, 3)));

        EnterpriseClients.List(client, "v4", "p1", new NullSyncLog()).Should().HaveCount(3);
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public void A_failed_request_throws_with_the_status()
    {
        var act = () => EnterpriseClients.List(FakeClient.AlwaysStatus(403), "v4", "p1", new NullSyncLog());

        act.Should().Throw<InvalidOperationException>().WithMessage("*HTTP 403*");
    }

    [Fact]
    public void An_empty_service_world_id_throws_before_any_request()
    {
        var client = FakeClient.AlwaysStatus(200);

        var act = () => EnterpriseClients.List(client, "v4", " ", new NullSyncLog());

        act.Should().Throw<InvalidOperationException>();
        client.Requests.Should().BeEmpty();
    }
}
