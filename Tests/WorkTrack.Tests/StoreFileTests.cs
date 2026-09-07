using System.Security.Cryptography;
using Application.Files.Commands;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Upload validation and persistence for <see cref="StoredFile"/>. The bytes now
/// land in SQL Server rather than Cloudinary, so this handler is the only place
/// that decides whether a payload is acceptable — what a caller declares about a
/// file is never trusted over what its leading bytes actually say.
/// </summary>
public class StoreFileTests
{
    private const string UploaderId = "u-1";

    // Real leading bytes for each accepted format, padded past the 100-byte floor
    // so size checks don't mask the signature checks under test.
    private static byte[] Png() => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    private static byte[] Jpeg() => Pad([0xFF, 0xD8, 0xFF, 0xE0]);
    private static byte[] Pdf() => Pad([0x25, 0x50, 0x44, 0x46, 0x2D]);

    private static byte[] Pad(byte[] signature, int totalLength = 256)
    {
        var buffer = new byte[totalLength];
        signature.CopyTo(buffer, 0);
        return buffer;
    }

    private static StoreFile.Handler Handler(AppDbContext db) => new(db);

    private static StoreFile.Command Command(
        byte[] content,
        string fileName,
        StoredFilePurpose purpose,
        string? declaredContentType = null) => new()
        {
            Content = content,
            FileName = fileName,
            DeclaredContentType = declaredContentType,
            Purpose = purpose,
            UploadedById = UploaderId,
        };

    // ── Signature validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_content_whose_bytes_are_not_a_recognised_format()
    {
        using var db = TestDb.Create();
        var content = Pad("this is plain text pretending to be a png"u8.ToArray());

        var result = await Handler(db).Handle(
            Command(content, "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    [Fact]
    public async Task Rejects_a_pdf_as_a_profile_image()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Pdf(), "cv.pdf", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    [Fact]
    public async Task Accepts_a_pdf_as_leave_evidence()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Pdf(), "sick-note.pdf", StoredFilePurpose.LeaveEvidence),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal("application/pdf", stored.ContentType);
    }

    [Fact]
    public async Task Accepts_a_jpeg_as_a_profile_image()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Jpeg(), "avatar.jpg", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal("image/jpeg", stored.ContentType);
    }

    // ── Declared vs detected ────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_a_declared_content_type_that_contradicts_the_bytes()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Png(), "avatar.png", StoredFilePurpose.ProfileImage, declaredContentType: "image/jpeg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    [Fact]
    public async Task Stores_the_detected_content_type_rather_than_a_vague_declared_one()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Png(), "avatar.png", StoredFilePurpose.ProfileImage, declaredContentType: "application/octet-stream"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal("image/png", stored.ContentType);
    }

    [Fact]
    public async Task Rejects_an_extension_outside_the_allow_list_even_when_the_bytes_are_valid()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Png(), "avatar.exe", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    // ── Size bounds ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_a_profile_image_over_the_five_megabyte_limit()
    {
        using var db = TestDb.Create();
        var oversized = Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], totalLength: 5 * 1024 * 1024 + 1);

        var result = await Handler(db).Handle(
            Command(oversized, "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    [Fact]
    public async Task Accepts_evidence_larger_than_the_profile_image_limit()
    {
        using var db = TestDb.Create();
        var sixMegabytes = Pad([0x25, 0x50, 0x44, 0x46, 0x2D], totalLength: 6 * 1024 * 1024);

        var result = await Handler(db).Handle(
            Command(sixMegabytes, "scan.pdf", StoredFilePurpose.LeaveEvidence),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Rejects_content_too_small_to_be_a_real_file()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.StoredFiles);
    }

    // ── Persistence detail ──────────────────────────────────────────────────────

    [Fact]
    public async Task Stores_the_sha256_of_the_content()
    {
        using var db = TestDb.Create();
        var content = Png();

        var result = await Handler(db).Handle(
            Command(content, "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(content)), stored.Sha256);
    }

    [Fact]
    public async Task Reduces_a_traversal_style_file_name_to_its_leaf()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Png(), @"..\..\..\windows\system32\evil.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal("evil.png", stored.FileName);
    }

    [Fact]
    public async Task Records_the_uploader_purpose_and_size()
    {
        using var db = TestDb.Create();
        var content = Png();

        var result = await Handler(db).Handle(
            Command(content, "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal(UploaderId, stored.UploadedById);
        Assert.Equal(StoredFilePurpose.ProfileImage, stored.Purpose);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(content, stored.Content);
    }

    [Fact]
    public async Task Returns_the_id_of_the_row_it_created()
    {
        using var db = TestDb.Create();

        var result = await Handler(db).Handle(
            Command(Png(), "avatar.png", StoredFilePurpose.ProfileImage),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(db.StoredFiles);
        Assert.Equal(stored.Id, result.Value);
    }
}
