using Newtonsoft.Json;
using SamedisCare.Api.Common;

namespace SamedisCare.Api.V4.Public;

/// <summary>
/// JSON:API model for /api/{version}/{tenant_scope}/inventories.
/// Slimmed to the fields the sync tools actually need:
/// device_number, serial_number, device_model_*, device_type_title, manufacturer.
/// </summary>
public class Inventories
{
    public class Attributes
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("tenant_id")] public string? TenantId { get; set; }
        [JsonProperty("device_number")] public string? DeviceNumber { get; set; }

        /// <summary>
        /// Service intervals in the compatibility shape the server sends:
        /// <c>{ label =&gt; value }</c>, where the value is a bare number when the entry is
        /// maintenance in months and the string <c>"value:unit:category"</c> otherwise — see
        /// <c>WithService#service_intervals</c> in the backend.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value can be JSON <c>null</c>, when the interval was stored without a value, and the
        /// string form can have an empty value part (<c>":month:inspection"</c>). Both mean "no
        /// interval": treat them as absent. <c>Convert.ToInt32(null)</c> would turn them into 0.
        /// </para>
        /// <para>
        /// Also carried by the inventory index (<c>InventoryOverviewSerializer</c>), unlike an
        /// issue's <c>with_service_intervals</c>, which only the full <c>IssueSerializer</c>
        /// (show) returns. A tool reading a worklist from the issue index gets its interval here.
        /// </para>
        /// </remarks>
        [JsonProperty("service_intervals")]
        public Dictionary<string, object>? ServiceIntervals { get; set; }

        [JsonProperty("serial_number")] public string? SerialNumber { get; set; }

        [JsonProperty("device_model_title")] public string? DeviceModelTitle { get; set; }
        [JsonProperty("device_model_version")] public string? DeviceModelVersion { get; set; }
        [JsonProperty("device_model_current_responsible_manufacturer")] public string? DeviceModelCurrentResponsibleManufacturer { get; set; }
        [JsonProperty("device_model_manufacturer_according_to_type_plate")] public string? DeviceModelManufacturerAccordingToTypePlate { get; set; }

        [JsonProperty("device_type_title")] public string? DeviceTypeTitle { get; set; }

        [JsonProperty("device_location_id")] public string? DeviceLocationId { get; set; }
        [JsonProperty("device_location_title")] public string? DeviceLocationTitle { get; set; }

        [JsonProperty("department_id")] public string? DepartmentId { get; set; }
        [JsonProperty("department_title")] public string? DepartmentTitle { get; set; }

        [JsonProperty("operation_status")] public string? OperationStatus { get; set; }
        [JsonProperty("no_medical_device")] public bool? NoMedicalDevice { get; set; }
        [JsonProperty("do_maintenance")] public bool? DoMaintenance { get; set; }

        [JsonProperty("created_at")] public string? CreatedAt { get; set; }
        [JsonProperty("updated_at")] public string? UpdatedAt { get; set; }
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
        [JsonConverter(typeof(JsonApi.SingleOrArrayConverter<Data>))]
        public List<Data>? Data { get; set; }

        [JsonProperty("meta")] public Meta? Meta { get; set; }
    }
}
