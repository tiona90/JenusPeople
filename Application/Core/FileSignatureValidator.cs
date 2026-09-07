namespace Application.Core;

/// <summary>
/// Magic-number (file signature) validation. Defends against bypasses that
/// rename or mislabel a file: a request can lie about the extension and the
/// Content-Type header, but the leading bytes of the actual payload cannot.
///
/// Lives in Application rather than Infrastructure because the upload handler
/// that needs it is a MediatR command, and Application cannot see Infrastructure.
/// </summary>
public static class FileSignatureValidator
{
    public enum FileKind
    {
        Jpeg,
        Png,
        Pdf,
    }

    private static readonly Dictionary<FileKind, byte[][]> Signatures = new()
    {
        // JPEG: FF D8 FF, with the 4th byte varying across JFIF / EXIF / SPIFF.
        [FileKind.Jpeg] = [[0xFF, 0xD8, 0xFF]],
        // PNG: 89 50 4E 47 0D 0A 1A 0A.
        [FileKind.Png] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        // PDF: %PDF-
        [FileKind.Pdf] = [[0x25, 0x50, 0x44, 0x46, 0x2D]],
    };

    private static readonly Dictionary<FileKind, string> ContentTypes = new()
    {
        [FileKind.Jpeg] = "image/jpeg",
        [FileKind.Png] = "image/png",
        [FileKind.Pdf] = "application/pdf",
    };

    private static readonly Dictionary<FileKind, string[]> Extensions = new()
    {
        [FileKind.Jpeg] = [".jpg", ".jpeg"],
        [FileKind.Png] = [".png"],
        [FileKind.Pdf] = [".pdf"],
    };

    /// <summary>
    /// The kind whose signature the content actually starts with, or null when it
    /// matches nothing known. Deliberately scans every known kind rather than
    /// only the ones a caller will accept, so the caller can tell "that is not a
    /// file we recognise" apart from "that is a real PDF, but not here".
    /// </summary>
    public static FileKind? Detect(ReadOnlySpan<byte> content)
    {
        foreach (var (kind, signatures) in Signatures)
        {
            foreach (var signature in signatures)
            {
                if (content.Length >= signature.Length
                    && content[..signature.Length].SequenceEqual(signature))
                {
                    return kind;
                }
            }
        }

        return null;
    }

    /// <summary>The content type to serve this kind back as.</summary>
    public static string ContentTypeFor(FileKind kind) => ContentTypes[kind];

    /// <summary>File extensions legitimately carried by this kind.</summary>
    public static IReadOnlyList<string> ExtensionsFor(FileKind kind) => Extensions[kind];

    /// <summary>
    /// The kind a declared content type claims to be, or null when the value is
    /// absent or too vague to contradict anything (<c>application/octet-stream</c>
    /// is what browsers send when they cannot work out a type themselves).
    /// </summary>
    public static FileKind? KindForContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        foreach (var (kind, value) in ContentTypes)
        {
            if (value.Equals(contentType, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }
}
