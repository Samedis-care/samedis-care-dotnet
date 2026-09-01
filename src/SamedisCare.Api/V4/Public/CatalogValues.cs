namespace SamedisCare.Api.V4.Public;

/// <summary>
/// Writing a device model's coded fields the way a person reads them.
/// </summary>
/// <remarks>
/// Every tool that exports device data grew its own copy of these tables, and both copies
/// were fed the wrong field — see the remarks on <see cref="MdrRiskClassMap"/>.
/// </remarks>
public static class CatalogValues
{
    /// <summary>
    /// The MDR risk class in the notation the regulation uses: <c>I</c>, <c>IIa</c>,
    /// <c>IIb</c>, <c>III</c>. Empty for anything it does not recognise.
    /// </summary>
    /// <param name="euMdr">
    /// The value of <c>regulatory.eu_mdr</c>: <c>class_1</c>, <c>class_2a</c>,
    /// <c>class_2b</c> or <c>class_3</c>. The bare forms (<c>1</c>, <c>2a</c>, …) are
    /// accepted too, because that is what the tools' own copies keyed on and a source
    /// system may deliver them that way.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>This is not <c>risk_level</c>.</b> A device model carries two different risk
    /// fields and they are easy to confuse:
    /// </para>
    /// <list type="table">
    /// <item>
    ///   <term><c>regulatory.eu_mdr</c></term>
    ///   <description>the MDR risk class — <c>class_1 | class_2a | class_2b | class_3</c>.
    ///   What this method is for.</description>
    /// </item>
    /// <item>
    ///   <term><c>risk_level</c></term>
    ///   <description>the Anwendungsrisiko, i.e. how much instruction a user needs —
    ///   <c>unknown | 0 | 1 | 2</c>, where 0 is self-explanatory, 1 needs a user
    ///   instruction and 2 needs the manufacturer's. Nothing to do with MDR.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Both tools fed <c>risk_level</c> into this table. The overlap is what makes it
    /// quiet: <c>"1"</c> comes back as <c>"I"</c> and <c>"2"</c> as <c>"II"</c>, so a device
    /// that merely needs a user instruction is exported as MDR class I, and one needing the
    /// manufacturer's as class II. <c>"0"</c> and <c>"unknown"</c> are dropped entirely, and
    /// <c>2a</c>, <c>2b</c>, <c>3</c> can never occur.
    /// </para>
    /// <para>
    /// <c>2</c> is kept as a key even though MDR has no class II — the classes are I, IIa,
    /// IIb and III. It exists because the tools' tables carried it and their exports show it
    /// today; removing it would change a file someone downstream already reads. It is the
    /// clearest evidence that the table was never written for <c>eu_mdr</c>.
    /// </para>
    /// </remarks>
    public static string MdrRiskClassMap(string? euMdr)
        => euMdr?.Trim() switch
        {
            "class_1" or "1" => "I",
            "class_2a" or "2a" => "IIa",
            "class_2b" or "2b" => "IIb",
            "class_3" or "3" => "III",

            // Not an MDR class. Kept because the tools' own tables had it and their exports
            // carry it today; see the remarks.
            "2" => "II",

            _ => string.Empty,
        };

    /// <summary>
    /// The annex of the German Medizinprodukte-Betreiberverordnung a device falls under,
    /// as the number an operator writes: <c>1</c>, <c>2</c> or <c>1+2</c>.
    /// </summary>
    /// <param name="operatorOrdinance">
    /// The value of <c>operator_ordinance</c>: <c>annex_1</c>, <c>annex_2</c>,
    /// <c>annex_1_2</c> or <c>none</c>.
    /// </param>
    /// <remarks>
    /// <c>none</c> and an unknown value both come back empty, which is deliberate: a device
    /// that falls under no annex and one whose annex is not recorded look the same in an
    /// export, and neither is a number.
    /// </remarks>
    public static string OperatorOrdinanceMap(string? operatorOrdinance)
        => operatorOrdinance?.Trim() switch
        {
            "annex_1" => "1",
            "annex_2" => "2",
            "annex_1_2" => "1+2",
            _ => string.Empty,
        };

    /// <summary>
    /// Whether a device needs an instruction before it may be used, from its
    /// <c>risk_level</c>. Null when the source does not say.
    /// </summary>
    /// <param name="riskLevel">
    /// The Anwendungsrisiko: <c>unknown</c>, <c>0</c>, <c>1</c> or <c>2</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Both <c>1</c> ("Anwendereinweisung notwendig") and <c>2</c> ("Herstellereinweisung
    /// erforderlich") mean an instruction is required, only by different people. <c>0</c>
    /// ("Gerät selbsterklärend") means none is. The wording is the app's own, from
    /// <c>config/locales/de.yml</c> under <c>catalogs.risk_level</c>.
    /// </para>
    /// <para>
    /// Returns the fact, not a word for it: whether an export writes "Ja", "yes" or "1" is
    /// the receiving system's business.
    /// </para>
    /// <para>
    /// <b>Not from <c>operator_ordinance</c>.</b> external-sync derived this from the
    /// Betreiberverordnung annex, which is a related but different question -- the annex says
    /// which rules a device falls under, the risk level says whether someone has to be shown
    /// how to use it. The German label for risk level 2 mentions "MP Anlage 1", which is
    /// probably how the two got tied together.
    /// </para>
    /// </remarks>
    public static bool? RequiresTraining(string? riskLevel)
        => riskLevel?.Trim() switch
        {
            ApplicationRisk.UserInstruction or ApplicationRisk.ManufacturerInstruction => true,
            ApplicationRisk.SelfExplanatory => false,
            _ => null,
        };

    /// <summary>
    /// The values <c>risk_level</c> takes, so a caller can tell the Anwendungsrisiko from
    /// the MDR class without repeating the list.
    /// </summary>
    /// <remarks>
    /// Deliberately no display mapping: what an export should say for "needs a manufacturer
    /// instruction" is the receiving system's question, and inventing wording here would be
    /// the same mistake as running these values through
    /// <see cref="MdrRiskClassMap"/>.
    /// </remarks>
    public static class ApplicationRisk
    {
        public const string Unknown = "unknown";

        /// <summary>Self-explanatory; no instruction needed.</summary>
        public const string SelfExplanatory = "0";

        /// <summary>A user instruction is required.</summary>
        public const string UserInstruction = "1";

        /// <summary>An instruction by the manufacturer is required.</summary>
        public const string ManufacturerInstruction = "2";

        public static readonly IReadOnlyList<string> All =
            new[] { Unknown, SelfExplanatory, UserInstruction, ManufacturerInstruction };
    }
}
