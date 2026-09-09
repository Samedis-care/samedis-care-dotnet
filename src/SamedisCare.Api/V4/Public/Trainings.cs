using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SamedisCare.Api.Common;
using SamedisCare.Api.Http;
using SamedisCare.Api.Lookup;
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

    /// <summary>A training found by lookup: its id, current status, and how many records
    /// the marker matched.</summary>
    /// <param name="Id">The training's Samedis id.</param>
    /// <param name="Status">Its current status.</param>
    /// <param name="Matches">
    /// How many records carry this marker. More than one is a real condition, not a
    /// theoretical one, and the caller should say so rather than silently take the first.
    /// </param>
    public readonly record struct Existing(string Id, string Status, int Matches = 0)
    {
        public bool Found => !string.IsNullOrEmpty(Id);

        /// <summary>Several trainings carry this source id -- the tenant has a duplicate.</summary>
        public bool Ambiguous => Matches > 1;
    }

    /// <summary>
    /// Finds the training a sync stamped with <paramref name="marker"/> in its
    /// <c>remark</c>. This is how a re-run recognises what it already imported, so both
    /// possible mistakes are expensive: missing the record imports it a second time, and
    /// matching the wrong one skips a training that was never imported at all.
    /// </summary>
    /// <param name="client">The API client.</param>
    /// <param name="scope">The tenant scope to look under.</param>
    /// <param name="marker">
    /// The stamp, parentheses included, e.g. <c>(4711)</c>. Matched literally -- the server
    /// runs the value through <c>Regexp.escape</c>, so the parentheses are characters and
    /// not a capture group.
    /// </param>
    /// <exception cref="LookupUnavailableException">
    /// The lookup was not answered. This is the read that decides whether a training gets
    /// created, so a failure must not pass as "not there yet": a token that lost a
    /// permission would otherwise re-import every training the tenant already has.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why the filter is not enough on its own.</b> <c>contains</c> looks anywhere in the
    /// remark, and the remark holds free text from the source system alongside the stamp --
    /// a site named "Haus A (12)" carries the literal <c>(12)</c> without being training 12.
    /// Taking the server's first hit would then report a different training as already
    /// imported and drop this one for good.
    /// </para>
    /// <para>
    /// So the filter stays broad and the decision is made here: of the records that carry
    /// the marker, the ones whose remark <b>ends</b> with it are preferred, because that is
    /// where a sync appends its stamp. The broad match is still what gets asked for -- an
    /// operator who adds a note after the stamp must not turn the training invisible and
    /// have it imported again.
    /// </para>
    /// </remarks>
    public static Existing FindByRemark(IApiClient client, ITenantScope scope, string marker)
    {
        var resource = scope.Resource("trainings");

        var filter = new FilterBuilder();
        filter.Add("remark", FilterBuilder.FilterType.Contains, FilterBuilder.Type.Text, marker);

        var content = client.Get($"{resource}?page[limit]={MarkerCandidates}&gridfilter={filter.Get()}");
        if (!JsonApi.IsSuccess(client.StatusCode))
        {
            LookupUnavailableException.ThrowUnlessAbsent(resource, client.StatusCode, content);
            return default;
        }

        var carrying = JsonApi.AllData(content)
            .Select(d => (Id: d["id"]?.ToString() ?? "",
                          Status: d["attributes"]?["status"]?.ToString() ?? "",
                          Remark: d["attributes"]?["remark"]?.ToString() ?? ""))
            .Where(c => c.Id.Length > 0)
            .ToList();

        if (carrying.Count == 0) return default;

        var stamped = carrying
            .Where(c => c.Remark.TrimEnd().EndsWith(marker, StringComparison.Ordinal))
            .ToList();

        // Nothing ends with the marker: every hit carries it incidentally, or someone wrote
        // past the stamp. Falling back to the broad set keeps the re-import from happening;
        // Matches tells the caller the choice was not clear-cut.
        var matches = stamped.Count > 0 ? stamped : carrying;

        return new Existing(matches[0].Id, matches[0].Status, matches.Count);
    }

    /// <summary>
    /// How many records the marker lookup reads before deciding. One is not enough -- the
    /// choice between an incidental hit and the stamped one can only be made across the
    /// candidates -- and a marker matching more than a handful means the tenant's data needs
    /// fixing, not a larger page.
    /// </summary>
    private const int MarkerCandidates = 20;

    /// <summary>Creates a training. Returns the new id, or null when the call failed.</summary>
    public static string? Create(IApiClient client, ITenantScope scope, JObject attributes)
    {
        var content = client.Post(scope.Resource("trainings"), Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode) ? JsonApi.FirstDataId(content) : null;
    }

    /// <summary>Updates a training's attributes.</summary>
    public static bool Update(IApiClient client, ITenantScope scope, string trainingId,
                              JObject attributes)
    {
        client.Put(scope.Resource("trainings"), trainingId, Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>
    /// Sets a training's status. <c>completed</c> is derived from the status rather than
    /// passed separately, because the two must not disagree.
    /// </summary>
    public static bool SetStatus(IApiClient client, ITenantScope scope, string trainingId,
                                 string briefingType, string status)
        => Update(client, scope, trainingId, new JObject
        {
            ["briefing_type"] = briefingType,
            ["status"] = status,
            ["completed"] = status == "closed",
        });

    /// <summary>Attaches a device model to a training.</summary>
    public static bool AddDeviceModel(IApiClient client, ITenantScope scope, string trainingId,
                                      JObject attributes)
    {
        client.Post(scope.Resource($"trainings/{trainingId}/device_models"), Envelope(attributes));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>Attaches a participant to a training.</summary>
    public static bool AddStaff(IApiClient client, ITenantScope scope, string trainingId, string staffId)
    {
        client.Post(scope.Resource($"trainings/{trainingId}/staffs"),
                    Envelope(new JObject { ["staff_id"] = staffId }));
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>Uploads a document to a training.</summary>
    public static bool UploadDocument(IApiClient client, ITenantScope scope, string trainingId,
                                      string filePath, string fileName)
    {
        client.PostDocument(scope.Resource($"trainings/{trainingId}/uploads"), filePath, fileName);
        return JsonApi.IsSuccess(client.StatusCode);
    }

    /// <summary>
    /// The <c>catalog_id</c>s already attached to a training. Used to make a resumed run
    /// idempotent instead of attaching a device twice.
    /// </summary>
    /// <exception cref="LookupUnavailableException">
    /// The read was not answered. An empty set is indistinguishable from "nothing attached",
    /// and that is precisely the answer a resumed run acts on -- so an unanswered read would
    /// undo the idempotency this method exists to provide.
    /// </exception>
    public static HashSet<string> AttachedCatalogIds(IApiClient client, ITenantScope scope, string trainingId)
        => AttributeSet(client, scope.Resource($"trainings/{trainingId}/device_models"), "catalog_id");

    /// <summary>The <c>staff_id</c>s already attached to a training.</summary>
    public static HashSet<string> AttachedStaffIds(IApiClient client, ITenantScope scope, string trainingId)
        => AttributeSet(client, scope.Resource($"trainings/{trainingId}/staffs"), "staff_id");

    /// <summary>How many documents are already attached to a training.</summary>
    /// <inheritdoc cref="AttachedCatalogIds" path="/exception"/>
    public static int UploadCount(IApiClient client, ITenantScope scope, string trainingId)
    {
        var resource = scope.Resource($"trainings/{trainingId}/uploads");
        var content = client.Get($"{resource}?page[limit]={PageLimit}");

        LookupUnavailableException.ThrowUnlessAnswered(resource, client.StatusCode, content);
        return JsonApi.DataCount(content);
    }

    /// <summary>
    /// Page size for the sub-resource reads above. High enough that a training's devices,
    /// participants or documents fit in one request — these are per-training lists, not
    /// tenant-wide ones.
    /// </summary>
    private const int PageLimit = 500;

    private static HashSet<string> AttributeSet(IApiClient client, string resource, string attribute)
    {
        var content = client.Get($"{resource}?page[limit]={PageLimit}");

        LookupUnavailableException.ThrowUnlessAnswered(resource, client.StatusCode, content);
        return JsonApi.AttributeSet(content, attribute);
    }

    // Samedis does not use the strict JSON:API { data: { type, attributes } } form; the
    // server validates params[:data][:<field>] directly. See Issues.BuildEnvelope.
    private static string Envelope(JObject attributes)
        => JsonConvert.SerializeObject(new Dictionary<string, object?> { ["data"] = attributes },
                                       Formatting.None);
}
