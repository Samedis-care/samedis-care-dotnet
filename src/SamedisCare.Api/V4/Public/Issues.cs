using Newtonsoft.Json;
using SamedisCare.Api.Common;

namespace SamedisCare.Api.V4.Public;

/// <summary>
/// JSON:API model for /api/{version}/{tenant_scope}/issues.
/// "Issue" is the public Samedis term; this model only ever deals with
/// `issue_type=maintenance`.
///
/// Note: this is the wire-level model. Mapping domain data onto these attributes
/// is the job of the consuming tool.
/// </summary>
public class Issues
{
    public class Attributes
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("tenant_id")] public string? TenantId { get; set; }
        [JsonProperty("inventory_id")] public string? InventoryId { get; set; }
        [JsonProperty("inventory_device_number")] public string? InventoryDeviceNumber { get; set; }

        [JsonProperty("issue_number")] public int? IssueNumber { get; set; }
        [JsonProperty("issue_type")] public string? IssueType { get; set; }
        [JsonProperty("status")] public string? Status { get; set; }

        [JsonProperty("external_id")] public string? ExternalId { get; set; }
        [JsonProperty("title")] public string? Title { get; set; }

        [JsonProperty("date")] public string? Date { get; set; }
        [JsonProperty("done_at")] public string? DoneAt { get; set; }

        /// <summary>
        /// (only for issue_type=maintenance) The service intervals cached from the inventory
        /// at the time the issue was created. Amount (<see cref="ServiceInterval.Value"/>) plus
        /// unit (<see cref="ServiceInterval.Unit"/>: day/week/month/year, default month).
        /// Convert to months where the target system only tracks months.
        /// Source: samedis-public.yaml, schema with_service_intervals.
        /// </summary>
        [JsonProperty("with_service_intervals")] public List<ServiceInterval>? WithServiceIntervals { get; set; }

        [JsonProperty("maintenance_performer")] public string? MaintenancePerformer { get; set; }
        [JsonProperty("maintenance_passed")] public bool? MaintenancePassed { get; set; }
        [JsonProperty("services")] public List<string>? Services { get; set; }

        [JsonProperty("responsible_id")] public string? ResponsibleId { get; set; }
        [JsonProperty("responsible_name")] public string? ResponsibleName { get; set; }

        [JsonProperty("test_result")] public string? TestResult { get; set; }
        [JsonProperty("test_comment")] public string? TestComment { get; set; }

        [JsonProperty("inventory_operation_status")] public string? InventoryOperationStatus { get; set; }

        [JsonProperty("created_at")] public string? CreatedAt { get; set; }
        [JsonProperty("updated_at")] public string? UpdatedAt { get; set; }
    }

    /// <summary>
    /// One entry from with_service_intervals (schema in samedis-public.yaml). An issue can
    /// have several (e.g. maintenance + inspection); picking the right one is the job of
    /// the consuming tool (prefer category=maintenance, then match on label/services).
    /// </summary>
    public class ServiceInterval
    {
        /// <summary>Interval category: "maintenance" or "inspection".</summary>
        [JsonProperty("category")] public string? Category { get; set; }

        /// <summary>Identifying label, in the language of the main tenant, e.g. "STK".</summary>
        [JsonProperty("label")] public string? Label { get; set; }

        /// <summary>Number of units.</summary>
        [JsonProperty("value")] public int? Value { get; set; }

        /// <summary>Unit: day | week | month | year (default month).</summary>
        [JsonProperty("unit")] public string? Unit { get; set; }
    }

    public class Data
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("attributes")] public Attributes? Attributes { get; set; }
    }

    public class Meta
    {
        [JsonProperty("total")] public int? Total { get; set; }
    }

    public class Root
    {
        [JsonProperty("data")]
        [JsonConverter(typeof(Helper.SingleOrArrayConverter<Data>))]
        public List<Data>? Data { get; set; }

        [JsonProperty("meta")] public Meta? Meta { get; set; }
    }

    /// <summary>
    /// Wraps an attributes dictionary into the request structure Samedis expects:
    ///   { "data": { "title": "...", "date": "...", ... } }
    ///
    /// Important: Samedis does NOT use the strict JSON:API shape { data: { type, attributes } }.
    /// The server validates directly against params[:data][:title], the same way a curl with
    /// `-F "data[title]=..."` (multipart/form-data) does. Wrapping the fields in an
    /// 'attributes' subkey makes the server ignore them silently, which yields
    /// "Title cannot be blank" / "Due on cannot be blank" even when everything was set.
    /// </summary>
    public static string BuildEnvelope(IDictionary<string, object> attributes)
    {
        var envelope = new { data = attributes };
        return JsonConvert.SerializeObject(envelope);
    }
}
