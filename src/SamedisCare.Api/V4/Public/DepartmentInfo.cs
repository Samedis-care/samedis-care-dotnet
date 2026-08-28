namespace SamedisCare.Api.V4.Public;

/// <summary>
/// The set of values needed to look up or create a department, as a single object.
/// <para>
/// Mirrors the parameters of
/// <see cref="Departments.FindOrCreateDepartment(SamedisCare.Api.Http.RequestData, string, string, string?, string?)"/>.
/// It carries no knowledge of where the values came from — a CSV import, an LDAP
/// directory or a database query all produce the same three fields — which is why it
/// belongs next to the department calls rather than in any one tool.
/// </para>
/// </summary>
public class DepartmentInfo
{
    /// <summary>
    /// Key the source system identifies this department by, used for de-duplication
    /// before any API call. Not sent to the API.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Department title. This is what the lookup matches on.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional department code.</summary>
    public string? Code { get; set; }

    /// <summary>Optional cost centre number.</summary>
    public string? CostCenter { get; set; }
}
