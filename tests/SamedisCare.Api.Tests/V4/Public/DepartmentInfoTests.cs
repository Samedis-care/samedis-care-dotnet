using FluentAssertions;
using SamedisCare.Api.V4.Public;
using Xunit;

namespace SamedisCare.Api.Tests.V4.Public;

// DepartmentInfo is the parameter object for Departments.FindOrCreateDepartment. It has
// to stay free of any import-format knowledge — a CSV, LDAP or database source must all
// be able to fill it — so what is worth pinning is the defaults and the field mapping.
public class DepartmentInfoTests
{
    [Fact]
    public void Defaults_are_empty_strings_and_nulls_not_null_titles()
    {
        var info = new DepartmentInfo();

        info.Key.Should().BeEmpty();
        info.Title.Should().BeEmpty();
        info.Code.Should().BeNull();
        info.CostCenter.Should().BeNull();
    }

    [Fact]
    public void Carries_the_three_values_FindOrCreateDepartment_needs()
    {
        var info = new DepartmentInfo
        {
            Key = "ABT-100",
            Title = "Intensivstation",
            Code = "ITS",
            CostCenter = "KST-100",
        };

        info.Title.Should().Be("Intensivstation");
        info.Code.Should().Be("ITS");
        info.CostCenter.Should().Be("KST-100");
    }

    // Key exists purely for de-duplication in the calling tool and must never reach the
    // API — the overload forwards Title/Code/CostCenter only.
    [Fact]
    public void Key_is_independent_of_the_title()
    {
        var info = new DepartmentInfo { Key = "raw source value", Title = "Cleaned Title" };

        info.Key.Should().NotBe(info.Title);
    }
}
