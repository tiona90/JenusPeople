using Application.Core;
using Application.Files;
using Application.Files.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Read authorization for stored files. While these lived on Cloudinary they sat
/// behind unauthenticated public URLs — anyone holding the link could open a
/// colleague's sick note. Serving them through the API means access can finally
/// be scoped, and leave evidence follows the same rules as the leave itself.
/// </summary>
public class GetStoredFileTests
{
    private const string OwnerId = "employee-1";
    private const string ManagerUserId = "mgr";
    private const string ManagerProfileId = "mp";

    private static readonly byte[] Content = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];

    private static StoredFile SeedFile(AppDbContext db, StoredFilePurpose purpose, string uploaderId)
    {
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString(),
            Content = Content,
            FileName = "sick-note.pdf",
            ContentType = "application/pdf",
            Sha256 = "hash",
            SizeBytes = Content.Length,
            Purpose = purpose,
            UploadedById = uploaderId,
        };
        db.StoredFiles.Add(file);
        db.SaveChanges();
        return file;
    }

    /// <summary>Attaches a file to a leave the way the apply-leave flow does.</summary>
    private static void SeedLeaveReferencing(AppDbContext db, StoredFile file, string employeeId, int departmentId)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            DepartmentId = departmentId,
            EvidenceUrl = StoredFilePath.For(file.Id),
            Status = AnnualLeaveStatus.Pending,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 3),
        });
        db.SaveChanges();
    }

    private static void SeedManagerOfDepartment(AppDbContext db, int departmentId)
    {
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ManagerProfileId,
            UserId = ManagerUserId,
            DepartmentId = departmentId,
        });
        db.SaveChanges();
    }

    private static Task<Result<StoredFileDto>> Read(
        AppDbContext db,
        string fileId,
        string userId,
        bool isAdmin = false,
        bool isManager = false) =>
        new GetStoredFile.Handler(db).Handle(
            new GetStoredFile.Query
            {
                Id = fileId,
                RequestingUserId = userId,
                IsAdmin = isAdmin,
                IsManager = isManager,
            },
            CancellationToken.None);

    // ── Profile images: visible to any signed-in colleague ──────────────────────

    [Fact]
    public async Task Profile_image_is_readable_by_another_authenticated_user()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.ProfileImage, OwnerId);

        var result = await Read(db, file.Id, "some-other-employee");

        Assert.True(result.IsSuccess, result.Error);
    }

    // ── Evidence: scoped like the leave it belongs to ───────────────────────────

    [Fact]
    public async Task Evidence_is_readable_by_the_employee_who_uploaded_it()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);

        var result = await Read(db, file.Id, OwnerId);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Evidence_is_readable_by_an_admin()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);

        var result = await Read(db, file.Id, "admin-1", isAdmin: true);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Evidence_is_readable_by_a_manager_of_the_leaves_department()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);
        SeedManagerOfDepartment(db, departmentId: 1);

        var result = await Read(db, file.Id, ManagerUserId, isManager: true);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Evidence_is_not_readable_by_a_manager_of_a_different_department()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 2);
        SeedManagerOfDepartment(db, departmentId: 1);

        var result = await Read(db, file.Id, ManagerUserId, isManager: true);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Evidence_is_not_readable_by_an_unrelated_employee()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);

        var result = await Read(db, file.Id, "nosy-colleague");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Evidence_is_readable_by_the_employee_the_leave_is_for_even_when_an_admin_uploaded_it()
    {
        using var db = TestDb.Create();
        // An Admin may create leave on behalf of someone else, which makes the
        // Admin the uploader — the employee it concerns must still see it.
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, uploaderId: "admin-1");
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);

        var result = await Read(db, file.Id, OwnerId);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Evidence_not_yet_attached_to_a_leave_is_still_readable_by_its_uploader()
    {
        using var db = TestDb.Create();
        // The apply-leave flow uploads evidence before the leave row exists.
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);

        var result = await Read(db, file.Id, OwnerId);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Evidence_not_yet_attached_to_a_leave_is_not_readable_by_a_manager()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedManagerOfDepartment(db, departmentId: 1);

        var result = await Read(db, file.Id, ManagerUserId, isManager: true);

        Assert.False(result.IsSuccess);
    }

    // ── No existence leak ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_refused_file_is_reported_as_missing_not_forbidden()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);
        SeedLeaveReferencing(db, file, OwnerId, departmentId: 1);

        var result = await Read(db, file.Id, "nosy-colleague");

        Assert.False(result.IsSuccess);
        // Deliberately NotFound: 403 would confirm the id names a real file.
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task An_unknown_id_is_reported_as_missing()
    {
        using var db = TestDb.Create();

        var result = await Read(db, "no-such-file", OwnerId, isAdmin: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    // ── What the caller gets back ───────────────────────────────────────────────

    [Fact]
    public async Task Returns_the_bytes_and_metadata_needed_to_serve_the_file()
    {
        using var db = TestDb.Create();
        var file = SeedFile(db, StoredFilePurpose.LeaveEvidence, OwnerId);

        var result = await Read(db, file.Id, OwnerId);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        Assert.Equal(Content, dto.Content);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal("sick-note.pdf", dto.FileName);
        Assert.Equal("hash", dto.Sha256);
    }
}
