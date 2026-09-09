using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.Query;
using SamedisCare.Helper.Logging;

namespace SamedisCare.Api.V4.Public;
public class Positions
{
  /// <summary>Resolves a position by title.</summary>
  public static string? FindPositionId(ResourceLookup lookup, string title)
    => lookup.ByField("title", title);

  /// <summary>
  /// Resolves a position by title, creating it when it does not exist.
  /// </summary>
  /// <remarks>
  /// The version this replaces read <c>meta.total</c> out of the create response to decide
  /// whether the position had been created. A create response carries no meaningful total, so
  /// a position that was created successfully came back as an empty string and the caller
  /// treated it as unresolved.
  /// </remarks>
  public static string? FindOrCreatePosition(IApiClient client, ResourceLookup lookup,
                                             string title, ISyncLog log)
    => Records.FindOrCreate(
         client, lookup.Resource,
         find: () => lookup.ByField("title", title),
         attributes: new Dictionary<string, object?>
         {
           ["title"] = title,
           ["show_in_directory"] = false
         },
         log, $"position '{title}'",
         remember: id => lookup.RememberField("title", title, id));

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
