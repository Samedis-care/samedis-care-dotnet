namespace SamedisCare.Api.V4.Public;

/// <summary>
/// The set of values needed to look up or create a department, as a single object.
/// <para>
/// What <see cref="Departments.FindOrCreateDepartment"/> needs, gathered in one place.
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

    /// <summary>
    /// The source system's own code for the department.
    /// <para>
    /// <b>Not sent to the API.</b> A department has no field for it: the model carries no
    /// <c>external_id</c> and the controller permits none, so a value put there is dropped by
    /// strong parameters without an error. It is kept on this object because source systems
    /// supply it and a tool may need it for its own bookkeeping — but if it has to reach
    /// Samedis, it belongs in <see cref="CostCenter"/>.
    /// </para>
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Optional cost centre number.</summary>
    public string? CostCenter { get; set; }
}
