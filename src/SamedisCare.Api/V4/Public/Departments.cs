using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Query;

namespace SamedisCare.Api.V4.Public;
public class Departments
{

  public static string? FindDepartmentId(RequestData client, string departmentsResource, string title)
  {
    // FilterBuilder URL-encodes the payload. The previous hand-built string only escaped
    // quotes, so a title containing '&', '#' or '+' terminated the query parameter early
    // and the lookup silently filtered on a truncated value.
    var gf = "?gridfilter=" + TitleFilter(title) + "&page=1&limit=1";
    var getResp = client.Get(departmentsResource + gf);

    if (string.IsNullOrEmpty(getResp))
      return null;

    try
    {
      var jo = JObject.Parse(getResp);
      var dataTok = jo["data"];
      if (dataTok is JObject obj)
        return obj["id"]?.ToString() ?? obj["attributes"]?["id"]?.ToString();
      if (dataTok is JArray arr && arr.Count > 0)
        return arr[0]["id"]?.ToString() ?? arr[0]["attributes"]?["id"]?.ToString();
    }
    catch { }

    return null;
  }

  /// <summary>
  /// Overload taking a <see cref="DepartmentInfo"/>, so callers that already collected
  /// title, code and cost centre together do not have to unpack them at the call site.
  /// </summary>
  public static string? FindOrCreateDepartment(RequestData client, string departmentsResource, DepartmentInfo department)
    => FindOrCreateDepartment(client, departmentsResource, department.Title, department.Code, department.CostCenter);

  public static string? FindOrCreateDepartment(RequestData client, string departmentsResource, string title, string? code, string? costCenter)
  {
    // lookup
    // FilterBuilder URL-encodes the payload. The previous hand-built string only escaped
    // quotes, so a title containing '&', '#' or '+' terminated the query parameter early
    // and the lookup silently filtered on a truncated value.
    var gf = "?gridfilter=" + TitleFilter(title) + "&page=1&limit=1";
    var getResp = client.Get(departmentsResource + gf);

    string? existingId = null;
    if (!string.IsNullOrEmpty(getResp))
    {
      try
      {
        var jo = JObject.Parse(getResp);
        var dataTok = jo["data"];
        if (dataTok is JObject obj)
          existingId = obj["id"]?.ToString() ?? obj["attributes"]?["id"]?.ToString();
        else if (dataTok is JArray arr && arr.Count > 0)
          existingId = arr[0]["id"]?.ToString() ?? arr[0]["attributes"]?["id"]?.ToString();
      }
      catch { /* ignore, treat as not found */ }
    }

    // prepare payload (create or update)
    var payload = new JObject
    {
      ["data"] = new JObject
      {
        ["title"] = title,
        ["external_id"] = string.IsNullOrWhiteSpace(code) ? null : code,
        ["cost_center_number"] = string.IsNullOrWhiteSpace(costCenter) ? null : costCenter,
        ["is_active"] = true
      }
    };
    var body = JsonConvert.SerializeObject(payload, Formatting.None);

    string? resp = existingId == null
      ? client.Post(departmentsResource, body)
      : client.Put(departmentsResource, existingId, body);

    // Some PUTs may return 204 No Content (empty body) – in that case, return existingId
    if (string.IsNullOrWhiteSpace(resp))
      return existingId; // could be null if POST failed silently

    try
    {
      var jo = JObject.Parse(resp);
      var dataTok = jo["data"];
      if (dataTok is JObject obj)
        return obj["id"]?.ToString() ?? obj["attributes"]?["id"]?.ToString() ?? existingId;
      if (dataTok is JArray arr && arr.Count > 0)
        return arr[0]["id"]?.ToString() ?? arr[0]["attributes"]?["id"]?.ToString() ?? existingId;
      return existingId;
    }
    catch
    {
      // If response is not JSON or unexpected, fall back to existing id (on update) or null (on create)
      return existingId;
    }
  }

  private static string TitleFilter(string title)
  {
    var filter = new FilterBuilder();
    filter.Add("title", FilterBuilder.FilterType.Equals, FilterBuilder.Type.Text, title);
    return filter.Get();
  }

  // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
  public class Attributes
  {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("tenant_id")]
    public string? TenantId { get; set; }

    [JsonProperty("cost_center_number")]
    public string? CostCenterNumber { get; set; }

    [JsonProperty("created_at")]
    public string? CreatedAt { get; set; }

    [JsonProperty("created_by_user")]
    public string? CreatedByUser { get; set; }

    [JsonProperty("inventory_count")]
    public int? InventoryCount { get; set; }

    [JsonProperty("is_active")]
    public bool? IsActive { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("profit_center_title")]
    public string? ProfitCenterTitle { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonProperty("updated_by_user")]
    public string? UpdatedByUser { get; set; }
  }

  public class Data
  {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("attributes")]
    public Attributes? Attributes { get; set; }
  }

  public class Fields
  {
  }

  public class JsonApiOptions
  {
    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("page")]
    public int Page { get; set; }

    [JsonProperty("fields")]
    public Fields? Fields { get; set; }
  }

  public class Meta
  {
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("json_api_options")]
    public JsonApiOptions? JsonApiOptions { get; set; }

    [JsonProperty("locale")]
    public string? Locale { get; set; }

    [JsonProperty("msg")]
    public Msg? Msg { get; set; }

    [JsonProperty("git_version")]
    public string? GitVersion { get; set; }

    [JsonProperty("current_user_id")]
    public string? CurrentUserId { get; set; }

    [JsonProperty("status")]
    public int Status { get; set; }
  }

  public class Msg
  {
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("error_details")]
    public string? ErrorDetails { get; set; }
  }

  public class Root
  {
    [JsonProperty("data")]
    [JsonConverter(typeof(JsonApi.SingleOrArrayConverter<Data>))]
    public List<Data>? Data { get; set; }

    [JsonProperty("meta")]
    public Meta? Meta { get; set; }
  }
}
