namespace Infrastructure.Services;

// Magic-number (file signature) validation. Defends against bypasses that
// rename or mislabel a file: a request can lie about the extension and the
// Content-Type header, but the leading bytes of the actual payload cannot.
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
        [FileKind.Jpeg] = new[]
        {
            new byte[] { 0xFF, 0xD8, 0xFF },
        },
        // PNG: 89 50 4E 47 0D 0A 1A 0A.
        [FileKind.Png] = new[]
        {
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
        },
        // PDF: %PDF-
        [FileKind.Pdf] = new[]
        {
            new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D },
        },
    };

    // Peek size must be >= the longest signature. 16 bytes is plenty.
    private const int PeekByteCount = 16;

    public static async Task<FileKind?> DetectAsync(
        Stream stream,
        IReadOnlyCollection<FileKind> allowed,
        CancellationToken cancellationToken = default)
    {
        if (allowed.Count == 0)
        {
            return null;
        }

        var buffer = new byte[PeekByteCount];
        var originalPosition = stream.CanSeek ? stream.Position : 0L;

        var totalRead = 0;
        while (totalRead < PeekByteCount)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, PeekByteCount - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        if (stream.CanSeek)
        {
            stream.Position = originalPosition;
        }

        foreach (var kind in allowed)
        {
            if (!Signatures.TryGetValue(kind, out var sigs))
            {
                continue;
            }

            foreach (var sig in sigs)
            {
                if (totalRead >= sig.Length && buffer.AsSpan(0, sig.Length).SequenceEqual(sig))
                {
                    return kind;
                }
            }
        }

        return null;
    }
}
