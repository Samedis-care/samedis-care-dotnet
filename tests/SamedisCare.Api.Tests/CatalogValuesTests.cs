using FluentAssertions;
using SamedisCare.Api.V4.Public;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>
/// A device model carries two risk fields that are easy to mistake for one another, and both
/// export tools did. These tests hold the difference.
/// </summary>
public class MdrRiskClassTests
{
    [Theory]
    [InlineData("class_1", "I")]
    [InlineData("class_2a", "IIa")]
    [InlineData("class_2b", "IIb")]
    [InlineData("class_3", "III")]
    public void The_eu_mdr_values_map_to_the_notation_the_regulation_uses(string euMdr, string expected)
        => CatalogValues.MdrRiskClassMap(euMdr).Should().Be(expected);

    // What the tools' own tables keyed on. A source system may still deliver it this way.
    [Theory]
    [InlineData("1", "I")]
    [InlineData("2a", "IIa")]
    [InlineData("2b", "IIb")]
    [InlineData("3", "III")]
    public void The_bare_forms_are_accepted_too(string key, string expected)
        => CatalogValues.MdrRiskClassMap(key).Should().Be(expected);

    // MDR has classes I, IIa, IIb and III -- no class II. This entry exists only because the
    // tools' tables carried it and their exports show it today.
    [Fact]
    public void The_key_two_is_kept_although_MDR_has_no_class_two()
        => CatalogValues.MdrRiskClassMap("2").Should().Be("II");

    // The heart of the confusion: risk_level is the Anwendungsrisiko, unknown/0/1/2. Fed in
    // here, "1" and "2" come back looking like MDR classes and the other two vanish.
    [Theory]
    [InlineData("0")]
    [InlineData("unknown")]
    public void The_application_risk_values_that_are_not_MDR_classes_map_to_nothing(string riskLevel)
        => CatalogValues.MdrRiskClassMap(riskLevel).Should().BeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("class_4")]
    public void Anything_unrecognised_maps_to_nothing(string? value)
        => CatalogValues.MdrRiskClassMap(value).Should().BeEmpty();

    [Fact]
    public void The_two_fields_carry_disjoint_vocabularies_except_for_one_and_two()
    {
        var mdr = new[] { "class_1", "class_2a", "class_2b", "class_3" };
        var application = CatalogValues.ApplicationRisk.All;

        mdr.Should().NotIntersectWith(application,
            "which is why the mix-up is silent only for the bare forms 1 and 2");
        application.Should().Equal("unknown", "0", "1", "2");
    }
}

public class OperatorOrdinanceTests
{
    [Theory]
    [InlineData("annex_1", "1")]
    [InlineData("annex_2", "2")]
    [InlineData("annex_1_2", "1+2")]
    public void The_annex_becomes_the_number_an_operator_writes(string ordinance, string expected)
        => CatalogValues.OperatorOrdinanceMap(ordinance).Should().Be(expected);

    // "no annex" and "not recorded" look the same in an export, and neither is a number.
    [Theory]
    [InlineData("none")]
    [InlineData("annex_9")]
    [InlineData(null)]
    [InlineData("")]
    public void Everything_else_is_empty(string? ordinance)
        => CatalogValues.OperatorOrdinanceMap(ordinance).Should().BeEmpty();
}

/// <summary>
/// Whether a device needs an instruction. external-sync derived this from the
/// Betreiberverordnung annex, which answers a different question — the annex says which rules
/// a device falls under, the risk level says whether someone has to be shown how to use it.
/// </summary>
public class TrainingRequiredTests
{
    [Theory]
    [InlineData("1")]   // Anwendereinweisung notwendig
    [InlineData("2")]   // Herstellereinweisung erforderlich
    public void An_instruction_is_required_whoever_gives_it(string riskLevel)
        => CatalogValues.RequiresTraining(riskLevel).Should().BeTrue();

    [Fact]
    public void A_self_explanatory_device_needs_none()
        => CatalogValues.RequiresTraining("0").Should().BeFalse();

    // Not the same as "no training needed": the source simply does not say, and an export
    // that wrote "Nein" here would claim something nobody recorded.
    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("")]
    public void An_unrecorded_risk_level_answers_neither_way(string? riskLevel)
        => CatalogValues.RequiresTraining(riskLevel).Should().BeNull();

    // The MDR class says nothing about instruction, and feeding it in must not look like it does.
    [Theory]
    [InlineData("class_1")]
    [InlineData("class_3")]
    public void An_mdr_class_is_not_an_answer_to_this_question(string euMdr)
        => CatalogValues.RequiresTraining(euMdr).Should().BeNull();
}
