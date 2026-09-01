using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.Query;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.V4.Public;
public class Departments
{
  /// <summary>Resolves a department by title.</summary>
  public static string? FindDepartmentId(ResourceLookup lookup, string title)
    => lookup.ByField("title", title);

  /// <summary>
  /// Resolves a department by title and writes the given details onto it, creating it when it
  /// does not exist.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>The department's code is not sent.</b> The version this replaces put it in an
  /// <c>external_id</c> field, which departments do not have: the model carries no such field
  /// and the controller does not permit one, so the value was dropped by strong parameters
  /// without any error. Anything that has to survive belongs in
  /// <see cref="DepartmentInfo.CostCenter"/>, which maps onto a real field.
  /// </para>
  /// </remarks>
  public static string? FindOrCreateDepartment(IApiClient client, ResourceLookup lookup,
                                               DepartmentInfo department, ISyncLog log)
  {
    var attributes = new Dictionary<string, object?>
    {
      ["title"] = department.Title,
      ["is_active"] = true
    };
    JsonApi.AddStringAttribute(attributes!, "cost_center_number", department.CostCenter);

    return Records.Upsert(
      client, lookup.Resource,
      find: () => lookup.ByField("title", department.Title),
      attributes: attributes,
      log, $"department '{department.Title}'",
      remember: id => lookup.RememberField("title", department.Title, id));
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
