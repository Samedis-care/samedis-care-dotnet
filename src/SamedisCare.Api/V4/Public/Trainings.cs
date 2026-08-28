using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Query;
using SamedisCare.Api.Routing;

namespace SamedisCare.Api.V4.Public;

/// <summary>
/// The <c>/trainings</c> resource and its sub-resources (<c>device_models</c>,
/// <c>staffs</c>, <c>uploads</c>).
/// <para>
/// The API calls the record a briefing (<c>BriefingSerializer</c>) while the endpoint is
/// <c>trainings</c>; this class is named after the endpoint, like the other resources here.
/// Attribute names follow the serializer.
/// </para>
/// </summary>
public class Trainings
{
    public class Attributes
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("tenant_id")] public string? TenantId { get; set; }
        [JsonProperty("briefing_type")] public string? BriefingType { get; set; }
        [JsonProperty("completed")] public bool? Completed { get; set; }
        [JsonProperty("date")] public string? Date { get; set; }
        [JsonProperty("device_model_ids")] public List<string>? DeviceModelIds { get; set; }
        [JsonProperty("instructor")] public string? Instructor { get; set; }
        [JsonProperty("instructor_id")] public string? InstructorId { get; set; }
        [JsonProperty("invalidate_reason")] public string? InvalidateReason { get; set; }
        [JsonProperty("is_digital")] public bool? IsDigital { get; set; }
        [JsonProperty("remark")] public string? Remark { get; set; }
        [JsonProperty("staff_ids")] public List<string>? StaffIds { get; set; }
        [JsonProperty("status")] public string? Status { get; set; }
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("vendor_company")] public string? VendorCompany { get; set; }
        [JsonProperty("created_at")] public string? CreatedAt { get; set; }
        [JsonProperty("updated_at")] public string? UpdatedAt { get; set; }
    }

    public class Data
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("attributes")] public Attributes? Attributes { get; set; }
    }

    public class Root
    {
        [JsonProperty("data")]
        [JsonConverter(typeof(JsonApi.SingleOrArrayConverter<Data>))]
        public List<Data>? Data { get; set; }
    }

    /// <summary>A training found by lookup: its id and current status.</summary>
    public readonly record struct Existing(string Id, string Status)
    {
        public bool Found => !string.IsNullOrEmpty(Id);
    }

    /// <summary>
    /// Finds a training whose <c>remark</c> contains <paramref name="marker"/>. Sync tools
    /// stamp their source id into the remark, which is how a re-run recognises what it
    /// already created. Returns an empty result when nothing matches.
    /// </summary>
    public static Existing FindByRemark(RequestData client, ITenantScope scope, string marker)
    {
        var filter = new FilterBuilder();
        filter.Add("remark", FilterBuilder.FilterType.Contains, FilterBuilder.Type.Text, marker);

        var content = client.Get($"{scope.Resource("trainings")}?page[limit]=1&gridfilter={filter.Get()}");
        if (!JsonApi.IsSuccess(client.StatusCode)) return default;

        var data = JsonApi.FirstData(content);
        if (data == null) return default;

        return new Existing(data["id"]?.ToString() ?? "",
                            data["attributes"]?["status"]?.ToString() ?? "");
    }

    /// <summary>Creates a training. Returns the new id, or null when the call failed.</summary>
    public static string? Create(RequestData client, ITenantScope scope, JObject attributes)
    {
        var content = client.Post(scope.Resource("trainings"), Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode) ? JsonApi.FirstDataId(content) : null;
    }

    /// <summary>Updates a training's attributes.</summary>
    public static bool Update(RequestData client, ITenantScope scope, string trainingId,
                              JObject attributes)
    {
        client.Put(scope.Resource("trainings"), trainingId, Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>
    /// Sets a training's status. <c>completed</c> is derived from the status rather than
    /// passed separately, because the two must not disagree.
    /// </summary>
    public static bool SetStatus(RequestData client, ITenantScope scope, string trainingId,
                                 string briefingType, string status)
        => Update(client, scope, trainingId, new JObject
        {
            ["briefing_type"] = briefingType,
            ["status"] = status,
            ["completed"] = status == "closed",
        });

    /// <summary>Attaches a device model to a training.</summary>
    public static bool AddDeviceModel(RequestData client, ITenantScope scope, string trainingId,
                                      JObject attributes)
    {
        client.Post(scope.Resource($"trainings/{trainingId}/device_models"), Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>Attaches a participant to a training.</summary>
    public static bool AddStaff(RequestData client, ITenantScope scope, string trainingId, string staffId)
    {
        client.Post(scope.Resource($"trainings/{trainingId}/staffs"),
                    Envelope(new JObject { ["staff_id"] = staffId }));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>Uploads a document to a training.</summary>
    public static bool UploadDocument(RequestData client, ITenantScope scope, string trainingId,
                                      string filePath, string fileName)
    {
        client.PostDocument(scope.Resource($"trainings/{trainingId}/uploads"), filePath, fileName);
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>
    /// The <c>catalog_id</c>s already attached to a training. Used to make a resumed run
    /// idempotent instead of attaching a device twice.
    /// </summary>
    public static HashSet<string> AttachedCatalogIds(RequestData client, ITenantScope scope, string trainingId)
        => AttributeSet(client, scope.Resource($"trainings/{trainingId}/device_models"), "catalog_id");

    /// <summary>The <c>staff_id</c>s already attached to a training.</summary>
    public static HashSet<string> AttachedStaffIds(RequestData client, ITenantScope scope, string trainingId)
        => AttributeSet(client, scope.Resource($"trainings/{trainingId}/staffs"), "staff_id");

    /// <summary>How many documents are already attached to a training.</summary>
    public static int UploadCount(RequestData client, ITenantScope scope, string trainingId)
    {
        var content = client.Get($"{scope.Resource($"trainings/{trainingId}/uploads")}?page[limit]={PageLimit}");
        return JsonApi.IsSuccess(client.StatusCode) ? JsonApi.DataCount(content) : 0;
    }

    /// <summary>
    /// Page size for the sub-resource reads above. High enough that a training's devices,
    /// participants or documents fit in one request — these are per-training lists, not
    /// tenant-wide ones.
    /// </summary>
    private const int PageLimit = 500;

    private static HashSet<string> AttributeSet(RequestData client, string resource, string attribute)
    {
        var content = client.Get($"{resource}?page[limit]={PageLimit}");
        return JsonApi.IsSuccess(client.StatusCode)
            ? JsonApi.AttributeSet(content, attribute)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    // Samedis does not use the strict JSON:API { data: { type, attributes } } form; the
    // server validates params[:data][:<field>] directly. See Issues.BuildEnvelope.
    private static string Envelope(JObject attributes)
        => JsonConvert.SerializeObject(new Dictionary<string, object?> { ["data"] = attributes },
                                       Formatting.None);
}
