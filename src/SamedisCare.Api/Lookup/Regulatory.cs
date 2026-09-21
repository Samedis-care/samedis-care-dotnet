namespace SamedisCare.Api.Lookup;

/// <summary>
/// The regulatory labels a device model may carry, grouped by what they are good for as a
/// lookup key.
/// <para>
/// A device model stores these in a <c>regulatory</c> hash, but the server accepts only the
/// labels listed in <see cref="Labels"/>: <c>with_regulatory</c> slices the filter against
/// them and then <b>returns no records at all</b> when nothing valid is left. An unknown or
/// misspelled label therefore does not fail loudly; it looks exactly like "this device does
/// not exist yet", which is the one answer that makes a sync create a duplicate. Hence the
/// client-side check in <see cref="Require"/>.
/// </para>
/// </summary>
public static class Regulatory
{
    /// <summary>
    /// Identifiers assigned to one device model or one device family. These are the labels
    /// worth putting in a find-or-create cascade.
    /// <para>
    /// Ordered by how narrowly they identify: <c>udi_id</c> and <c>eudamed_id</c> are UDI-DIs
    /// and name a specific model; <c>emtec_id</c> and <c>emtec_code</c> name a specific
    /// entry in the emtec catalogue; <c>eudamed_di</c> is a Basic UDI-DI and covers a whole
    /// device family, so it may legitimately match several models — put it last, or leave it
    /// out where exactly one record must come back.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> DeviceIdentifiers =
        new[] { "udi_id", "eudamed_id", "emtec_id", "emtec_code", "eudamed_di" };

    /// <summary>
    /// Nomenclature codes. They classify a device rather than identify it — many models
    /// share one code — so a cascade must not treat a match as "this is the record".
    /// Filtering by them is still useful for narrowing a set.
    /// </summary>
    public static readonly IReadOnlyList<string> NomenclatureCodes =
        new[] { "emdn_code", "umdns_code", "gmdn_code" };

    /// <summary>
    /// Risk classes and the CE marking: not identifiers at all. <c>ecri_risk_level</c>,
    /// <c>us_fda</c> and <c>eu_mdr</c> are enumerations in the spec; <c>ce</c> is free text
    /// but names a certificate, not a device.
    /// </summary>
    public static readonly IReadOnlyList<string> Classifications =
        new[] { "ce", "ecri_risk_level", "us_fda", "eu_mdr" };

    /// <summary>Every label the server accepts.</summary>
    public static readonly IReadOnlySet<string> Labels =
        new HashSet<string>(DeviceIdentifiers.Concat(NomenclatureCodes).Concat(Classifications),
                            StringComparer.Ordinal);

    /// <summary>
    /// The labels a caller may configure as a lookup key when resolving a device model,
    /// in the order they are worth trying: <see cref="DeviceIdentifiers"/> first, then
    /// <c>emdn_code</c> and <c>gmdn_code</c>.
    /// <para>
    /// Narrower than <see cref="Labels"/> on purpose. The server also accepts <c>ce</c> and
    /// the risk classes, but those describe a device rather than name one: a filter on
    /// <c>eu_mdr</c> matches thousands of models, and a cascade taking the first of them
    /// attaches the device to an arbitrary record. That failure is silent — an id comes
    /// back, it is simply the wrong one — so the check belongs in front of the request,
    /// not in a reviewer's head.
    /// </para>
    /// <para>
    /// The two nomenclature codes are in despite what <see cref="NomenclatureCodes"/> says
    /// about them, because <see cref="Cascades.DeviceModel"/> asks every identifier narrowed
    /// by the title first and records a leftover ambiguity in
    /// <see cref="ResourceLookup.AmbiguousMatches"/>; a shared code plus a matching title is
    /// still evidence. <c>umdns_code</c> stays out: it is the coarsest of the three and
    /// nothing has asked for it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> LookupKeys =
        DeviceIdentifiers.Concat(new[] { "emdn_code", "gmdn_code" }).ToList();

    /// <summary>
    /// Pairs each of <see cref="DeviceIdentifiers"/> with a value from the caller, dropping
    /// the ones with nothing to search for, in the order the identifiers are declared.
    /// A convenience for building the <c>regulatory</c> argument of
    /// <see cref="Cascades.DeviceModel"/> from a row that may carry any subset.
    /// </summary>
    public static IReadOnlyList<(string Label, string? Value)> Identifiers(
        string? udiId = null,
        string? eudamedId = null,
        string? emtecId = null,
        string? emtecCode = null,
        string? eudamedDi = null)
    {
        var byLabel = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["udi_id"] = udiId,
            ["eudamed_id"] = eudamedId,
            ["emtec_id"] = emtecId,
            ["emtec_code"] = emtecCode,
            ["eudamed_di"] = eudamedDi,
        };

        return DeviceIdentifiers
            .Where(l => !string.IsNullOrWhiteSpace(byLabel[l]))
            .Select(l => (l, byLabel[l]))
            .ToList();
    }

    /// <summary>
    /// Returns the label unchanged, or throws if the server would not accept it.
    /// </summary>
    /// <param name="label">The regulatory label to check.</param>
    /// <exception cref="ArgumentException">The label is not one the server knows.</exception>
    public static string Require(string label)
        => Labels.Contains(label)
            ? label
            : throw new ArgumentException(
                $"'{label}' is not a regulatory label the server accepts. Valid: {string.Join(", ", Labels.Order(StringComparer.Ordinal))}.",
                nameof(label));

    /// <summary>
    /// Returns the label unchanged, or throws if it is not one of <see cref="LookupKeys"/>.
    /// The check for a label a caller configured as a lookup key — stricter than
    /// <see cref="Require"/>, which only asks whether the server would accept it.
    /// </summary>
    /// <param name="label">The regulatory label to check.</param>
    /// <exception cref="ArgumentException">The label does not identify a device model.</exception>
    public static string RequireLookupKey(string label)
        => LookupKeys.Contains(label)
            ? label
            : throw new ArgumentException(
                $"'{label}' does not identify a device model and cannot be used as a lookup key. Valid: {string.Join(", ", LookupKeys)}.",
                nameof(label));
}
