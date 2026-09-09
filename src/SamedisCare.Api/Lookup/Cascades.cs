using SamedisCare.Api.Http;
using SamedisCare.Api.Query;
using SamedisCare.Api.Routing;

namespace SamedisCare.Api.Lookup;

/// <summary>
/// The resolution orders the syncs use, written down once.
/// <para>
/// Each cascade goes from the strongest key to the weakest and stops at the first hit —
/// see the note on <see cref="ResourceLookup"/> for why falling through after a match on
/// id or external_id corrupts the following update.
/// </para>
/// </summary>
public static class Cascades
{
    /// <summary>
    /// Device models are largely public master data, so a lookup limited to the facility's own
    /// records would miss almost everything. Omitting the parameter is not equivalent: the
    /// server then answers with the facility's own records only, whatever the spec's documented
    /// default says.
    /// </summary>
    private const string BothScopes = "filter[scope]=public_and_tenant";

    /// <summary>
    /// Resolves an inventory: <c>id</c>, then <c>external_id</c>, then <c>device_number</c>.
    /// </summary>
    /// <param name="lookup">The lookup bound to the inventories collection.</param>
    /// <param name="id">A Samedis inventory id, if the source carries one.</param>
    /// <param name="externalId">The source system's own key for the device.</param>
    /// <param name="deviceNumber">The facility's inventory number.</param>
    /// <param name="query">
    /// Extra query parameters for the device-number step, without a leading separator, e.g.
    /// <c>variant=regular</c> to ask for the smaller serializer variant.
    /// </param>
    /// <param name="deviceNumberFallback">
    /// Whether the device number may be used at all. Off for callers that must not match a
    /// record they were not given a stable key for.
    /// </param>
    /// <remarks>
    /// The device number is the weakest key: the source may change it for a device whose
    /// external_id still points at the same record, so it is only consulted when neither
    /// stronger key resolved.
    /// </remarks>
    public static string? Inventory(ResourceLookup lookup,
                                    string? id,
                                    string? externalId,
                                    string? deviceNumber,
                                    string? query = null,
                                    bool deviceNumberFallback = true)
        => lookup.First(
            () => lookup.ById(id),
            () => lookup.ByUniqueField("external_id", externalId),
            () => deviceNumberFallback
                ? lookup.ByField("device_number", deviceNumber, FilterBuilder.FilterType.Equals, query)
                : null);

    /// <summary>
    /// Resolves a device model (catalog): <c>id</c>, then <c>external_id</c>, then the
    /// regulatory identifiers in the order given, then title plus manufacturer.
    /// </summary>
    /// <param name="lookup">The lookup bound to the device-models collection.</param>
    /// <param name="catalogId">A Samedis catalog id, if the source carries one.</param>
    /// <param name="title">The device model's title.</param>
    /// <param name="manufacturer">The manufacturer, tried against both manufacturer fields.</param>
    /// <param name="regulatory">
    /// Regulatory label/value pairs to try, strongest first — build it with
    /// <see cref="Regulatory.Identifiers"/>. Only labels from
    /// <see cref="Regulatory.DeviceIdentifiers"/> belong here; a nomenclature code
    /// classifies a device rather than identifying it and would match a wrong record.
    /// </param>
    /// <param name="externalId">
    /// The tenant's own key, matched with a gridfilter. Only ever resolves a model the
    /// tenant created itself — see the remarks.
    /// </param>
    /// <param name="caseInsensitiveTitleMatch">
    /// Whether the title/manufacturer step compares case-insensitively. Source data and
    /// catalog entries routinely differ only in casing ("seca 954" against "Seca 954"), so
    /// this defaults to on — external-sync compares case-sensitively today and misses
    /// exactly those.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>external_id works here, but not through the via route.</b> The field exists on the
    /// model and is writable on the sync endpoint, and because the gridfilter whitelist is
    /// simply every field of the document, it can be filtered on. What is missing is
    /// <c>via/external_id</c>: that route is mounted only on the MDM endpoint for device
    /// models. So this step uses a gridfilter, unlike every other cascade here.
    /// </para>
    /// <para>
    /// It is also the weaker key for this resource. Device models are largely public master
    /// data shared across facilities, and the unique index is on
    /// <c>(tenant_id, external_id)</c> — a value the tenant did not set itself resolves
    /// nothing. The regulatory identifiers are what travels with the device: they are
    /// assigned externally and hold across facilities.
    /// </para>
    /// <para>
    /// Regulatory matching goes through <c>filter[regulatory][label]</c>, which both the
    /// standard and the enterprise endpoint accept. The single-value <c>filter[udi]</c>
    /// shortcut is deliberately unused: it is absent from the enterprise spec, so a cascade
    /// built on it would resolve nothing in enterprise mode and silently create duplicates.
    /// </para>
    /// <para>
    /// The manufacturer is tried against two fields because source systems use them
    /// interchangeably: the type-plate manufacturer first, then the currently responsible
    /// one. Every gridfilter step is scoped to the tenant's models and the public catalog,
    /// since a source may reference either.
    /// </para>
    /// </remarks>
    public static string? DeviceModel(ResourceLookup lookup,
                                      string? catalogId,
                                      string? title,
                                      string? manufacturer,
                                      IReadOnlyList<(string Label, string? Value)>? regulatory = null,
                                      string? externalId = null,
                                      bool caseInsensitiveTitleMatch = true)
    {
        var comparator = caseInsensitiveTitleMatch
            ? FilterBuilder.FilterType.Matches
            : FilterBuilder.FilterType.Equals;

        var steps = new List<Func<string?>>
        {
            () => lookup.ById(catalogId),
            () => lookup.ByField("external_id", externalId, FilterBuilder.FilterType.Equals, BothScopes),
        };

        foreach (var (label, value) in regulatory ?? Array.Empty<(string, string?)>())
        {
            // Captured per iteration on purpose — the delegates run later, in First.
            var l = label;
            var v = value;

            // Narrowed by the title first, because a regulatory identifier is not unique in
            // production: one eudamed_id covers both "Perfusor Space" and "Perfusor Space
            // PCA". Taking the first of those would attach the device to the wrong model.
            if (!string.IsNullOrWhiteSpace(title))
                steps.Add(() => lookup.ByRegulatory(l, v, BothScopes,
                                                    new (string, string?)[] { ("title", title) },
                                                    comparator));

            // Then unnarrowed: the source title may differ from the catalog's wording, and a
            // matching identifier is still far better evidence than a title guess. Where
            // several records remain, the first is taken and the key is recorded in
            // ResourceLookup.AmbiguousMatches for the caller to log.
            steps.Add(() => lookup.ByRegulatory(l, v, BothScopes));
        }

        // Both steps require a title. A manufacturer on its own is not an identifier -- it
        // would match an arbitrary model from that maker -- and ByFields silently drops a
        // blank condition, so without this guard a row with no title but a manufacturer
        // resolves to whichever of that maker's models happens to come first.
        if (!string.IsNullOrWhiteSpace(title))
        {
            steps.Add(() => string.IsNullOrWhiteSpace(manufacturer)
                ? lookup.ByField("title", title, comparator, BothScopes)
                : lookup.ByFields(new (string, string?)[] { ("title", title), ("manufacturer_according_to_type_plate", manufacturer) },
                                  comparator, BothScopes));
            steps.Add(() => string.IsNullOrWhiteSpace(manufacturer)
                ? null
                : lookup.ByFields(new (string, string?)[] { ("title", title), ("current_responsible_manufacturer", manufacturer) },
                                  comparator, BothScopes));
        }

        return lookup.First(steps.ToArray());
    }

    /// <summary>
    /// Records a device model under the keys <see cref="DeviceModel"/> would look it up by,
    /// so a later row naming the same model is answered from memory.
    /// </summary>
    /// <param name="lookup">The lookup bound to the device-models collection.</param>
    /// <param name="title">The model's title.</param>
    /// <param name="manufacturer">The manufacturer, as the type plate names it.</param>
    /// <param name="id">The model's id.</param>
    /// <param name="caseInsensitiveTitleMatch">
    /// Must match what <see cref="DeviceModel"/> is called with, or the seeded entry answers a
    /// different question than the one that gets asked.
    /// </param>
    /// <remarks>
    /// Lives here rather than at the call site because the conditions and the scope are built
    /// inside <see cref="DeviceModel"/>: a seed assembled separately would drift from them the
    /// first time either changes, leaving a cache that silently never hits.
    /// </remarks>
    public static void RememberDeviceModel(ResourceLookup lookup, string? title, string? manufacturer,
                                           string? id, bool caseInsensitiveTitleMatch = true)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(id)) return;

        var comparator = caseInsensitiveTitleMatch
            ? FilterBuilder.FilterType.Matches
            : FilterBuilder.FilterType.Equals;

        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            lookup.RememberField("title", title, id, comparator, BothScopes);
            return;
        }

        lookup.RememberFields(
            new (string, string?)[] { ("title", title), ("manufacturer_according_to_type_plate", manufacturer) },
            id, comparator, BothScopes);
    }

    /// <summary>
    /// Resolves a member of staff: <c>id</c>, then <c>external_id</c>, then the employee
    /// number, then optionally the e-mail address.
    /// </summary>
    /// <param name="lookup">The lookup bound to the staffs collection.</param>
    /// <param name="id">A Samedis staff id, if the source carries one.</param>
    /// <param name="externalId">The source system's own key for the person.</param>
    /// <param name="employeeNo">The facility's personnel number.</param>
    /// <param name="email">
    /// The e-mail address, tried last and only when given. Off by default because it is the
    /// one key here that is not necessarily a person: source systems put shared mailboxes
    /// ("mt@...") on several records, and a match then attaches a training to whoever of
    /// them the server returns first.
    /// </param>
    /// <remarks>
    /// <c>staffs/via/employee_no/{no}</c> appears in none of the specs under doc/v4, but it
    /// is what the syncs have always used and it answers.
    /// </remarks>
    public static string? Staff(ResourceLookup lookup,
                                string? id,
                                string? externalId,
                                string? employeeNo,
                                string? email = null)
        => lookup.First(
            () => lookup.ById(id),
            () => lookup.ByUniqueField("external_id", externalId),
            () => lookup.ByUniqueField("employee_no", employeeNo),
            () => lookup.ByUniqueField("email", email));

    /// <summary>
    /// Resolves a record that is identified by <c>id</c>, <c>external_id</c> or a single
    /// title-like field — the shape most of the master-data resources use (departments,
    /// positions, buildings, floors, locations, properties, profit centres, device types).
    /// </summary>
    public static string? ByTitle(ResourceLookup lookup,
                                  string? id,
                                  string? externalId,
                                  string? title,
                                  string titleField = "title",
                                  bool caseInsensitiveTitleMatch = false)
        => lookup.First(
            () => lookup.ById(id),
            () => lookup.ByUniqueField("external_id", externalId),
            () => lookup.ByField(titleField, title,
                                 caseInsensitiveTitleMatch
                                     ? FilterBuilder.FilterType.Matches
                                     : FilterBuilder.FilterType.Equals));

    /// <summary>
    /// Convenience: builds a <see cref="ResourceLookup"/> for a resource under a scope.
    /// </summary>
    /// <remarks>
    /// Takes the key-lookup mechanism from the scope, so a sync moved to the enterprise API
    /// keeps working without touching a cascade.
    /// </remarks>
    public static ResourceLookup For(IApiClient client, ITenantScope scope, string resource)
        => new(client, scope.Resource(resource), scope.KeyLookup);
}
