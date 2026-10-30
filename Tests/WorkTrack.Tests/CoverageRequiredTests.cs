using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Validators;
using Application.Core;
using Application.Files;
using Application.Files.Queries;
using AutoMapper;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Coverage is mandatory for an Employee and a Manager, and what they hand over
/// to the delegate travels with the nomination.
///
/// The delegate used to be optional and told nothing beyond the dates. Now a
/// request from either role must name one (<see cref="CoverageRule"/>), the
/// employee can leave them a note and a document, and both reach the delegate in
/// the coverage email — the note as text and the document as a file — and nobody
/// else: the department's round-robin message stays as it was.
/// </summary>
public class CoverageRequiredTests
{
    private const int DepartmentId = 4;
    private const int LeaveTypeId = 7;
    private const string EmployeeUserId = "u-employee";
    private const string EmployeeProfileId = "p-employee";
    private const string DelegateUserId = "u-delegate";
    private const string ColleagueUserId = "u-colleague";
    private const string AdminUserId = "u-admin";
    private const string ManagerUserId = "u-manager";
    private const string DelegateEmail = "andreas@test.local";
    private const string ColleagueEmail = "petros@test.local";

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];

    private static IMapper BuildMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static async Task<AppDbContext> SeedWorldAsync(bool requiresApproval = false)
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.SystemAdministrator, NormalizedName = "ADMIN" };
        var managerRole = new Role { Id = "r-manager", Name = AppRoles.Manager, NormalizedName = "MANAGER" };
        var employeeRole = new Role { Id = "r-employee", Name = AppRoles.Employee, NormalizedName = "EMPLOYEE" };
        db.Roles.AddRange(adminRole, managerRole, employeeRole);

        AddPerson(db, EmployeeUserId, EmployeeProfileId, "Maria Ioannou", "maria@test.local", DepartmentId, employeeRole);
        AddPerson(db, DelegateUserId, "p-delegate", "Andreas Georgiou", DelegateEmail, DepartmentId, employeeRole);
        AddPerson(db, ColleagueUserId, "p-colleague", "Petros Christou", ColleagueEmail, DepartmentId, employeeRole);
        AddPerson(db, ManagerUserId, "p-manager", "Nikos Manager", "nikos@test.local", DepartmentId, managerRole);
        AddPerson(db, AdminUserId, "p-admin", "Chris System Administrator", "admin@test.local", null, adminRole);

        db.LeaveTypes.Add(new LeaveType
        {
            Id = LeaveTypeId,
            Name = "Annual Leave",
            IsActive = true,
            AffectsBalance = true,
            RequiresManagerApproval = requiresApproval,
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static void AddPerson(
        AppDbContext db, string userId, string profileId, string name, string email, int? departmentId, Role role)
    {
        db.Users.Add(new User { Id = userId, UserName = email, Email = email, DisplayName = name });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = profileId,
            UserId = userId,
            DepartmentId = departmentId,
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
    }

    private static CreateAnnualLeave.Command CreateCmd(
        string employeeId, string? delegateId, string? note = null, string? attachmentUrl = null) => new()
    {
        AnnualLeave = new CreateAnnualLeaveRequest
        {
            EmployeeId = employeeId,
            LeaveTypeId = LeaveTypeId,
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
            Reason = "Family trip",
            DelegateId = delegateId,
            CoverageNote = note,
            CoverageAttachmentUrl = attachmentUrl,
        },
    };

    private static async Task<List<string>> ValidateCreate(AppDbContext db, CreateAnnualLeave.Command cmd)
    {
        var result = await new CreateAnnualLeaveRequestValidator(db).ValidateAsync(cmd);
        return result.Errors.Select(e => e.ErrorMessage).ToList();
    }

    // ── The rule ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_employee_filing_without_a_delegate_is_refused()
    {
        await using var db = await SeedWorldAsync();

        var errors = await ValidateCreate(db, CreateCmd(EmployeeUserId, delegateId: null));

        Assert.Contains(CoverageRule.RequiredMessage, errors);
    }

    [Fact]
    public async Task A_blank_delegate_counts_as_none()
    {
        await using var db = await SeedWorldAsync();

        var errors = await ValidateCreate(db, CreateCmd(EmployeeUserId, delegateId: "   "));

        Assert.Contains(CoverageRule.RequiredMessage, errors);
    }

    [Fact]
    public async Task A_manager_filing_without_a_delegate_is_refused()
    {
        await using var db = await SeedWorldAsync();

        var errors = await ValidateCreate(db, CreateCmd(ManagerUserId, delegateId: null));

        Assert.Contains(CoverageRule.RequiredMessage, errors);
    }

    [Fact]
    public async Task An_admin_filing_their_own_leave_needs_no_delegate()
    {
        await using var db = await SeedWorldAsync();

        var errors = await ValidateCreate(db, CreateCmd(AdminUserId, delegateId: null));

        Assert.DoesNotContain(CoverageRule.RequiredMessage, errors);
    }

    [Fact]
    public async Task An_employee_naming_a_delegate_passes()
    {
        await using var db = await SeedWorldAsync();

        var errors = await ValidateCreate(db, CreateCmd(EmployeeUserId, DelegateUserId));

        Assert.DoesNotContain(CoverageRule.RequiredMessage, errors);
    }

    [Fact]
    public async Task Editing_an_employees_request_to_drop_the_delegate_is_refused()
    {
        await using var db = await SeedWorldAsync();
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1",
            EmployeeId = EmployeeUserId,
            EmployeeProfileId = EmployeeProfileId,
            LeaveTypeId = LeaveTypeId,
            Status = AnnualLeaveStatus.Pending,
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
            DelegateId = DelegateUserId,
        });
        await db.SaveChangesAsync();

        var result = await new EditAnnualLeaveRequestValidator(db).ValidateAsync(new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1",
                LeaveTypeId = LeaveTypeId,
                StartDate = new DateTime(2024, 3, 4),
                EndDate = new DateTime(2024, 3, 8),
                Reason = "test",
                DelegateId = null,
            },
            ChangedByUserId = AdminUserId,
            IsAdmin = true,
        });

        // No exemption for an admin editing on somebody's behalf: the rule is about
        // whose leave it is, not who is typing.
        Assert.Contains(CoverageRule.RequiredMessage, result.Errors.Select(e => e.ErrorMessage));
    }

    // ── The handover ────────────────────────────────────────────────────────────

    private static StoredFile SeedHandoverFile(AppDbContext db, string uploaderId)
    {
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString(),
            Content = PdfBytes,
            FileName = "handover.pdf",
            ContentType = "application/pdf",
            Sha256 = "hash",
            SizeBytes = PdfBytes.Length,
            Purpose = StoredFilePurpose.CoverageHandover,
            UploadedById = uploaderId,
        };
        db.StoredFiles.Add(file);
        db.SaveChanges();
        return file;
    }

    [Fact]
    public async Task The_delegate_gets_the_note_and_the_document_and_the_department_gets_neither()
    {
        await using var db = await SeedWorldAsync(requiresApproval: false);
        var file = SeedHandoverFile(db, EmployeeUserId);
        var mail = new FakeEmailService();

        var result = await new CreateAnnualLeave.Handler(db, BuildMapper(), mail).Handle(
            CreateCmd(EmployeeUserId, DelegateUserId,
                note: "Client X calls on Tuesday; the deck is on the shared drive.",
                attachmentUrl: StoredFilePath.For(file.Id)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var toDelegate = Assert.Single(mail.Sent, sent => sent.Recipient == DelegateEmail);
        Assert.Contains("Client X calls on Tuesday", toDelegate.TextBody!, StringComparison.Ordinal);
        var attachment = Assert.Single(toDelegate.Attachments);
        Assert.Equal("handover.pdf", attachment.FileName);
        Assert.Equal(PdfBytes, attachment.Content);

        var toColleague = Assert.Single(mail.Sent, sent => sent.Recipient == ColleagueEmail);
        Assert.DoesNotContain("Client X", toColleague.TextBody!, StringComparison.Ordinal);
        Assert.Empty(toColleague.Attachments);
    }

    [Fact]
    public async Task The_note_is_stored_with_the_leave_and_a_blank_one_is_stored_as_null()
    {
        await using var db = await SeedWorldAsync(requiresApproval: true);
        var mail = new FakeEmailService();

        var result = await new CreateAnnualLeave.Handler(db, BuildMapper(), mail).Handle(
            CreateCmd(EmployeeUserId, DelegateUserId, note: "  Keep an eye on the deploy.  ", attachmentUrl: "   "),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = db.AnnualLeaves.Single(al => al.Id == result.Value);
        Assert.Equal("Keep an eye on the deploy.", stored.CoverageNote);
        Assert.Null(stored.CoverageAttachmentUrl);
    }

    /// <summary>
    /// A link can only be saved naming a file that exists (LeaveAttachmentRule),
    /// so "missing" means it went after filing — the request is approved later and
    /// the file is no longer there.
    /// </summary>
    [Fact]
    public async Task A_missing_handover_file_does_not_stop_the_email()
    {
        await using var db = await SeedWorldAsync(requiresApproval: true);
        var mail = new FakeEmailService();
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-gone", EmployeeId = EmployeeUserId, EmployeeProfileId = EmployeeProfileId,
            DepartmentId = DepartmentId, LeaveTypeId = LeaveTypeId, Status = AnnualLeaveStatus.Pending,
            StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 8), Reason = "Family trip",
            DelegateId = DelegateUserId, CoverageNote = "Note only", CoverageAttachmentUrl = StoredFilePath.For("no-such-file"),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new UpdateLeaveStatus.Handler(db, mail).Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = "L-gone", ChangedByUserId = ManagerUserId, IsManager = true,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var toDelegate = Assert.Single(mail.Sent, sent => sent.Recipient == DelegateEmail);
        Assert.Contains("Note only", toDelegate.TextBody!, StringComparison.Ordinal);
        Assert.Empty(toDelegate.Attachments);
    }

    // ── Who may open the handover file ──────────────────────────────────────────

    private static Task<Result<StoredFileDto>> Read(
        AppDbContext db, string fileId, string userId, bool isAdmin = false, bool isManager = false) =>
        new GetStoredFile.Handler(db).Handle(
            new GetStoredFile.Query { Id = fileId, RequestingUserId = userId, IsAdmin = isAdmin, IsManager = isManager },
            CancellationToken.None);

    private static void SeedLeaveWithHandover(AppDbContext db, StoredFile file)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-h",
            EmployeeId = EmployeeUserId,
            EmployeeProfileId = EmployeeProfileId,
            DepartmentId = DepartmentId,
            LeaveTypeId = LeaveTypeId,
            Status = AnnualLeaveStatus.Approved,
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
            DelegateId = DelegateUserId,
            CoverageAttachmentUrl = StoredFilePath.For(file.Id),
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task The_delegate_may_open_the_handover_file()
    {
        await using var db = await SeedWorldAsync();
        var file = SeedHandoverFile(db, EmployeeUserId);
        SeedLeaveWithHandover(db, file);

        var result = await Read(db, file.Id, DelegateUserId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_bystander_in_the_same_department_may_not()
    {
        await using var db = await SeedWorldAsync();
        var file = SeedHandoverFile(db, EmployeeUserId);
        SeedLeaveWithHandover(db, file);

        var result = await Read(db, file.Id, ColleagueUserId);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task The_delegate_may_not_open_the_leaves_evidence_by_the_same_token()
    {
        await using var db = await SeedWorldAsync();
        var evidence = new StoredFile
        {
            Id = Guid.NewGuid().ToString(),
            Content = PdfBytes,
            FileName = "sick-note.pdf",
            ContentType = "application/pdf",
            Sha256 = "hash",
            SizeBytes = PdfBytes.Length,
            Purpose = StoredFilePurpose.LeaveEvidence,
            UploadedById = EmployeeUserId,
        };
        db.StoredFiles.Add(evidence);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-e",
            EmployeeId = EmployeeUserId,
            DepartmentId = DepartmentId,
            Status = AnnualLeaveStatus.Approved,
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
            DelegateId = DelegateUserId,
            EvidenceUrl = StoredFilePath.For(evidence.Id),
        });
        db.SaveChanges();

        var result = await Read(db, evidence.Id, DelegateUserId);

        Assert.False(result.IsSuccess);
    }
}
