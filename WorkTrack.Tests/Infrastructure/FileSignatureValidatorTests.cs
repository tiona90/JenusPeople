using Infrastructure.Services;
using Xunit;

namespace WorkTrack.Tests.Infrastructure;

public class FileSignatureValidatorTests
{
    private static readonly byte[] JpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
    private static readonly byte[] PngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
    private static readonly byte[] PdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
    private static readonly byte[] DocxZipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
    private static readonly byte[] ExeBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };

    private static readonly FileSignatureValidator.FileKind[] ProfileAllowed =
    {
        FileSignatureValidator.FileKind.Jpeg,
        FileSignatureValidator.FileKind.Png,
    };

    private static readonly FileSignatureValidator.FileKind[] EvidenceAllowed =
    {
        FileSignatureValidator.FileKind.Jpeg,
        FileSignatureValidator.FileKind.Png,
        FileSignatureValidator.FileKind.Pdf,
    };

    [Fact]
    public async Task Detects_Jpeg_When_Allowed()
    {
        using var stream = new MemoryStream(JpegBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, ProfileAllowed);
        Assert.Equal(FileSignatureValidator.FileKind.Jpeg, kind);
    }

    [Fact]
    public async Task Detects_Png_When_Allowed()
    {
        using var stream = new MemoryStream(PngBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, ProfileAllowed);
        Assert.Equal(FileSignatureValidator.FileKind.Png, kind);
    }

    [Fact]
    public async Task Detects_Pdf_When_Allowed()
    {
        using var stream = new MemoryStream(PdfBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, EvidenceAllowed);
        Assert.Equal(FileSignatureValidator.FileKind.Pdf, kind);
    }

    [Fact]
    public async Task Rejects_Pdf_For_Profile_Allow_List()
    {
        using var stream = new MemoryStream(PdfBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, ProfileAllowed);
        Assert.Null(kind);
    }

    [Fact]
    public async Task Rejects_Executable_Renamed_To_Image()
    {
        // .exe (MZ header) trying to masquerade as an image.
        using var stream = new MemoryStream(ExeBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, EvidenceAllowed);
        Assert.Null(kind);
    }

    [Fact]
    public async Task Rejects_Docx_When_Only_Pdf_And_Images_Allowed()
    {
        // DOCX shares the ZIP signature; deliberately not in the allow-list.
        using var stream = new MemoryStream(DocxZipBytes);
        var kind = await FileSignatureValidator.DetectAsync(stream, EvidenceAllowed);
        Assert.Null(kind);
    }

    [Fact]
    public async Task Rejects_Empty_Stream()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var kind = await FileSignatureValidator.DetectAsync(stream, EvidenceAllowed);
        Assert.Null(kind);
    }

    [Fact]
    public async Task Resets_Stream_Position_For_Subsequent_Upload()
    {
        using var stream = new MemoryStream(JpegBytes);
        stream.Position = 0;

        await FileSignatureValidator.DetectAsync(stream, ProfileAllowed);

        Assert.Equal(0, stream.Position);
    }
}
