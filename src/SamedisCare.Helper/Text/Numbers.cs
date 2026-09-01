using System.Globalization;

namespace SamedisCare.Helper.Text;

/// <summary>
/// Parsing and formatting of decimal numbers against a configured separator.
/// <para>
/// The tools read numbers written by German source systems, where the decimal separator is a
/// comma and the group separator a dot — the opposite of the invariant culture. Which one
/// applies is a per-installation setting, so it travels as an instance rather than as
/// process-wide state: the implementation this replaces held it in a mutable static, so the
/// meaning of every later parse depended on whether some unrelated code had already assigned
/// it.
/// </para>
/// </summary>
public sealed class NumberFormat
{
    /// <summary>German convention: <c>1.234,56</c>.</summary>
    public static NumberFormat Comma { get; } = new(',');

    /// <summary>Invariant convention: <c>1,234.56</c>.</summary>
    public static NumberFormat Dot { get; } = new('.');

    /// <param name="decimalSeparator">Either <c>','</c> or <c>'.'</c>.</param>
    /// <exception cref="ArgumentException">Any other character.</exception>
    public NumberFormat(char decimalSeparator)
    {
        if (decimalSeparator is not (',' or '.'))
            throw new ArgumentException("The decimal separator must be ',' or '.'.",
                                        nameof(decimalSeparator));

        DecimalSeparator = decimalSeparator;
        Info = new NumberFormatInfo
        {
            NumberDecimalSeparator   = decimalSeparator.ToString(),
            NumberGroupSeparator     = decimalSeparator == ',' ? "." : ",",
            CurrencyDecimalSeparator = decimalSeparator.ToString(),
            CurrencyGroupSeparator   = decimalSeparator == ',' ? "." : ",",
        };
    }

    /// <summary>
    /// Builds a format from a configuration string, falling back to
    /// <see cref="Comma"/> when it is empty.
    /// </summary>
    public static NumberFormat FromSetting(string? decimalSeparator)
        => string.IsNullOrEmpty(decimalSeparator) ? Comma : new NumberFormat(decimalSeparator[0]);

    public char DecimalSeparator { get; }

    /// <summary>The underlying format, for callers that pass it to .NET APIs directly.</summary>
    public NumberFormatInfo Info { get; }

    public bool TryParseDecimal(string? value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, Info, out result);

    /// <summary>Formats with the configured separator; two decimals by default.</summary>
    public string Format(decimal value, string format = "F2")
        => value.ToString(format, Info);
}
