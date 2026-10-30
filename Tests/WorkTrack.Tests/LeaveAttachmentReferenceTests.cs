using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Application.Files;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A leave's evidence and handover links are stored as the caller sent them, and
/// GetStoredFile grants access to a file by finding it on a leave the caller owns
/// (or covers). So a request naming somebody else's /api/files/{id} gave its filer
/// that file — a doctor's note, say, from an id seen once in a manager's view — and
/// a handover link could have another person's file mailed to the delegate. Any
/// non-blank text also satisfied a "document required" policy.
///
/// A new link now has to be a stored file of the right purpose that the filer or
/// the employee uploaded. A link the leave already carries passes unchanged, so an
/// edit of an older request keeps its Cloudinary URL.
/// </summary>
public class LeaveAttachmentReferenceTests
{
    private const string Emp = "u-emp";
    private const string Other = "u-other";
    private const string Delegate = "u-del";
    private const string LegacyUrl = "https://res.cloudinary.com/demo/image/upload/sick-note.pdf";

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Roles.Add(new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        foreach (var id in new[] { Emp, Other, Delegate, "u-mgr" })
        {
            db.Users.Add(new User { Id = id, UserName = id, Email = $"{id}@t.local", DisplayName = id });
            db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{id}", UserId = id, DepartmentId = 1, AnnualLeaveEntitlement = 20, LeaveBalance = 20 });
        }
        db.UserRoles.Add(new UserRole { UserId = "u-mgr", RoleId = "r-mgr" });
        db.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Sick Leave", IsActive = true, RequiresManagerApproval = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static string File(AppDbContext db, string uploader, StoredFilePurpose purpose)
    {
        var file = new StoredFile
        {
            Content = [1], FileName = "f.pdf", ContentType = "application/pdf", Sha256 = "h", SizeBytes = 1,
            Purpose = purpose, UploadedById = uploader,
        };
        db.StoredFiles.Add(file);
        db.SaveChanges();
        return StoredFilePath.For(file.Id);
    }

    private static Task<Result<string>> CreateAsync(AppDbContext db, string? evidence = null, string? handover = null) =>
        new CreateAnnualLeave.Handler(db, Mapper(), new FakeEmailService()).Handle(new CreateAnnualLeave.Command
        {
            RequestingUserId = Emp,
            AnnualLeave = new CreateAnnualLeaveRequest
            {
                EmployeeId = Emp, LeaveTypeId = 1, Reason = "Flu", DelegateId = Delegate,
                StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 5),
                EvidenceUrl = evidence, CoverageAttachmentUrl = handover,
            },
        }, CancellationToken.None);

    [Fact]
    public async Task Somebody_else_s_evidence_cannot_be_named_on_your_leave()
    {
        using var db = await WorldAsync();
        var theirs = File(db, Other, StoredFilePurpose.LeaveEvidence);

        var result = await CreateAsync(db, evidence: theirs);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveAttachmentRule.Message, result.Error);
        Assert.False(await db.AnnualLeaves.AnyAsync());
    }

    [Fact]
    public async Task A_file_of_another_purpose_is_not_evidence()
    {
        using var db = await WorldAsync();
        var photo = File(db, Emp, StoredFilePurpose.ProfileImage);

        var result = await CreateAsync(db, evidence: photo);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveAttachmentRule.Message, result.Error);
    }

    [Theory]
    [InlineData(LegacyUrl)]
    [InlineData("x")]
    [InlineData("/api/files/no-such-file")]
    public async Task Text_that_is_not_an_uploaded_file_is_not_evidence(string evidence)
    {
        using var db = await WorldAsync();

        var result = await CreateAsync(db, evidence: evidence);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveAttachmentRule.Message, result.Error);
    }

    [Fact]
    public async Task Somebody_else_s_file_cannot_be_the_handover()
    {
        using var db = await WorldAsync();
        var theirs = File(db, Other, StoredFilePurpose.CoverageHandover);

        var result = await CreateAsync(db, handover: theirs);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveAttachmentRule.Message, result.Error);
    }

    [Fact]
    public async Task Your_own_uploads_are_accepted()
    {
        using var db = await WorldAsync();
        var evidence = File(db, Emp, StoredFilePurpose.LeaveEvidence);
        var handover = File(db, Emp, StoredFilePurpose.CoverageHandover);

        var result = await CreateAsync(db, evidence: evidence, handover: handover);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.AnnualLeaves.AsNoTracking().SingleAsync();
        Assert.Equal(evidence, stored.EvidenceUrl);
        Assert.Equal(handover, stored.CoverageAttachmentUrl);
    }

    private static async Task SeedPendingAsync(AppDbContext db, string? evidence)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1", EmployeeId = Emp, EmployeeProfileId = $"p-{Emp}", DepartmentId = 1, LeaveTypeId = 1,
            StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 5), Reason = "Flu",
            DelegateId = Delegate, EvidenceUrl = evidence, Status = AnnualLeaveStatus.Pending, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<Result<MediatR.Unit>> EditAsync(AppDbContext db, string? evidence) =>
        new EditAnnualLeave.Handler(db, new FakeEmailService()).Handle(new EditAnnualLeave.Command
        {
            ChangedByUserId = Emp,
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1", LeaveTypeId = 1, Reason = "Flu, still", DelegateId = Delegate,
                StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 5), EvidenceUrl = evidence,
            },
        }, CancellationToken.None);

    [Fact]
    public async Task An_edit_keeps_a_link_the_leave_already_carries()
    {
        using var db = await WorldAsync();
        await SeedPendingAsync(db, LegacyUrl);

        var result = await EditAsync(db, LegacyUrl);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(LegacyUrl, (await db.AnnualLeaves.AsNoTracking().SingleAsync()).EvidenceUrl);
    }

    [Fact]
    public async Task An_edit_cannot_swap_in_somebody_else_s_file()
    {
        using var db = await WorldAsync();
        await SeedPendingAsync(db, evidence: null);
        var theirs = File(db, Other, StoredFilePurpose.LeaveEvidence);

        var result = await EditAsync(db, theirs);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveAttachmentRule.Message, result.Error);
        Assert.Null((await db.AnnualLeaves.AsNoTracking().SingleAsync()).EvidenceUrl);
    }
}
