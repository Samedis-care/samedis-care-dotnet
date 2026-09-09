using System.Text;

namespace SamedisCare.Helper.Text;

/// <summary>
/// Encoding detection for the tools' source files.
/// <para>
/// The exports these tools read come from German Excel and from in-house systems, so they
/// arrive as UTF-8 with or without a BOM, as UTF-16/32, or as Windows-1252. Assuming UTF-8
/// unconditionally turns every umlaut in a Windows-1252 file into a replacement character,
/// and the failure is silent — the import "succeeds" with corrupted names.
/// </para>
/// </summary>
public static class TextEncodings
{
    /// <summary>UTF-8 without a BOM on write.</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// UTF-8 that throws on invalid bytes. Used as the probe in <see cref="Detect"/>: a file
    /// that decodes cleanly as UTF-8 is UTF-8, and one that does not is treated as
    /// Windows-1252.
    /// </summary>
    public static readonly Encoding Utf8Strict =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Windows-1252, the usual encoding of a German Excel CSV export.
    /// </summary>
    /// <remarks>
    /// .NET Core ships only a handful of encodings, so the code-page provider has to be
    /// registered before 1252 can be resolved at all.
    /// </remarks>
    public static readonly Encoding Windows1252 = CreateWindows1252();

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Detects a text file's encoding: byte-order mark first, then a strict UTF-8 read of the
    /// whole file, and Windows-1252 when that fails.
    /// </summary>
    /// <remarks>
    /// The UTF-8 probe reads to the end on purpose. An invalid byte can sit anywhere, and a
    /// file whose first kilobyte is plain ASCII says nothing about the rest.
    /// </remarks>
    public static Encoding Detect(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Span<byte> bom = stackalloc byte[4];
        var read = stream.Read(bom);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Utf8;
        if (read >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return Encoding.UTF32;
        if (read >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;

        stream.Position = 0;
        try
        {
            using var probe = new StreamReader(stream, Utf8Strict,
                                               detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var buffer = new char[4096];
            while (probe.ReadBlock(buffer, 0, buffer.Length) > 0) { }
            return Utf8;
        }
        catch (DecoderFallbackException)
        {
            return Windows1252;
        }
    }
}
