using SamedisCare.Helper.Text;

namespace SamedisCare.Helper.IO;

/// <summary>File inspection shared by the sync tools.</summary>
public static class Files
{
    /// <summary>
    /// True when a file carries no data: missing, zero bytes, a byte-order mark and nothing
    /// else, or whitespace only.
    /// </summary>
    /// <remarks>
    /// The BOM-only case is the one worth having: an exporter that had nothing to write still
    /// creates a three-byte file, and treating that as a malformed import produces a daily
    /// ERROR for a situation that is entirely normal.
    /// </remarks>
    public static bool IsEffectivelyEmpty(string filePath)
    {
        if (!File.Exists(filePath)) return true;

        var info = new FileInfo(filePath);
        if (info.Length == 0) return true;

        if (info.Length <= 4)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0) return true;
                if (bytes.Length == 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return true;
                if (bytes.Length == 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
                                          (bytes[0] == 0xFE && bytes[1] == 0xFF))) return true;
            }
            catch (IOException)
            {
                // Unreadable is not the same as empty — let the caller's own read fail loudly.
                return false;
            }
        }

        try
        {
            using var reader = new StreamReader(filePath, TextEncodings.Detect(filePath),
                                                detectEncodingFromByteOrderMarks: true);
            return string.IsNullOrWhiteSpace(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Works out a file extension from whatever the source offers: the file name, then the
    /// path of a URL, then the MIME type.
    /// </summary>
    /// <param name="name">A file name, if one was supplied.</param>
    /// <param name="mimeType">A MIME type, consulted when name and URL carry no extension.</param>
    /// <param name="url">A URL whose path may carry the extension.</param>
    /// <param name="fallback">
    /// What to return when nothing yields an extension. Required rather than defaulted: the
    /// implementation this replaces hardcoded <c>.pdf</c>, which is right for a document
    /// download and wrong for anything else.
    /// </param>
    public static string Extension(string? name, string? mimeType, string? url, string fallback)
    {
        var ext = string.IsNullOrEmpty(name) ? string.Empty : Path.GetExtension(name);

        if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            ext = Path.GetExtension(uri.AbsolutePath);

        if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(mimeType))
            ext = MimeExtension(mimeType);

        return string.IsNullOrEmpty(ext) ? fallback : ext;
    }

    private static string MimeExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "image/png"       => ".png",
        "image/jpeg"      => ".jpg",
        "image/gif"       => ".gif",
        _                 => string.Empty
    };
}
