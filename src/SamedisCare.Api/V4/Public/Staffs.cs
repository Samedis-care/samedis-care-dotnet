using Newtonsoft.Json;
using SamedisCare.Api.Common;

namespace SamedisCare.Api.V4.Public;
public class Staffs {

  /// <summary>
  /// Resolves a staff id from an employee number. Returns an empty string when there is no
  /// match, so a caller can tell "not found" from a failed request by checking the
  /// client's status code.
  /// </summary>
  /// <remarks>
  /// Uses <c>staffs/via/employee_no/{no}</c>. That route is NOT in any of the OpenAPI specs
  /// under doc/v4, but it is what the syncs have always used and it works. The employee
  /// number comes straight from a source system and may contain characters that would break
  /// a path segment, hence the escaping.
  /// </remarks>
  public static string FindIdByEmployeeNo(Http.RequestData client, Routing.ITenantScope scope,
                                          string employeeNo)
  {
    if (string.IsNullOrWhiteSpace(employeeNo)) return "";

    var content = client.Get(scope.Resource($"staffs/via/employee_no/{Uri.EscapeDataString(employeeNo.Trim())}"));

    return JsonApi.IsSuccess(client.StatusCode) ? JsonApi.FirstDataId(content) ?? "" : "";
  }

  // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
  public class Attributes
  {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("account")]
    public string? Account { get; set; }

    [JsonProperty("administered_briefings_count")]
    public int? AdministeredBriefingsCount { get; }

    [JsonProperty("avatar")]
    public string? Avatar { get; }

    [JsonProperty("catalog_ids")]
    public List<string>? CatalogIds { get; }

    [JsonProperty("email")]
    public string? Email { get; set; }

    [JsonProperty("employee_no")]
    public string? EmployeeNo { get; set; }

    [JsonProperty("first_name")]
    public string? FirstName { get; set; }

    [JsonProperty("ident_user_id")]
    public string? IdentUserId { get; }

    [JsonProperty("joined")]
    public string? Joined { get; set; }

    [JsonProperty("last_name")]
    public string? LastName { get; set; }

    [JsonProperty("left")]
    public string? Left { get; set; }

    [JsonProperty("login_allowed")]
    public bool? LoginAllowed { get; set; }

    [JsonProperty("manufacturer_catalog_ids")]
    public List<string>? ManufacturerCatalogIds { get; }

    [JsonProperty("mobile_number")]
    public string? MobileNumber { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("department_ids")]
    public List<string>? DepartmentIds { get; set; }

    [JsonProperty("position_ids")]
    public List<string>? PositionIds { get; set; }
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
  }

  public class Msg
  {
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
    [JsonProperty("message")]
    public string? Message { get; set; }
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
