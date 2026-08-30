using System.Globalization;
using System.Web;
using Newtonsoft.Json;

namespace SamedisCare.Api.Query;

/// <summary>
/// Builds Samedis <c>gridfilter=...</c> payloads, URL-encoded and validated against what the
/// server accepts.
/// <para>
/// Shape (see the
/// <see href="https://github.com/Samedis-care/samedis-care-community/wiki/03---API-Gridfilter">wiki</see>):
/// </para>
/// <code>
/// {
///   "&lt;field&gt;": {
///     "filterType": "text" | "number" | "object_id" | "date" | "dateTime" | "bool" | "array",
///     "type":       "equals" | "greaterThan" | "contains" | ... ,
///     // one value key, decided by filterType:
///     //   text / object_id / number / bool -> "filter"   (+ "filterTo" for inRange)
///     //   date                             -> "dateFrom" (+ "dateTo")
///     //   dateTime                         -> "dateTimeFrom" (+ "dateTimeTo")
///   }
/// }
/// </code>
/// <para>
/// <c>filterType</c> is the field's data type and <c>type</c> the comparator. Swapping them
/// yields <c>{"msg":{"error":"gridfilter_error"}}</c> — an early version of this class did.
/// </para>
/// <para>
/// Not every comparator works with every type, and the server checks each condition against a
/// closed key whitelist, so a wrong combination fails the whole request rather than being
/// ignored. <see cref="Add"/> therefore validates the pair up front — see
/// <see cref="Allowed"/> for the table and where it comes from.
/// </para>
/// <example>
/// <code>
/// var b = new FilterBuilder();
/// b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.DateTime, lastRun);
/// b.Add("device_number", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, "12345");
/// var url = resource + "?gridfilter=" + b.Get();
/// </code>
/// </example>
/// </summary>
public class FilterBuilder
{
    public enum FilterType
    {
        Equals,
        NotEqual,
        Contains,
        NotContains,
        StartsWith,
        EndsWith,

        /// <summary>
        /// Case-insensitive, anchored match. Use this where source data differs from the
        /// catalog only in casing — "seca 954" against "Seca 954" — which
        /// <see cref="Equals"/> would miss because it compares case-sensitively.
        /// </summary>
        Matches,
        NotMatches,

        /// <summary>Value must be a collection; sent as a JSON array.</summary>
        InSet,

        /// <summary>Value must be a collection; sent as a JSON array.</summary>
        NotInSet,

        /// <summary>Field is null or empty. Takes no value, and works with every type.</summary>
        Empty,

        /// <summary>Field is neither null nor empty. Takes no value, and works with every type.</summary>
        NotEmpty,

        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,

        /// <summary>Requires an upper bound — pass <c>valueTo</c> to <see cref="Add"/>.</summary>
        InRange,

        /// <summary>
        /// Date-relative comparators. These belong to <see cref="Type.Bool"/>: the server
        /// implements them in its boolean criterion builder, not the date one, so pairing
        /// them with <see cref="Type.Date"/> is rejected.
        /// </summary>
        BeforeToday,
        AfterToday,
        BeforeNow,
        AfterNow
    }

    public enum Type
    {
        Text,
        Number,
        Bool,

        /// <summary>
        /// A record id. The server wants <c>object_id</c> here, not <c>text</c> — filtering
        /// an id as text does not match.
        /// </summary>
        ObjectId,

        /// <summary>A calendar day.</summary>
        Date,

        /// <summary>
        /// A point in time, as opposed to <see cref="Date"/>.
        /// <para>
        /// This distinction decides whether an incremental sync works. The server runs
        /// <c>Date.parse</c> on a <see cref="Date"/> value and compares <c>greaterThan</c>
        /// against <c>end_of_day</c> — so "changed since 2026-08-27 14:30" becomes "changed
        /// after 2026-08-27 23:59:59" and every record from that day is dropped. A
        /// <see cref="DateTime"/> value is <c>Time.parse</c>d and compared against the exact
        /// instant.
        /// </para>
        /// </summary>
        DateTime,

        /// <summary>
        /// An array field. The server supports it for emptiness only, which is why
        /// <see cref="Allowed"/> lists just <see cref="FilterType.Empty"/> and
        /// <see cref="FilterType.NotEmpty"/>.
        /// </summary>
        Array
    }

    /// <summary>
    /// Which comparators the server accepts per field type.
    /// <para>
    /// Read off <c>ApplicationDocument._gridfilter_condition_to_criterion</c> and the
    /// per-type criterion builders, not from the specs — the specs describe the parameter but
    /// not the combinations. Two consequences that are easy to trip over:
    /// <c>text</c> and <c>object_id</c> have no <c>filterTo</c> key at all, so
    /// <see cref="FilterType.InRange"/> cannot apply to them; and the date-relative
    /// comparators live in the boolean builder, not the date one.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Type, IReadOnlySet<FilterType>> Allowed { get; } =
        new Dictionary<Type, IReadOnlySet<FilterType>>
        {
            [Type.Text] = Set(FilterType.Contains, FilterType.NotContains, FilterType.StartsWith,
                              FilterType.EndsWith, FilterType.Equals, FilterType.NotEqual,
                              FilterType.Matches, FilterType.NotMatches,
                              FilterType.InSet, FilterType.NotInSet),
            [Type.ObjectId] = Set(FilterType.Equals, FilterType.NotEqual,
                                  FilterType.InSet, FilterType.NotInSet,
                                  FilterType.GreaterThan, FilterType.GreaterThanOrEqual,
                                  FilterType.LessThan, FilterType.LessThanOrEqual),
            [Type.Number] = Set(FilterType.Equals, FilterType.NotEqual,
                                FilterType.LessThan, FilterType.LessThanOrEqual,
                                FilterType.GreaterThan, FilterType.GreaterThanOrEqual,
                                FilterType.InRange, FilterType.InSet, FilterType.NotInSet),
            [Type.Date] = Set(FilterType.Equals, FilterType.NotEqual, FilterType.LessThan,
                              FilterType.GreaterThan, FilterType.InRange),
            [Type.DateTime] = Set(FilterType.Equals, FilterType.NotEqual, FilterType.LessThan,
                                  FilterType.GreaterThan, FilterType.InRange),
            [Type.Bool] = Set(FilterType.Equals, FilterType.NotEqual,
                              FilterType.BeforeToday, FilterType.AfterToday,
                              FilterType.BeforeNow, FilterType.AfterNow),
            [Type.Array] = Set(),
        };

    // Empty/NotEmpty are handled before the server dispatches on filterType and work for
    // every type, so they are added to each entry rather than repeated in the table.
    private static IReadOnlySet<FilterType> Set(params FilterType[] types)
        => new HashSet<FilterType>(types) { FilterType.Empty, FilterType.NotEmpty };

    private readonly Dictionary<string, object> _filters = new();

    public void Clear() => _filters.Clear();

    /// <summary>Adds a condition, replacing any earlier one on the same field.</summary>
    /// <param name="field">The field to filter on.</param>
    /// <param name="filterType">The comparator.</param>
    /// <param name="valueType">The field's data type, which also decides the value key.</param>
    /// <param name="value">
    /// Omit for <see cref="FilterType.Empty"/> and <see cref="FilterType.NotEmpty"/>, which
    /// compare against nothing. For <see cref="FilterType.InSet"/> and
    /// <see cref="FilterType.NotInSet"/> pass a collection — the server requires a JSON array
    /// there and rejects a comma-joined string.
    /// </param>
    /// <param name="valueTo">
    /// The upper bound, required by <see cref="FilterType.InRange"/> and rejected otherwise.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The comparator does not apply to this field type, or a range is missing its upper
    /// bound, or a set comparator was given something that is not a collection.
    /// </exception>
    public void Add(string field, FilterType filterType, Type valueType,
                    object? value = null, object? valueTo = null)
    {
        Validate(field, filterType, valueType, value, valueTo);

        var entry = new Dictionary<string, object>
        {
            ["filterType"] = MapType(valueType),    // field type, e.g. "dateTime"
            ["type"]       = MapFilter(filterType)  // comparator, e.g. "greaterThan"
        };

        // Exactly one value key may be present, and none at all for the value-less
        // comparators — the server validates each condition against a closed key whitelist.
        if (filterType is not (FilterType.Empty or FilterType.NotEmpty))
            entry[ValueKey(valueType, upper: false)] = FormatValue(value ?? string.Empty, valueType, filterType);

        if (valueTo is not null)
            entry[ValueKey(valueType, upper: true)] = FormatValue(valueTo, valueType, filterType);

        _filters[field] = entry;
    }

    private static void Validate(string field, FilterType filterType, Type valueType,
                                object? value, object? valueTo)
    {
        // Looked up before anything else so an out-of-range cast fails the same way it does
        // in MapType, rather than surfacing as a KeyNotFoundException from the table.
        if (!Allowed.TryGetValue(valueType, out var allowed))
            throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Unmapped value type");

        if (!allowed.Contains(filterType))
            throw new ArgumentException(
                $"'{MapFilter(filterType)}' does not apply to a {MapType(valueType)} field "
              + $"('{field}'). The server accepts: "
              + string.Join(", ", allowed.Select(MapFilter).Order(StringComparer.Ordinal))
              + ".", nameof(filterType));

        // The server raises "missing condition dateTo/filterTo" rather than falling back to
        // an open-ended range, so an incomplete range fails the entire request.
        if (filterType == FilterType.InRange && valueTo is null)
            throw new ArgumentException(
                $"'{field}': inRange needs an upper bound — pass valueTo.", nameof(valueTo));

        if (filterType != FilterType.InRange && valueTo is not null)
            throw new ArgumentException(
                $"'{field}': only inRange takes an upper bound, not '{MapFilter(filterType)}'.",
                nameof(valueTo));

        if (filterType is FilterType.InSet or FilterType.NotInSet
            && value is not System.Collections.IEnumerable or string)
            throw new ArgumentException(
                $"'{field}': {MapFilter(filterType)} needs a collection — the server requires a "
              + "JSON array and rejects a joined string.", nameof(value));
    }

    /// <summary>
    /// Which key carries the value. The server rejects the wrong one, and the pair differs
    /// per type: <c>dateFrom</c>/<c>dateTo</c>, <c>dateTimeFrom</c>/<c>dateTimeTo</c>,
    /// otherwise <c>filter</c>/<c>filterTo</c>.
    /// </summary>
    private static string ValueKey(Type valueType, bool upper) => (valueType, upper) switch
    {
        (Type.Date, false)     => "dateFrom",
        (Type.Date, true)      => "dateTo",
        (Type.DateTime, false) => "dateTimeFrom",
        (Type.DateTime, true)  => "dateTimeTo",
        (_, false)             => "filter",
        (_, true)              => "filterTo",
    };

    /// <summary>
    /// The payload, URL-encoded, or <c>{}</c> when nothing was added — which is what the
    /// Samedis web view sends for an empty filter.
    /// </summary>
    /// <remarks>
    /// The whole JSON is encoded here, so callers must not pre-escape values. The
    /// implementations this replaces hand-replaced <c>&amp;</c>, <c>/</c> and <c>+</c> inside
    /// the value and left the JSON itself unencoded — which silently corrupted any value
    /// containing a literal <c>%</c> and broke on <c>#</c>.
    /// </remarks>
    public string Get()
    {
        if (_filters.Count == 0) return "{}";
        return HttpUtility.UrlEncode(JsonConvert.SerializeObject(_filters));
    }

    /// <summary>The payload as readable JSON, unencoded. For logs and diagnostics.</summary>
    public override string ToString()
        => JsonConvert.SerializeObject(_filters, Formatting.Indented);

    private static string MapType(Type t) => t switch
    {
        Type.Text     => "text",
        Type.Number   => "number",
        Type.Bool     => "bool",
        Type.ObjectId => "object_id",
        Type.Date     => "date",
        Type.DateTime => "dateTime",
        Type.Array    => "array",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unmapped value type")
    };

    private static string MapFilter(FilterType f) => f switch
    {
        FilterType.Equals             => "equals",
        FilterType.NotEqual           => "notEqual",
        FilterType.Contains           => "contains",
        FilterType.NotContains        => "notContains",
        FilterType.StartsWith         => "startsWith",
        FilterType.EndsWith           => "endsWith",
        FilterType.Matches            => "matches",
        FilterType.NotMatches         => "notMatches",
        FilterType.InSet              => "inSet",
        FilterType.NotInSet           => "notInSet",
        FilterType.Empty              => "empty",
        FilterType.NotEmpty           => "notEmpty",
        FilterType.LessThan           => "lessThan",
        FilterType.LessThanOrEqual    => "lessThanOrEqual",
        FilterType.GreaterThan        => "greaterThan",
        FilterType.GreaterThanOrEqual => "greaterThanOrEqual",
        FilterType.InRange            => "inRange",
        FilterType.BeforeToday        => "beforeToday",
        FilterType.AfterToday         => "afterToday",
        FilterType.BeforeNow          => "beforeNow",
        FilterType.AfterNow           => "afterNow",
        // No silent fallback: an unmapped FilterType used to become "equals" here, which
        // produced a wrong filter instead of an error. That is exactly how the missing
        // "matches" mapping stayed hidden until a test asked for it.
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, "Unmapped filter type")
    };

    /// <summary>
    /// Formats a value for the gridfilter JSON. Dates render as <c>yyyy-MM-dd</c>; a point in
    /// time renders as UTC with milliseconds, because the server parses it as an instant.
    /// Set comparators keep their collection so it serializes as a JSON array.
    /// </summary>
    private static object FormatValue(object value, Type valueType, FilterType filterType)
    {
        if (filterType is FilterType.InSet or FilterType.NotInSet) return value;

        return valueType switch
        {
            Type.Date when value is DateTime dt         => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type.Date when value is DateTimeOffset dto  => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type.DateTime when value is DateTime dtt    => dtt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Type.DateTime when value is DateTimeOffset o => o.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Type.Number when value is IFormattable n    => n.ToString(null, CultureInfo.InvariantCulture),
            _                                           => value
        };
    }
}
