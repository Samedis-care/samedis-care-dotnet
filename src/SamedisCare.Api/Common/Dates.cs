using System.Globalization;

namespace SamedisCare.Api.Common;

/// <summary>
/// One date parser for the whole tool family. It replaces three separate
/// implementations that had drifted apart across the sync tools.
/// <para>
/// Culture and <see cref="DateTimeStyles"/> are explicit parameters on purpose. The
/// implementations this consolidates used different values — invariant culture with
/// <c>AssumeUniversal | AdjustToUniversal</c> in one place, <c>de-DE</c> with
/// <c>AssumeLocal</c> in another. Those are not interchangeable: swapping them shifts a
/// parsed value by the UTC offset, which moves midnight timestamps to the previous or
/// next day. A caller must therefore keep the values its old code used rather than rely
/// on the defaults here.
/// </para>
/// </summary>
public static class Dates
{
    /// <summary>
    /// Formats accepted by default: ISO first, then the German forms with and without
    /// leading zeros and with a two-digit year.
    /// </summary>
    public static readonly string[] DefaultFormats =
    {
        "yyyy-MM-dd",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd.MM.yy",
        "d.M.yy",
    };

    /// <summary>
    /// Parses a date, trying <paramref name="formats"/> exactly first and falling back to
    /// a general parse. Returns false for null, empty or whitespace input.
    /// </summary>
    /// <param name="input">The text to parse; surrounding whitespace is ignored.</param>
    /// <param name="date">The parsed value, or <c>default</c> when parsing fails.</param>
    /// <param name="formats">Exact formats to try; <see cref="DefaultFormats"/> when null.</param>
    /// <param name="culture">Culture for parsing; <see cref="CultureInfo.InvariantCulture"/> when null.</param>
    /// <param name="styles">
    /// How to treat missing timezone information in the exact-format pass. Pass the value
    /// the calling code used before — see the note on this class about why it matters.
    /// </param>
    /// <param name="fallbackStyles">
    /// Styles for the general-parse fallback. Defaults to <paramref name="styles"/>.
    /// This exists because the implementations being consolidated deliberately used
    /// stricter styles for the known formats and looser ones for the fallback; collapsing
    /// the two would shift date-only values across a day boundary.
    /// </param>
    public static bool TryParse(
        string? input,
        out DateTime date,
        IEnumerable<string>? formats = null,
        CultureInfo? culture = null,
        DateTimeStyles styles = DateTimeStyles.None,
        DateTimeStyles? fallbackStyles = null)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        var fmts = (formats as string[]) ?? formats?.ToArray() ?? DefaultFormats;
        var cult = culture ?? CultureInfo.InvariantCulture;

        if (DateTime.TryParseExact(text, fmts, cult, styles, out date))
            return true;

        return DateTime.TryParse(text, cult, fallbackStyles ?? styles, out date);
    }

    /// <summary>
    /// Parses an Active Directory GeneralizedTime value (e.g. <c>20240531193352.0Z</c>),
    /// falling back to <see cref="TryParse"/> for anything else. The value is always UTC,
    /// hence the fixed <c>AssumeUniversal</c>.
    /// </summary>
    public static bool TryParseGeneralizedTime(string? input, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (DateTime.TryParseExact(input.Trim(), "yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeUniversal, out date))
            return true;

        return TryParse(input, out date, styles: DateTimeStyles.AssumeUniversal);
    }
}
