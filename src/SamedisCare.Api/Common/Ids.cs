using System.Text.RegularExpressions;

namespace SamedisCare.Api.Common;

/// <summary>
/// Samedis record ids. They are MongoDB ObjectIds, so 24 hexadecimal characters.
/// <para>
/// Every tool grew its own copy of this check: sync-trainings as a BsonId regex, spl-sync
/// and fluke-sync inside ValidateTenantId. Source data often carries a placeholder or a
/// free-text value in an id column, so the check decides whether a lookup is worth making.
/// </para>
/// </summary>
public static class Ids
{
    private static readonly Regex ObjectId = new("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);

    /// <summary>
    /// True when the value is a well-formed ObjectId. Surrounding whitespace is ignored;
    /// null, empty and anything else is false.
    /// </summary>
    public static bool IsObjectId(string? value)
        => !string.IsNullOrWhiteSpace(value) && ObjectId.IsMatch(value.Trim());
}
