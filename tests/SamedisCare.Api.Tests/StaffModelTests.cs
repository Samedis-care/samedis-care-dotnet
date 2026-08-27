using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace SamedisCare.Api.Tests;

// Wire-level contract for the resources staff-sync synchronises. These models came from
// samedis-care-staff-sync, which had no tests at all — these are new, and they are the
// regression net for migrating that tool onto the library.
public class StaffModelTests
{
    [Fact]
    public void Staff_deserializes_the_fields_the_sync_writes()
    {
        const string json = """
        { "data": [ { "id": "s1", "type": "staffs",
          "attributes": {
            "id": "s1",
            "employee_no": "4711",
            "first_name": "Erika",
            "last_name": "Mustermann",
            "email": "erika@example.org",
            "login_allowed": true,
            "joined": "2026-01-15",
            "left": null
          } } ] }
        """;

        var staff = JsonConvert.DeserializeObject<Staffs.Root>(json)!.Data![0].Attributes!;

        staff.EmployeeNo.Should().Be("4711");
        staff.FirstName.Should().Be("Erika");
        staff.LastName.Should().Be("Mustermann");
        staff.Email.Should().Be("erika@example.org");
        staff.Joined.Should().Be("2026-01-15");
        staff.Left.Should().BeNull();
    }

    [Fact]
    public void Position_deserializes_title_and_external_id()
    {
        const string json = """
        { "data": [ { "id": "p1", "type": "positions",
          "attributes": { "id": "p1", "title": "Pflegefachkraft", "external_id": "POS-7" } } ] }
        """;

        var position = JsonConvert.DeserializeObject<Positions.Root>(json)!.Data![0].Attributes!;

        position.Title.Should().Be("Pflegefachkraft");
        position.ExternalId.Should().Be("POS-7");
    }

    [Fact]
    public void Department_deserializes_title_and_cost_center()
    {
        const string json = """
        { "data": [ { "id": "d1", "type": "departments",
          "attributes": { "id": "d1", "title": "Intensivstation", "cost_center_number": "KST-100",
                          "is_active": true } } ] }
        """;

        var department = JsonConvert.DeserializeObject<Departments.Root>(json)!.Data![0].Attributes!;

        department.Title.Should().Be("Intensivstation");
        department.CostCenterNumber.Should().Be("KST-100");
    }

    // The API returns `data` as a bare object for single-record responses and as an array
    // for collections. Every Root here relies on Helper.SingleOrArrayConverter to absorb
    // that — if the converter is ever dropped, these two cases catch it.
    [Fact]
    public void Data_as_single_object_is_accepted()
    {
        const string json = """
        { "data": { "id": "s1", "type": "staffs", "attributes": { "employee_no": "4711" } } }
        """;

        var root = JsonConvert.DeserializeObject<Staffs.Root>(json);

        root!.Data.Should().HaveCount(1);
        root.Data![0].Attributes!.EmployeeNo.Should().Be("4711");
    }

    [Fact]
    public void Data_as_empty_array_yields_no_records()
    {
        var root = JsonConvert.DeserializeObject<Staffs.Root>("""{ "data": [] }""");
        root!.Data.Should().BeEmpty();
    }
}
