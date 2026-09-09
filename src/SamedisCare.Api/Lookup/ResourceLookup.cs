using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Query;
using SamedisCare.Api.Routing;

namespace SamedisCare.Api.Lookup;

/// <summary>
/// Finds the id of an existing record by walking a caller-defined cascade of keys, with
/// per-key caching of both hits and misses.
/// <para>
/// Every sync resolves records this way and each tool grew its own version — twelve
/// <c>Resolve*Id</c> variants in external-sync alone. The cascade differs per resource, so
/// the order is the caller's to state; what is shared is the lookup mechanics and the
/// caching.
/// </para>
/// <para>
/// <b>A hit on a stronger key is final.</b> <see cref="First"/> stops at the first step
/// that resolves and never falls through to a weaker one. That is not an optimisation:
/// external-sync documents what happens otherwise — the source may deliver a changed
/// device number for a device whose <c>external_id</c> still matches, and falling through
/// to a device-number lookup then picks a DIFFERENT record. The following update would try
/// to move the row's <c>external_id</c> onto it and be rejected by the unique index on
/// <c>(tenant_id, external_id)</c>.
/// </para>
/// </summary>
public sealed class ResourceLookup
{
    private readonly IApiClient _client;
    private readonly string _resource;
    private readonly KeyLookup _keyLookup;

    // One cache per lookup kind, keyed "kind:value". An empty value is a remembered miss,
    // so an id the source repeats on every row costs one request, not one per row.
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <param name="client">The API client.</param>
    /// <param name="resource">
    /// The collection path, e.g. the result of <c>scope.Resource("inventories")</c>.
    /// </param>
    /// <param name="keyLookup">
    /// How this backend answers a lookup by a key other than the id. Defaults to the route,
    /// which is what the tenant API offers; build the lookup through
    /// <see cref="Cascades.For"/> to take it from the scope instead of restating it here.
    /// </param>
    public ResourceLookup(IApiClient client, string resource,
                          KeyLookup keyLookup = KeyLookup.Route)
    {
        _client = client;
        _resource = resource;
        _keyLookup = keyLookup;
    }

    /// <summary>The collection path this lookup asks about.</summary>
    public string Resource => _resource;

    /// <summary>
    /// Number of remembered lookups, hits and misses together. For diagnostics.
    /// </summary>
    public int CachedKeys => _cache.Count;

    /// <summary>
    /// Walks the steps in order and returns the first id one of them resolves, or null.
    /// A step that resolves nothing is skipped; the next is tried.
    /// </summary>
    /// <remarks>
    /// Steps are passed as delegates so nothing is requested for a key the earlier steps
    /// already answered.
    /// </remarks>
    public string? First(params Func<string?>[] steps)
    {
        foreach (var step in steps)
        {
            var id = step();
            if (!string.IsNullOrWhiteSpace(id)) return id;
        }
        return null;
    }

    /// <summary>
    /// Fetches the record directly by its Samedis id.
    /// <para>
    /// Values that are not a well-formed ObjectId are rejected without a request: source
    /// data routinely carries a placeholder or free text in an id column, and asking the
    /// API about it only costs a round trip.
    /// </para>
    /// </summary>
    public string? ById(string? id)
    {
        if (!Ids.IsObjectId(id)) return null;
        var key = id!.Trim();

        return Cached(IdKey(key), () =>
        {
            var body = _client.Get($"{_resource}/{Uri.EscapeDataString(key)}");
            if (JsonApi.IsSuccess(_client.StatusCode)) return JsonApi.ExtractDataId(body) ?? key;
            GuardUnusableAnswer(body);
            return null;
        });
    }

    /// <summary>
    /// Resolves through the server's find-by-field route, <c>{resource}/via/{viaName}/{value}</c>.
    /// </summary>
    /// <param name="viaName">
    /// The field to look up by. The route is generic over the field name, but each model
    /// declares which names it accepts and rejects the rest with an error, so the name is
    /// not free: <c>external_id</c> wherever the model carries one, and additionally
    /// <c>device_number</c> for inventories, <c>employee_no</c> and <c>email</c> for staff,
    /// <c>title</c> for positions, <c>incident_number</c>, <c>briefing_number</c>,
    /// <c>contract_number</c>.
    /// </param>
    /// <param name="value">The value to look up. Null or blank resolves nothing.</param>
    /// <remarks>
    /// Two constraints that are not visible from the specs under doc/v4, which document
    /// none of this:
    /// <list type="bullet">
    /// <item>The route has to be mounted on the resource. It is not mounted everywhere —
    /// notably <b>not</b> on the sync endpoint for device models, which is why
    /// <see cref="Cascades.DeviceModel"/> resolves through regulatory identifiers instead.</item>
    /// <item>Departments carry no <c>external_id</c> at all, so no via name works there
    /// even though the route is mounted.</item>
    /// </list>
    /// The value comes from a source system and may contain characters that would break a
    /// path segment, hence the escaping.
    /// </remarks>
    public string? ByVia(string viaName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var key = value.Trim();

        return Cached(ViaKey(viaName, key), () =>
        {
            var body = _client.Get($"{_resource}/via/{viaName}/{Uri.EscapeDataString(key)}");
            if (JsonApi.IsSuccess(_client.StatusCode)) return JsonApi.FirstDataId(body);
            GuardUnusableAnswer(body);
            return null;
        });
    }

    /// <summary>
    /// Resolves by a regulatory identifier, e.g. a UDI-DI, optionally narrowed by further
    /// fields.
    /// </summary>
    /// <param name="label">
    /// One of <see cref="Regulatory.Labels"/>; anything else throws rather than reaching the
    /// server, which would answer an unknown label with an empty result set.
    /// </param>
    /// <param name="value">The identifier to match exactly. Null or blank resolves nothing.</param>
    /// <param name="query">
    /// Extra query parameters, without a leading separator. For device models this must
    /// carry <c>filter[scope]=public_and_tenant</c> — see the remarks.
    /// </param>
    /// <param name="narrow">
    /// Optional gridfilter conditions applied on top, to disambiguate an identifier that
    /// several records share.
    /// </param>
    /// <param name="comparator">The comparator for <paramref name="narrow"/>.</param>
    /// <remarks>
    /// <para>
    /// Sent as <c>filter[regulatory][{label}]</c> and matched exactly. This filter is
    /// available on both the standard and the enterprise device-model endpoints, unlike the
    /// single-value <c>filter[udi]</c> shortcut, which exists only on the standard one.
    /// </para>
    /// <para>
    /// <b>The scope is not optional.</b> The spec documents
    /// <c>filter[scope]</c> as defaulting to <c>public_and_tenant</c>, but omitting the
    /// parameter yields the tenant's own catalogs only — verified against production, where
    /// the same request returned 13 records without it and 29,942 with it. A device-model
    /// lookup that leaves it out therefore misses every public master-data record and
    /// reports "does not exist" for devices that are plainly there.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The label is not one the server accepts.</exception>
    public string? ByRegulatory(string label, string? value,
                                string? query = null,
                                IReadOnlyList<(string Field, string? Value)>? narrow = null,
                                FilterBuilder.FilterType comparator = FilterBuilder.FilterType.Matches)
    {
        Regulatory.Require(label);
        if (string.IsNullOrWhiteSpace(value)) return null;
        var key = value.Trim();

        var conditions = (narrow ?? Array.Empty<(string, string?)>())
                         .Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();

        var cacheKey = $"regulatory/{label}:{key}" + (conditions.Count == 0
            ? string.Empty
            : "|" + comparator + ":" + string.Join("|", conditions.Select(c => $"{c.Item1}={c.Item2!.Trim()}")));

        return Cached(cacheKey, () =>
        {
            var url = $"{_resource}?page[number]=1&page[limit]=1"
                    + (string.IsNullOrEmpty(query) ? "" : "&" + query)
                    + $"&filter[regulatory][{label}]={Uri.EscapeDataString(key)}";

            if (conditions.Count > 0)
            {
                var filter = new FilterBuilder();
                foreach (var (field, v) in conditions)
                    filter.Add(field, comparator, FilterBuilder.Type.Text, v!.Trim());
                url += "&gridfilter=" + filter.Get();
            }

            return FindFirst(url, cacheKey);
        });
    }

    /// <summary>
    /// Resolves by a gridfilter whose conditions each carry their own comparator and field
    /// type, taking the first match.
    /// </summary>
    /// <param name="conditions">
    /// The conditions, combined with AND. All must match.
    /// </param>
    /// <param name="query">
    /// Extra query parameters, without a leading separator, e.g.
    /// <c>filter[scope]=public_and_tenant</c>.
    /// </param>
    /// <remarks>
    /// The general form. <see cref="ByFields"/> is the common case where every condition is a
    /// text comparison; this one is for the mixed sets, such as "belongs to this tenant AND
    /// has no parent" — where one condition compares a value and the other asserts absence.
    /// </remarks>
    public string? ByConditions(IReadOnlyList<Condition> conditions, string? query = null)
    {
        var usable = conditions.Where(c => c.TakesNoValue || !IsBlank(c.Value)).ToList();
        if (usable.Count == 0) return null;

        var filter = new FilterBuilder();
        foreach (var c in usable)
            filter.Add(c.Field, c.Comparator, c.Type, c.TakesNoValue ? null : Normalize(c.Value));

        var cacheKey = "conditions:" + (query ?? string.Empty) + ":" +
                       string.Join("|", usable.Select(c =>
                           $"{c.Field}/{c.Comparator}/{c.Type}={(c.TakesNoValue ? string.Empty : Normalize(c.Value))}"));

        return Cached(cacheKey, () =>
        {
            var url = $"{_resource}?page[number]=1&page[limit]=1"
                    + (string.IsNullOrEmpty(query) ? "" : "&" + query)
                    + "&gridfilter=" + filter.Get();

            return FindFirst(url, cacheKey);
        });
    }

    private static bool IsBlank(object? value)
        => value is null || (value is string s && string.IsNullOrWhiteSpace(s));

    private static object? Normalize(object? value)
        => value is string s ? s.Trim() : value;

    /// <summary>
    /// Resolves by a gridfilter over one or more fields, taking the first match.
    /// </summary>
    /// <param name="conditions">
    /// Field/value pairs, combined as one filter. All must match.
    /// </param>
    /// <param name="comparator">
    /// <see cref="FilterBuilder.FilterType.Equals"/> compares case-sensitively;
    /// <see cref="FilterBuilder.FilterType.Matches"/> does not. The tools disagree here —
    /// external-sync matches device models case-sensitively and so misses "seca 954"
    /// against a catalog entry "Seca 954", while sync-trainings does not — so the choice is
    /// explicit rather than defaulted to whatever one caller happened to use.
    /// </param>
    /// <param name="query">
    /// Extra query parameters, without a leading separator, e.g.
    /// <c>filter[scope]=public_and_tenant</c>.
    /// </param>
    public string? ByFields(IReadOnlyList<(string Field, string? Value)> conditions,
                            FilterBuilder.FilterType comparator = FilterBuilder.FilterType.Equals,
                            string? query = null)
    {
        var usable = conditions.Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
        if (usable.Count == 0) return null;

        var filter = new FilterBuilder();
        foreach (var (field, value) in usable)
            filter.Add(field, comparator, FilterBuilder.Type.Text, value!.Trim());

        var cacheKey = FieldsKey(usable, comparator, query);

        return Cached(cacheKey, () =>
        {
            var url = $"{_resource}?page[number]=1&page[limit]=1"
                    + (string.IsNullOrEmpty(query) ? "" : "&" + query)
                    + "&gridfilter=" + filter.Get();

            return FindFirst(url, cacheKey);
        });
    }

    /// <summary>
    /// Resolves by a field the backend treats as a key: <c>external_id</c>, an employee
    /// number, an incident number. Uses whichever mechanism this backend offers.
    /// </summary>
    /// <param name="field">
    /// The key field. Which names work is the model's decision, not the route's -- see
    /// <see cref="ByVia"/> for the list.
    /// </param>
    /// <param name="value">The value to look up. Null or blank resolves nothing.</param>
    /// <param name="query">
    /// Extra query parameters for the filter mechanism, without a leading separator. Ignored
    /// under <see cref="KeyLookup.Route"/>, which takes no query.
    /// </param>
    /// <remarks>
    /// This is what the cascades call, so that moving a sync between the tenant and the
    /// enterprise API changes the scope and nothing else. Calling <see cref="ByVia"/>
    /// directly stays correct where the caller knows the route exists.
    /// </remarks>
    public string? ByUniqueField(string field, string? value, string? query = null)
        => _keyLookup == KeyLookup.Route
            ? ByVia(field, value)
            : ByField(field, value, FilterBuilder.FilterType.Equals, query);

    /// <summary>
    /// Seeds <see cref="ByUniqueField"/> under whichever key that method would look up by.
    /// </summary>
    /// <remarks>
    /// Has to dispatch the same way, or a run seeds one cache and asks the other -- a cache
    /// that never hits and never says so.
    /// </remarks>
    public void RememberUniqueField(string field, string? value, string? id, string? query = null)
    {
        if (_keyLookup == KeyLookup.Route) RememberVia(field, value, id);
        else RememberField(field, value, id, FilterBuilder.FilterType.Equals, query);
    }

    /// <summary>Single-field convenience over <see cref="ByFields"/>.</summary>
    public string? ByField(string field, string? value,
                           FilterBuilder.FilterType comparator = FilterBuilder.FilterType.Equals,
                           string? query = null)
        => ByFields(new[] { (field, value) }, comparator, query);

    /// <summary>
    /// Records an id the caller already knows, so a later lookup for the same key answers
    /// from memory instead of asking.
    /// </summary>
    /// <remarks>
    /// The case this exists for: a sync creates a record and the next source row refers to
    /// the same device. Without seeding, that row asks the server for something this run just
    /// wrote. Seeding through these methods rather than a raw dictionary keeps the key
    /// derivation in one place — a hand-built key that does not match what
    /// <see cref="ById"/> or <see cref="ByVia"/> compute is a cache that silently never hits.
    /// </remarks>
    /// <param name="id">The id that was looked up. Ignored unless it is a well-formed ObjectId.</param>
    /// <param name="resolvedId">
    /// What that id resolves to. Null means it resolves to itself, which is the usual case;
    /// pass a value where the source's id is an alias for a different record.
    /// </param>
    public void RememberId(string? id, string? resolvedId = null)
    {
        if (!Ids.IsObjectId(id)) return;

        var target = string.IsNullOrWhiteSpace(resolvedId) ? id!.Trim() : resolvedId.Trim();
        _cache[IdKey(id!.Trim())] = target;
    }

    /// <inheritdoc cref="RememberId"/>
    public void RememberVia(string viaName, string? value, string? id)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(id))
            _cache[ViaKey(viaName, value.Trim())] = id.Trim();
    }

    /// <inheritdoc cref="RememberId"/>
    public void RememberFields(IReadOnlyList<(string Field, string? Value)> conditions, string? id,
                               FilterBuilder.FilterType comparator = FilterBuilder.FilterType.Equals,
                               string? query = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var usable = conditions.Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
        // Seeding a subset of the conditions would answer a narrower question with a broader
        // record, so a partial set is not remembered at all.
        if (usable.Count == 0 || usable.Count != conditions.Count) return;

        _cache[FieldsKey(usable, comparator, query)] = id.Trim();
    }

    /// <summary>Single-field convenience over <see cref="RememberFields"/>.</summary>
    public void RememberField(string field, string? value, string? id,
                              FilterBuilder.FilterType comparator = FilterBuilder.FilterType.Equals,
                              string? query = null)
        => RememberFields(new[] { (field, value) }, id, comparator, query);

    private static string IdKey(string id) => $"id:{id}";

    private static string ViaKey(string viaName, string value) => $"via/{viaName}:{value}";

    /// <summary>
    /// The cache key for a field lookup. The extra query is part of it: the same field and
    /// value under <c>filter[scope]=tenant</c> and under
    /// <c>filter[scope]=public_and_tenant</c> are two different questions, and sharing a key
    /// would answer one with the other's record.
    /// </summary>
    private static string FieldsKey(IReadOnlyList<(string Field, string? Value)> conditions,
                                    FilterBuilder.FilterType comparator, string? query = null)
        => "fields:" + comparator + ":" + (query ?? string.Empty) + ":" +
           string.Join("|", conditions.Select(c => $"{c.Field}={c.Value!.Trim()}"));

    /// <summary>
    /// Drops everything remembered. Caches are per-run by design; call this only if the
    /// records may have changed underneath.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _ambiguous.Clear();
    }

    /// <summary>
    /// Lookups that matched more than one record, by cache key. The first match was taken —
    /// a sync has to make progress — but the caller should log these: production has device
    /// models sharing a UDI-DI, including two different models ("Perfusor Space" and
    /// "Perfusor Space PCA") under one <c>eudamed_id</c>, so an unnarrowed identifier can
    /// attach a device to the wrong model.
    /// </summary>
    public IReadOnlyCollection<string> AmbiguousMatches => _ambiguous;

    private readonly List<string> _ambiguous = new();

    /// <summary>
    /// Requests a single-record page and returns the first id, noting when the server says
    /// more than one record matched.
    /// </summary>
    private string? FindFirst(string url, string cacheKey)
    {
        var body = _client.Get(url);
        if (!JsonApi.IsSuccess(_client.StatusCode))
        {
            GuardUnusableAnswer(body);
            return null;
        }

        var id = JsonApi.FirstDataId(body);
        if (id is not null && JsonApi.Total(body) > 1 && !_ambiguous.Contains(cacheKey))
            _ambiguous.Add(cacheKey);

        return id;
    }

    /// <summary>
    /// Applies <see cref="LookupUnavailableException.ThrowUnlessAbsent"/> to this lookup's
    /// resource and the client's last status.
    /// </summary>
    private void GuardUnusableAnswer(string? body)
        => LookupUnavailableException.ThrowUnlessAbsent(_resource, _client.StatusCode, body);

    private string? Cached(string key, Func<string?> lookup)
    {
        if (_cache.TryGetValue(key, out var remembered))
            return string.IsNullOrEmpty(remembered) ? null : remembered;

        var resolved = lookup();
        // Misses are remembered as an empty string. Without that, an unknown id repeated
        // across a thousand source rows costs a thousand requests.
        _cache[key] = resolved ?? string.Empty;
        return resolved;
    }
}

/// <summary>
/// One gridfilter condition: which field, how to compare, and what the field's type is.
/// </summary>
/// <param name="Field">The field to filter on.</param>
/// <param name="Comparator">How to compare.</param>
/// <param name="Type">The field's data type, which decides the value key the server expects.</param>
/// <param name="Value">
/// The value, or null for the comparators that take none
/// (<see cref="FilterBuilder.FilterType.Empty"/> and <see cref="FilterBuilder.FilterType.NotEmpty"/>).
/// </param>
public readonly record struct Condition(
    string Field,
    FilterBuilder.FilterType Comparator,
    FilterBuilder.Type Type,
    object? Value = null)
{
    /// <summary>Whether this comparator compares against nothing.</summary>
    public bool TakesNoValue
        => Comparator is FilterBuilder.FilterType.Empty or FilterBuilder.FilterType.NotEmpty;

    /// <summary>A text equality condition, the common case.</summary>
    public static Condition Text(string field, string? value)
        => new(field, FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, value);

    /// <summary>An id equality condition.</summary>
    public static Condition Id(string field, string? value)
        => new(field, FilterBuilder.FilterType.Equals, FilterBuilder.Type.ObjectId, value);

    /// <summary>Asserts that a field carries no value.</summary>
    public static Condition Empty(string field, FilterBuilder.Type type)
        => new(field, FilterBuilder.FilterType.Empty, type);
}
