using System.Globalization;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace SamedisCare.Api.Query;

/// <summary>
/// Builds Samedis-style `gridfilter=...` JSON payloads, URL-encoded.
///
/// API schema per public.yaml and the wiki documentation
/// (https://github.com/Samedis-care/samedis-care-community/wiki/03---API-Gridfilter):
///
///   {
///     "&lt;field&gt;": {
///       "filterType": "date" | "number" | "text" | "bool" | "set",   // field data type
///       "type":       "equals" | "greaterThan" | "contains" | ... ,  // comparison operator
///       // values depend on filterType:
///       //   date          -> "dateFrom" (and optionally "dateTo" for inRange)
///       //   number/text/  -> "filter"   (and optionally "filterTo" for inRange)
///       //     bool/set
///     }
///   }
///
/// Important: `filterType` and `type` were swapped in an early version of this
/// class; the API then responded with
///   { "msg": { "error": "gridfilter_error", "message": "Grid filter error" } }.
///
/// Example:
///   var b = new FilterBuilder();
///   b.Add("updated_at", FilterBuilder.FilterType.GreaterThan, FilterBuilder.Type.Date, DateTime.UtcNow.AddDays(-1));
///   b.Add("device_number", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, "12345");
///   var url = baseResource + "?gridfilter=" + b.Get();
/// </summary>
public class FilterBuilder
{
    public enum FilterType
    {
        Equals,
        NotEqual,
        Contains,
        NotContains,

        /// <summary>
        /// Case-insensitive, anchored match. Use this where source data differs from
        /// the catalog only in casing - "seca 954" against "Seca 954" - which
        /// <see cref="Equals"/> would miss because it compares case-sensitively.
        /// </summary>
        Matches,
        StartsWith,
        EndsWith,
        GreaterThan,
        LessThan,
        InRange
    }

    public enum Type
    {
        Text,
        Number,
        Date,
        Bool,
        Set,

        /// <summary>
        /// A record id. The API wants <c>object_id</c> as the field type here, not
        /// <c>text</c> — filtering an id as text does not match.
        /// </summary>
        ObjectId
    }

    private readonly Dictionary<string, object> _filters = new();

    public void Clear() => _filters.Clear();

    public void Add(string field, FilterType filterType, Type valueType, object value)
    {
        var entry = new Dictionary<string, object>
        {
            ["filterType"] = MapType(valueType),    // <-- field type, e.g. "date"
            ["type"]       = MapFilter(filterType)  // <-- comparator, e.g. "greaterThan"
        };

        var formatted = FormatValue(value, valueType);

        // Date filters use dateFrom/dateTo; everything else uses 'filter' (and 'filterTo' for ranges).
        if (valueType == Type.Date)
            entry["dateFrom"] = formatted;
        else
            entry["filter"] = formatted;

        _filters[field] = entry;
    }

    public string Get()
    {
        if (_filters.Count == 0) return "{}";       // Samedis-Webview verwendet '{}' wenn leer
        var json = JsonConvert.SerializeObject(_filters);
        return HttpUtility.UrlEncode(json);
    }

    private static string MapType(Type t) => t switch
    {
        Type.Text   => "text",
        Type.Number => "number",
        Type.Date   => "date",
        Type.Bool   => "bool",
        Type.Set    => "set",
        Type.ObjectId => "object_id",
        // No silent fallback, same reason as MapFilter.
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unmapped value type")
    };

    private static string MapFilter(FilterType f) => f switch
    {
        FilterType.Equals       => "equals",
        FilterType.NotEqual     => "notEqual",
        FilterType.Contains     => "contains",
        FilterType.NotContains  => "notContains",
        FilterType.StartsWith   => "startsWith",
        FilterType.EndsWith     => "endsWith",
        FilterType.GreaterThan  => "greaterThan",
        FilterType.LessThan     => "lessThan",
        FilterType.InRange      => "inRange",
        FilterType.Matches      => "matches",
        // No silent fallback: a new FilterType without a mapping used to become "equals"
        // here, which produced a wrong filter instead of an error. That is exactly how the
        // missing "matches" mapping hid until a test asked for it.
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, "Unmapped filter type")
    };

    /// <summary>
    /// Formats a value to the textual form expected inside the gridfilter JSON.
    /// Dates: yyyy-MM-dd (the API documentation shows date-level granularity).
    /// </summary>
    private static object FormatValue(object value, Type valueType)
    {
        return valueType switch
        {
            Type.Date when value is DateTime dt        => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type.Date when value is DateTimeOffset dto => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type.Number when value is IFormattable n   => n.ToString(null, CultureInfo.InvariantCulture),
            _                                          => value
        };
    }
}
