using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SamedisCare.Api.Common;

/// <summary>
/// Helpers for the JSON:API envelope Samedis returns.
/// <para>
/// Renamed from <c>Helper</c>: with a <c>SamedisCare.Helper</c> namespace in the family,
/// the identifier <c>Helper</c> resolved to that namespace instead of this class. The
/// generic parsing and DataRow helpers that used to sit here moved to
/// <c>SamedisCare.Helper.Text.Strings</c> and <c>SamedisCare.Helper.Data.Rows</c>, since
/// they were never API concerns.
/// </para>
/// </summary>
public static class JsonApi
{
    /// <summary>
    /// Extracts <c>data[0].id</c>, or <c>data.id</c> when data is a single object. Returns
    /// null for an empty body or one that is not the expected shape; never throws.
    /// </summary>
    public static string? ExtractDataId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var root = JsonConvert.DeserializeObject<JObject>(json);
            var data = root?["data"];
            if (data == null) return null;
            if (data is JArray arr)
                return arr.FirstOrDefault()?["id"]?.ToString();
            return data["id"]?.ToString();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The first <c>data</c> element: the array's first entry for a list response, or the
    /// object itself for a single-record one. Returns null for an empty body or one that is
    /// not the expected shape; never throws.
    /// </summary>
    public static JToken? FirstData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var data = JObject.Parse(json)["data"];
            return data is JArray arr ? arr.FirstOrDefault() : data;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The <c>id</c> of the first <c>data</c> element, or null. Same shape tolerance as
    /// <see cref="FirstData"/>.
    /// </summary>
    public static string? FirstDataId(string? json)
        => FirstData(json)?["id"]?.ToString();

    /// <summary>
    /// Collects one attribute across all <c>data</c> entries, skipping empty values.
    /// Case-insensitive set, because these are compared against source data.
    /// </summary>
    public static HashSet<string> AttributeSet(string? json, string attribute)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return set;
        try
        {
            if (JObject.Parse(json)["data"] is JArray arr)
                foreach (var item in arr)
                {
                    var value = item["attributes"]?[attribute]?.ToString();
                    if (!string.IsNullOrEmpty(value)) set.Add(value);
                }
        }
        catch (JsonException) { /* not the expected shape - treat as none */ }
        return set;
    }

    /// <summary>Number of entries in a list response, or 0 when it is not a list.</summary>
    public static int DataCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try { return JObject.Parse(json)["data"] is JArray arr ? arr.Count : 0; }
        catch (JsonException) { return 0; }
    }

    /// <summary>
    /// True for a 2xx status. The tools each had their own two-line version of this.
    /// </summary>
    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>Adds an attribute only when the value is not null or whitespace.</summary>
    public static void AddStringAttribute(IDictionary<string, object> attributes, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        attributes[key] = value;
    }

    /// <summary>
    /// Newtonsoft converter that accepts both <c>data: {...}</c> and <c>data: [...]</c> —
    /// JSON:API returns one or many depending on the endpoint.
    /// </summary>
    public class SingleOrArrayConverter<T> : JsonConverter
    {
        public override bool CanConvert(System.Type objectType) => objectType == typeof(List<T>);

        public override object ReadJson(JsonReader reader, System.Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.Array)
                return token.ToObject<List<T>>(serializer) ?? new List<T>();
            var single = token.ToObject<T>(serializer);
            return single == null ? new List<T>() : new List<T> { single };
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }
}
