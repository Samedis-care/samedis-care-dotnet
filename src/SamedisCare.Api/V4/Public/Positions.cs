using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Query;

namespace SamedisCare.Api.V4.Public;
public class Positions
{

  public static string? FindPositionId(RequestData client, string positionsResource, string title)
  {
    // FilterBuilder URL-encodes the payload. The previous hand-built string only escaped
    // quotes, so a title containing '&', '#' or '+' terminated the query parameter early
    // and the lookup silently filtered on a truncated value.
    var gf = "?gridfilter=" + TitleFilter(title) + "&page=1&limit=1";
    var getResp = client.Get(positionsResource + gf);
    Root? posRoot = null;
    if (!string.IsNullOrEmpty(getResp))
    {
      posRoot = JsonConvert.DeserializeObject<Root>(getResp);
    }
    var total = posRoot?.Meta?.Total ?? 0;
    if (total > 0)
      return posRoot!.Data![0].Attributes!.Id;
    return null;
  }

  public static string? FindOrCreatePosition(RequestData client, string positionsResource, string title)
  {
    // Build gridfilter for title equals
    // FilterBuilder URL-encodes the payload. The previous hand-built string only escaped
    // quotes, so a title containing '&', '#' or '+' terminated the query parameter early
    // and the lookup silently filtered on a truncated value.
    var gf = "?gridfilter=" + TitleFilter(title) + "&page=1&limit=1";
    var getResp = client.Get(positionsResource + gf);
    Root? posRoot = null;
    if (!string.IsNullOrEmpty(getResp))
    {
      posRoot = JsonConvert.DeserializeObject<Root>(getResp);
    }
    var total = posRoot?.Meta?.Total ?? 0;
    if (total > 0)
      return posRoot!.Data![0].Attributes!.Id;

    var payload = new JObject
    {
      ["data"] = new JObject
      {
        ["title"] = title,
        ["show_in_directory"] = false
      }
    };

    var body = JsonConvert.SerializeObject(payload, Formatting.None);
    var postResp = client.Post(positionsResource, body);
    if (!string.IsNullOrEmpty(postResp))
    {
      posRoot = JsonConvert.DeserializeObject<Root>(postResp);
    }
    total = posRoot?.Meta?.Total ?? 0;
    if (total > 0) return posRoot!.Data![0].Attributes!.Id;
    return "";
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

    [JsonProperty("created_at")]
    public string? CreatedAt { get; set; }

    [JsonProperty("created_by_user")]
    public string? CreatedByUser { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("external_id")]
    public string? ExternalId { get; set; }

    [JsonProperty("show_in_directory")]
    public bool? ShowInDirectory { get; set; }

    [JsonProperty("staff_ids")]
    public List<string>? StaffIds { get; set; }

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
    public List<string>? ErrorDetails { get; set; }
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
