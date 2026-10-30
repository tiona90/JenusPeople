using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Application.Files;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// <see cref="LeaveType.AttachmentPolicy"/> used to be display-only: the admin
/// dialog saved it and the leave type's card rendered it, and nothing in the
/// request path ever read it. These tests pin it as an enforced rule — a type set
/// to <see cref="AttachmentPolicy.Required"/> cannot be <em>approved</em> while
/// the request carries no evidence.
///
/// Approval, not filing. The rule first refused at filing time, and that refused
/// exactly the request Military Leave exists for: call-up papers are dated the day
/// of service, so the request has to be made before the document exists. An
/// employee now files without one, attaches it later from My Leave, and only then
/// can a manager approve. The one place filing still refuses is a type that
/// auto-approves, because there filing <em>is</em> approval.
///
/// Four things the tests exist to hold still:
///
/// <list type="bullet">
/// <item><description>
/// Only <c>Required</c> blocks. <c>Optional</c> is the encouragement the client
/// renders in amber and <c>None</c> is silence; neither may ever refuse a request,
/// or an admin nudging a type towards documentation would lock employees out of it.
/// </description></item>
/// <item><description>
/// Every path into <c>Approved</c> is gated — the approve button
/// (<see cref="UpdateLeaveStatus"/>), the admin's edit dialog
/// (<see cref="EditAnnualLeave"/> with a status), and an auto-approving type's
/// creation (<see cref="CreateAnnualLeave"/>). Leaving a request Pending, or
/// moving it to Rejected, never asks for a document.
/// </description></item>
/// <item><description>
/// There is no exemption at approval. An admin approving on somebody's behalf is
/// refused like a manager — the rule is about the leave type, not about who is
/// clicking. But an edit that keeps a request Pending is not an approval, so an
/// admin fixing the reason on an undocumented request is no longer refused.
/// </description></item>
/// <item><description>
/// Whitespace is not an attachment. <see cref="AnnualLeave.EvidenceUrl"/> is a
/// free-text column, so the check trims before believing it.
/// </description></item>
/// </list>
/// </summary>
public class AttachmentPolicyEnforcementTests
{
    private const string UserId = "employee-1";
    private const string AdminId = "admin-1";
    private const string ProfileId = "profile-1";
    private const int DepartmentId = 1;
    private const string EvidenceUrl = "/api/files/8f2c1b6e-0000-4000-8000-000000000001";

    private const int RequiredTypeId = 1;
    private const int OptionalTypeId = 2;
    private const int NoneTypeId = 3;
    private const int AutoApprovedRequiredTypeId = 4;

    private const string ApprovalRefusal =
        "Evidence Leave requires a supporting document before it can be approved. Attach one first.";

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });

        db.Users.Add(new User
        {
            Id = UserId,
            UserName = "employee-1@example.com",
            Email = "employee-1@example.com",
            DisplayName = "Andreas Georgiou",
        });

        db.Users.Add(new User
        {
            Id = AdminId,
            UserName = "admin-1@example.com",
            Email = "admin-1@example.com",
            DisplayName = "HR Administrator",
        });

        db.Departments.Add(new Department { Id = DepartmentId, Name = "Ops", Code = "OPS" });

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId,
            UserId = UserId,
            DepartmentId = DepartmentId,
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });

        // IsAdmin on UpdateLeaveStatus/EditAnnualLeave is now the HR Administrator
        // acting on somebody's behalf, scoped to their assigned departments — this
        // caller needs a UserDepartment row over the employee's department to reach
        // the leave under test at all.
        db.UserDepartments.Add(new UserDepartment { UserId = AdminId, DepartmentId = DepartmentId });

        // The file EvidenceUrl names, uploaded by the employee: a link has to name a
        // stored evidence file the filer or the employee uploaded (LeaveAttachmentRule).
        db.StoredFiles.Add(new StoredFile
        {
            Id = StoredFilePath.TryParseId(EvidenceUrl)!, Content = [1], FileName = "note.pdf",
            ContentType = "application/pdf", Sha256 = "h", SizeBytes = 1,
            Purpose = StoredFilePurpose.LeaveEvidence, UploadedById = UserId,
        });

        // Named for its policy rather than "Sick Leave": the rule has to follow the
        // admin's setting, not a word in the type's name, which is what the apply
        // page used to sniff for.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = RequiredTypeId,
            Name = "Evidence Leave",
            IsActive = true,
            RequiresManagerApproval = true,
            AffectsBalance = false,
            AttachmentPolicy = AttachmentPolicy.Required,
            DefaultAllowance = 10,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = OptionalTypeId,
            Name = "Encouraged Leave",
            IsActive = true,
            RequiresManagerApproval = true,
            AffectsBalance = false,
            AttachmentPolicy = AttachmentPolicy.Optional,
            DefaultAllowance = 10,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = NoneTypeId,
            Name = "Annual Leave",
            IsActive = true,
            RequiresManagerApproval = true,
            AffectsBalance = true,
            AttachmentPolicy = AttachmentPolicy.None,
            DefaultAllowance = 25,
        });

        // Filing is approval here, so filing is where the document is asked for.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = AutoApprovedRequiredTypeId,
            Name = "Evidence Leave",
            IsActive = true,
            RequiresManagerApproval = false,
            AffectsBalance = false,
            AttachmentPolicy = AttachmentPolicy.Required,
            DefaultAllowance = 10,
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static IMapper BuildMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<string>> Create(AppDbContext db, int leaveTypeId, string? evidenceUrl) =>
        new CreateAnnualLeave.Handler(db, BuildMapper(), new FakeEmailService())
            .Handle(new CreateAnnualLeave.Command
            {
                AnnualLeave = new CreateAnnualLeaveRequest
                {
                    EmployeeId = UserId,
                    LeaveTypeId = leaveTypeId,
                    StartDate = new DateTime(2026, 6, 1),
                    EndDate = new DateTime(2026, 6, 5),
                    Reason = "Out of office",
                    EvidenceUrl = evidenceUrl,
                },
            }, CancellationToken.None);

    /// <summary>
    /// Writes an existing request directly, bypassing the create path. The default
    /// status is the HR stage: the decisions below are made by the HR Administrator,
    /// and a Pending row on a type that asks for the manager is the manager's, not
    /// theirs (<see cref="ApprovalStageRule.WithManagerMessage"/>). Tests that edit
    /// as the employee pass Pending explicitly, since an employee cannot edit a row
    /// that is with HR.
    /// </summary>
    private static async Task SeedLeaveAsync(
        AppDbContext db, int leaveTypeId, string? evidenceUrl, AnnualLeaveStatus status = AnnualLeaveStatus.AwaitingHrApproval)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1",
            EmployeeId = UserId,
            EmployeeProfileId = ProfileId,
            DepartmentId = DepartmentId,
            LeaveTypeId = leaveTypeId,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 5),
            Reason = "Out of office",
            EvidenceUrl = evidenceUrl,
            Status = status,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<Result<Unit>> Edit(
        AppDbContext db, int leaveTypeId, string? evidenceUrl, bool isAdmin = false, AnnualLeaveStatus? status = null) =>
        new EditAnnualLeave.Handler(db, new FakeEmailService())
            .Handle(new EditAnnualLeave.Command
            {
                ChangedByUserId = isAdmin ? AdminId : UserId,
                IsAdmin = isAdmin,
                AnnualLeave = new EditAnnualLeaveRequest
                {
                    Id = "L1",
                    LeaveTypeId = leaveTypeId,
                    StartDate = new DateTime(2026, 6, 8),
                    EndDate = new DateTime(2026, 6, 12),
                    Reason = "Rebooked",
                    EvidenceUrl = evidenceUrl,
                    Status = status,
                },
            }, CancellationToken.None);

    private static Task<Result<Unit>> SetStatus(AppDbContext db, AnnualLeaveStatus status) =>
        new UpdateLeaveStatus.Handler(db, new FakeEmailService())
            .Handle(new UpdateLeaveStatus.Command
            {
                LeaveId = "L1",
                ChangedByUserId = AdminId,
                IsAdmin = true,
                Request = new UpdateLeaveStatusRequest { Status = status },
            }, CancellationToken.None);

    private static async Task<AnnualLeave> StoredLeaveAsync(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        return await db.AnnualLeaves.AsNoTracking().SingleAsync();
    }

    // ── Filing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Military Leave case: the document is dated the day of service, so the
    /// request has to go in before it exists. Filing is allowed; approval waits.
    /// </summary>
    [Fact]
    public async Task A_type_needing_approval_accepts_filing_without_the_document_it_requires()
    {
        await using var db = await WorldAsync();

        var result = await Create(db, RequiredTypeId, evidenceUrl: null);

        Assert.True(result.IsSuccess);
        var leave = Assert.Single(await db.AnnualLeaves.ToListAsync());
        // Open, not approved. This world has no manager, so the request goes
        // straight to HR rather than waiting Pending on nobody.
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, leave.Status);
        Assert.Null(leave.EvidenceUrl);
    }

    /// <summary>Filing is approval for an auto-approving type, so filing is gated.</summary>
    [Fact]
    public async Task An_auto_approving_type_refuses_filing_without_the_document_it_requires()
    {
        await using var db = await WorldAsync();

        var result = await Create(db, AutoApprovedRequiredTypeId, evidenceUrl: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalRefusal, result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task An_auto_approving_type_accepts_filing_with_the_document_it_requires()
    {
        await using var db = await WorldAsync();

        var result = await Create(db, AutoApprovedRequiredTypeId, EvidenceUrl);

        Assert.True(result.IsSuccess);
        var leave = Assert.Single(await db.AnnualLeaves.ToListAsync());
        Assert.Equal(AnnualLeaveStatus.Approved, leave.Status);
        Assert.Equal(EvidenceUrl, leave.EvidenceUrl);
    }

    [Fact]
    public async Task An_encouraged_attachment_never_blocks_a_request()
    {
        await using var db = await WorldAsync();

        var result = await Create(db, OptionalTypeId, evidenceUrl: null);

        Assert.True(result.IsSuccess);
        Assert.Single(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task A_type_needing_no_attachment_never_blocks_a_request()
    {
        await using var db = await WorldAsync();

        var result = await Create(db, NoneTypeId, evidenceUrl: null);

        Assert.True(result.IsSuccess);
        Assert.Single(await db.AnnualLeaves.ToListAsync());
    }

    // ── The approve button ────────────────────────────────────────────────────

    [Fact]
    public async Task Approval_is_refused_while_the_document_is_missing()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null);

        var result = await SetStatus(db, AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalRefusal, result.Error);
        var leave = await StoredLeaveAsync(db);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, leave.Status);
        Assert.Null(leave.ApprovedAt);
    }

    /// <summary>
    /// EvidenceUrl is free text, so a blank string would otherwise satisfy a
    /// required policy while pointing at nothing.
    /// </summary>
    [Fact]
    public async Task Whitespace_is_not_an_attachment()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, "   ");

        var result = await SetStatus(db, AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredLeaveAsync(db)).Status);
    }

    [Fact]
    public async Task Approval_goes_through_once_the_document_is_attached()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, EvidenceUrl);

        var result = await SetStatus(db, AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredLeaveAsync(db)).Status);
    }

    /// <summary>The rule is about approving; declining an undocumented request is fine.</summary>
    [Fact]
    public async Task Rejection_is_not_blocked_by_a_missing_document()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null);

        var result = await SetStatus(db, AnnualLeaveStatus.Rejected);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnualLeaveStatus.Rejected, (await StoredLeaveAsync(db)).Status);
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The edit an employee makes to attach the document once it exists, and the
    /// reason a pending request has to be editable at all.
    /// </summary>
    [Fact]
    public async Task An_employee_can_attach_the_document_to_their_pending_request()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null, AnnualLeaveStatus.Pending);

        var result = await Edit(db, RequiredTypeId, EvidenceUrl);

        Assert.True(result.IsSuccess);
        var leave = await StoredLeaveAsync(db);
        Assert.Equal(EvidenceUrl, leave.EvidenceUrl);
        Assert.Equal(AnnualLeaveStatus.Pending, leave.Status);
    }

    /// <summary>
    /// The old no-exemption rule refused this: a request predating the policy
    /// carries no evidence, and an admin could not fix its reason without
    /// attaching one. Keeping it Pending is not an approval, so it passes now.
    /// </summary>
    [Fact]
    public async Task An_edit_that_keeps_an_undocumented_request_pending_is_accepted()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null);

        var result = await Edit(db, RequiredTypeId, evidenceUrl: null, isAdmin: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rebooked", (await StoredLeaveAsync(db)).Reason);
    }

    /// <summary>An edit can move a pending request onto a type that requires evidence; it stays fileable.</summary>
    [Fact]
    public async Task An_edit_onto_a_type_that_requires_an_attachment_is_accepted_while_pending()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, NoneTypeId, evidenceUrl: null, AnnualLeaveStatus.Pending);

        var result = await Edit(db, RequiredTypeId, evidenceUrl: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequiredTypeId, (await StoredLeaveAsync(db)).LeaveTypeId);
    }

    /// <summary>The admin's edit dialog is the third way into Approved, and it is gated too.</summary>
    [Fact]
    public async Task Approving_through_the_edit_dialog_is_refused_while_the_document_is_missing()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null);

        var result = await Edit(db, RequiredTypeId, evidenceUrl: null, isAdmin: true, status: AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalRefusal, result.Error);
        var leave = await StoredLeaveAsync(db);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, leave.Status);
        Assert.Equal("Out of office", leave.Reason);
    }

    /// <summary>The rule reads the evidence as edited, so attaching and approving in one save is fine.</summary>
    [Fact]
    public async Task Approving_through_the_edit_dialog_with_the_document_attached_in_the_same_edit_is_accepted()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, evidenceUrl: null);

        var result = await Edit(db, RequiredTypeId, EvidenceUrl, isAdmin: true, status: AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess);
        var leave = await StoredLeaveAsync(db);
        Assert.Equal(AnnualLeaveStatus.Approved, leave.Status);
        Assert.Equal(EvidenceUrl, leave.EvidenceUrl);
    }

    /// <summary>An already-approved request must not be left approved and undocumented by an edit.</summary>
    [Fact]
    public async Task An_edit_that_clears_the_evidence_on_an_approved_request_is_refused()
    {
        await using var db = await WorldAsync();
        await SeedLeaveAsync(db, RequiredTypeId, EvidenceUrl, AnnualLeaveStatus.Approved);

        var result = await Edit(db, RequiredTypeId, evidenceUrl: null, isAdmin: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalRefusal, result.Error);
        var leave = await StoredLeaveAsync(db);
        Assert.Equal(EvidenceUrl, leave.EvidenceUrl);
        Assert.Equal("Out of office", leave.Reason);
    }
}
