using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The two approval switches through the handlers: where a request is filed,
/// where an Approve lands it, who is emailed, and what the balance does. The
/// table itself lives in ApprovalStageRuleTests; this is the plumbing.
/// </summary>
public class ApprovalStageHandlerTests
{
    private const int Dept = 1;
    private const int ManagerOnlyType = 1;
    private const int HrOnlyType = 2;
    private const int BothType = 3;

    private const string Employee = "u-emp";
    private const string EmployeeProfile = "p-emp";
    private const string Manager = "u-mgr";
    private const string Hr = "u-hr";
    private const string Delegate = "u-del";

    private static readonly DateTime Start = new(2026, 6, 1); // Monday
    private static readonly DateTime End = new(2026, 6, 5);   // Friday: 5 business days

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = Dept, Name = "Engineering", Code = "ENG" });
        db.Roles.AddRange(
            new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() },
            new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        db.Users.AddRange(
            new User { Id = Employee, UserName = Employee, Email = "emp@t.local", DisplayName = "Maria Ioannou" },
            new User { Id = Manager, UserName = Manager, Email = "mgr@t.local", DisplayName = "Nikos Manager" },
            new User { Id = Hr, UserName = Hr, Email = "hr@t.local", DisplayName = "Helen HR" },
            new User { Id = Delegate, UserName = Delegate, Email = "del@t.local", DisplayName = "Andreas Georgiou" });
        db.UserRoles.AddRange(
            new UserRole { UserId = Hr, RoleId = "r-hr" },
            new UserRole { UserId = Manager, RoleId = "r-mgr" });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = EmployeeProfile, UserId = Employee, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-mgr", UserId = Manager, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-del", UserId = Delegate, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-hr", UserId = Hr, DepartmentId = null });
        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = Dept });
        db.LeaveTypes.AddRange(
            new LeaveType { Id = ManagerOnlyType, Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 20, RequiresManagerApproval = true, RequiresHrApproval = false },
            new LeaveType { Id = HrOnlyType, Name = "Sabbatical", IsActive = true, AffectsBalance = true, DefaultAllowance = 20, RequiresManagerApproval = false, RequiresHrApproval = true },
            new LeaveType { Id = BothType, Name = "Unpaid Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 20, RequiresManagerApproval = true, RequiresHrApproval = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<Result<string>> CreateAsync(AppDbContext db, FakeEmailService email, int leaveTypeId) =>
        new CreateAnnualLeave.Handler(db, Mapper(), email).Handle(new CreateAnnualLeave.Command
        {
            AnnualLeave = new CreateAnnualLeaveRequest
            {
                EmployeeId = Employee, LeaveTypeId = leaveTypeId, StartDate = Start, EndDate = End,
                Reason = "Family trip", DelegateId = Delegate,
            },
        }, CancellationToken.None);

    private static async Task<AnnualLeave> SeedLeaveAsync(AppDbContext db, int leaveTypeId, AnnualLeaveStatus status, string id = "L1")
    {
        var leave = new AnnualLeave
        {
            Id = id, EmployeeId = Employee, EmployeeProfileId = EmployeeProfile, DepartmentId = Dept,
            LeaveTypeId = leaveTypeId, StartDate = Start, EndDate = End, Reason = "Family trip",
            DelegateId = Delegate, Status = status, CreatedAt = DateTime.UtcNow,
        };
        db.AnnualLeaves.Add(leave);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return leave;
    }

    private static Task<Result<Unit>> DecideAsync(AppDbContext db, FakeEmailService email, string leaveId, AnnualLeaveStatus status, bool asHr, DateTime? nowUtc = null) =>
        new UpdateLeaveStatus.Handler(db, email).Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = leaveId,
            ChangedByUserId = asHr ? Hr : Manager,
            IsAdmin = asHr,
            IsManager = !asHr,
            Request = new UpdateLeaveStatusRequest { Status = status },
            NowUtc = nowUtc,
        }, CancellationToken.None);

    private static async Task<AnnualLeave> StoredAsync(AppDbContext db, string id = "L1") =>
        (await db.AnnualLeaves.AsNoTracking().FirstAsync(l => l.Id == id));

    private static async Task<decimal> BalanceAsync(AppDbContext db) =>
        (await db.EmployeeProfiles.AsNoTracking().FirstAsync(p => p.Id == EmployeeProfile)).LeaveBalance;

    // ── Filing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_only_type_is_filed_pending_and_the_manager_is_told()
    {
        using var db = await WorldAsync();
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, ManagerOnlyType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db, result.Value!)).Status);
        Assert.Contains(email.Sent, m => m.Recipient == "mgr@t.local" && m.Subject.StartsWith("New leave request"));
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "hr@t.local");
    }

    [Fact]
    public async Task An_hr_only_type_is_filed_straight_into_the_hr_stage_and_hr_is_told()
    {
        using var db = await WorldAsync();
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, HrOnlyType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db, result.Value!)).Status);
        var toHr = Assert.Single(email.Sent, m => m.Recipient == "hr@t.local");
        Assert.Equal(HrApprovalNotification.Subject, toHr.Subject);
        Assert.Contains("Family trip", toHr.HtmlBody);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.Equal(20m, await BalanceAsync(db));
    }

    [Fact]
    public async Task A_type_needing_both_is_filed_pending()
    {
        using var db = await WorldAsync();

        var result = await CreateAsync(db, new FakeEmailService(), BothType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db, result.Value!)).Status);
    }

    // ── Deciding ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_managers_approve_on_a_type_needing_hr_advances_it_and_tells_hr_and_the_employee()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Pending);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Approved, asHr: false);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await StoredAsync(db);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, stored.Status);
        Assert.Null(stored.ApprovedAt);
        Assert.Null(stored.ApprovedById);
        Assert.Equal(20m, await BalanceAsync(db));

        var toHr = Assert.Single(email.Sent, m => m.Recipient == "hr@t.local");
        Assert.Equal(HrApprovalNotification.Subject, toHr.Subject);
        Assert.Contains("Nikos Manager", toHr.HtmlBody);
        var toEmployee = Assert.Single(email.Sent, m => m.Recipient == "emp@t.local");
        Assert.Equal("Your leave request is awaiting HR approval", toEmployee.Subject);
        Assert.Contains("awaiting HR approval", toEmployee.HtmlBody);
        // Coverage is not announced before the final approval.
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "del@t.local");

        var history = await db.LeaveStatusHistories.AsNoTracking().SingleAsync();
        Assert.Equal(AnnualLeaveStatus.Pending, history.OldStatus);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, history.NewStatus);
    }

    /// <summary>
    /// The manager stage is the Manager's: HR is refused on a Pending request
    /// whatever they ask, on a manager-only type and on one that comes to them
    /// afterwards alike. Nothing is written and nobody is told.
    /// </summary>
    [Theory]
    [InlineData(ManagerOnlyType, AnnualLeaveStatus.Approved)]
    [InlineData(ManagerOnlyType, AnnualLeaveStatus.Rejected)]
    [InlineData(BothType, AnnualLeaveStatus.Approved)]
    [InlineData(BothType, AnnualLeaveStatus.Cancelled)]
    public async Task Hr_is_refused_on_a_pending_request_that_is_with_the_manager(int leaveTypeId, AnnualLeaveStatus attempt)
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, leaveTypeId, AnnualLeaveStatus.Pending);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", attempt, asHr: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalStageRule.WithManagerMessage, result.Error);
        var stored = await StoredAsync(db);
        Assert.Equal(AnnualLeaveStatus.Pending, stored.Status);
        Assert.Null(stored.ApprovedById);
        Assert.Equal(20m, await BalanceAsync(db));
        Assert.Empty(email.Sent);
        Assert.Empty(db.LeaveStatusHistories);
    }

    /// <summary>
    /// What HR does hold over an approved request: cancelling it before it starts.
    /// The balance comes back, the delegate is stood down, the employee is told.
    /// </summary>
    [Fact]
    public async Task Hr_cancelling_an_approved_request_before_it_starts_stands_the_delegate_down()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Approved);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Cancelled, asHr: true, nowUtc: Start.AddDays(-10));

        Assert.True(result.IsSuccess, result.Error);
        var stored = await StoredAsync(db);
        Assert.Equal(AnnualLeaveStatus.Cancelled, stored.Status);
        Assert.Null(stored.ApprovedAt);
        Assert.Null(stored.ApprovedById);
        Assert.Equal(20m, await BalanceAsync(db));
        Assert.Contains(email.Sent, m => m.Recipient == "del@t.local");
        Assert.Contains(email.Sent, m => m.Recipient == "emp@t.local" && m.Subject == "Your leave request was cancelled");
    }

    /// <summary>
    /// The manager approved it; HR taking that back is news to them. It used to
    /// reach the employee and the delegate only.
    /// </summary>
    [Fact]
    public async Task Hr_cancelling_an_approved_request_tells_the_manager()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Approved);
        var email = new FakeEmailService();

        var result = await new UpdateLeaveStatus.Handler(db, email).Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = "L1", ChangedByUserId = Hr, IsAdmin = true,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Cancelled, StatusComment = "Project deadline moved" },
            NowUtc = Start.AddDays(-10),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var toManager = Assert.Single(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.Equal($"{ManagerReversalNotification.CancelledSubjectPrefix} Maria Ioannou", toManager.Subject);
        Assert.Contains("Helen HR", toManager.HtmlBody);
        Assert.Contains("Project deadline moved", toManager.HtmlBody);
        // The leave's own reason stays with the approval it was written for.
        Assert.DoesNotContain("Family trip", toManager.HtmlBody);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "hr@t.local");
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.AwaitingHrApproval)]
    public async Task Hr_rejecting_what_the_manager_approved_tells_the_manager(AnnualLeaveStatus from)
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, from);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Rejected, asHr: true);

        Assert.True(result.IsSuccess, result.Error);
        var toManager = Assert.Single(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.StartsWith(ManagerReversalNotification.RejectedSubjectPrefix, toManager.Subject);
    }

    [Fact]
    public async Task A_manager_taking_back_their_own_approval_is_not_emailed_about_it()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Approved);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Rejected, asHr: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.Contains(email.Sent, m => m.Recipient == "emp@t.local");
    }

    [Fact]
    public async Task Hr_cancelling_from_the_edit_dialog_tells_the_manager_too()
    {
        using var db = await WorldAsync();
        // Starts in the future, since the edit path reads the real clock.
        var start = DateTime.UtcNow.Date.AddDays(30);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1", EmployeeId = Employee, EmployeeProfileId = EmployeeProfile, DepartmentId = Dept,
            LeaveTypeId = ManagerOnlyType, StartDate = start, EndDate = start, Reason = "Family trip",
            DelegateId = Delegate, Status = AnnualLeaveStatus.Approved, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var email = new FakeEmailService();

        var result = await new EditAnnualLeave.Handler(db, email).Handle(new EditAnnualLeave.Command
        {
            ChangedByUserId = Hr, IsAdmin = true, IsManager = false,
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1", LeaveTypeId = ManagerOnlyType, StartDate = start, EndDate = start,
                Reason = "Family trip", DelegateId = Delegate, Status = AnnualLeaveStatus.Cancelled,
            },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(email.Sent, m => m.Recipient == "mgr@t.local" && m.Subject.StartsWith(ManagerReversalNotification.CancelledSubjectPrefix));
    }

    [Fact]
    public async Task An_approved_request_that_has_started_cannot_be_cancelled()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Approved);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Cancelled, asHr: true, nowUtc: Start.AddDays(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(CancellationRule.AlreadyStartedMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredAsync(db)).Status);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Hr_approving_from_the_hr_stage_finishes_the_request()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Approved, asHr: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredAsync(db)).Status);
        Assert.Equal(15m, await BalanceAsync(db));
        Assert.Contains(email.Sent, m => m.Recipient == "emp@t.local" && m.Subject == "Your leave request was approved");
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    public async Task A_manager_is_refused_on_a_request_that_is_with_hr(AnnualLeaveStatus attempt)
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", attempt, asHr: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalStageRule.AwaitingHrMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db)).Status);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Nobody_can_ask_for_the_hr_stage_directly()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Pending);

        var result = await DecideAsync(db, new FakeEmailService(), "L1", AnnualLeaveStatus.AwaitingHrApproval, asHr: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalStageRule.StageIsDerivedMessage, result.Error);
    }

    [Fact]
    public async Task A_manager_cannot_pass_an_undocumented_request_to_hr()
    {
        using var db = await WorldAsync();
        var type = await db.LeaveTypes.FindAsync(BothType);
        type!.AttachmentPolicy = AttachmentPolicy.Required;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Pending);

        var result = await DecideAsync(db, new FakeEmailService(), "L1", AnnualLeaveStatus.Approved, asHr: false);

        Assert.False(result.IsSuccess);
        Assert.Contains("supporting document", result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db)).Status);
    }

    [Fact]
    public async Task A_manager_only_type_still_approves_outright()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Pending);

        var result = await DecideAsync(db, new FakeEmailService(), "L1", AnnualLeaveStatus.Approved, asHr: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredAsync(db)).Status);
        Assert.Equal(15m, await BalanceAsync(db));
    }

    /// <summary>
    /// A retried Approve on a row that is already approved — a slow first response,
    /// a double click — must change nothing: not the status, not the approval
    /// metadata, and it must tell nobody anything.
    /// </summary>
    [Fact]
    public async Task Re_approving_an_approved_request_changes_nothing_and_tells_nobody()
    {
        using var db = await WorldAsync();
        var leave = await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Approved);
        var tracked = await db.AnnualLeaves.FirstAsync(l => l.Id == "L1");
        tracked.ApprovedAt = new DateTime(2026, 5, 1);
        tracked.ApprovedById = Hr;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var email = new FakeEmailService();

        var result = await DecideAsync(db, email, "L1", AnnualLeaveStatus.Approved, asHr: false);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await StoredAsync(db);
        Assert.Equal(AnnualLeaveStatus.Approved, stored.Status);
        Assert.Equal(Hr, stored.ApprovedById);
        Assert.Equal(new DateTime(2026, 5, 1), stored.ApprovedAt);
        Assert.Empty(email.Sent);
        Assert.Empty(db.LeaveStatusHistories);
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    private static Task<Result<Unit>> EditAsync(AppDbContext db, FakeEmailService email, string byUserId, bool isAdmin, bool isManager, AnnualLeaveStatus? status, int leaveTypeId = BothType) =>
        new EditAnnualLeave.Handler(db, email).Handle(new EditAnnualLeave.Command
        {
            ChangedByUserId = byUserId, IsAdmin = isAdmin, IsManager = isManager,
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1", LeaveTypeId = leaveTypeId, StartDate = Start, EndDate = End,
                Reason = "Rebooked", DelegateId = Delegate, Status = status,
            },
        }, CancellationToken.None);

    [Fact]
    public async Task The_employee_cannot_edit_a_request_that_is_with_hr()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);

        var result = await EditAsync(db, new FakeEmailService(), Employee, isAdmin: false, isManager: false, status: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("awaiting HR", result.Error);
        Assert.Equal("Family trip", (await StoredAsync(db)).Reason);
    }

    [Fact]
    public async Task A_manager_cannot_edit_a_request_that_is_with_hr_either()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);

        var result = await EditAsync(db, new FakeEmailService(), Manager, isAdmin: false, isManager: true, status: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Family trip", (await StoredAsync(db)).Reason);
    }

    [Fact]
    public async Task Hr_in_scope_may_still_edit_it()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);

        var result = await EditAsync(db, new FakeEmailService(), Hr, isAdmin: true, isManager: false, status: null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Rebooked", (await StoredAsync(db)).Reason);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db)).Status);
    }

    [Fact]
    public async Task A_manager_approving_from_the_edit_dialog_advances_to_hr_and_tells_them()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Pending);
        var email = new FakeEmailService();

        var result = await EditAsync(db, email, Manager, isAdmin: false, isManager: true, status: AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db)).Status);
        Assert.Equal(20m, await BalanceAsync(db));
        Assert.Contains(email.Sent, m => m.Recipient == "hr@t.local" && m.Subject == HrApprovalNotification.Subject);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "del@t.local");
    }

    [Fact]
    public async Task Hr_approving_from_the_edit_dialog_is_refused_while_the_request_is_with_the_manager()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.Pending);
        var email = new FakeEmailService();

        var result = await EditAsync(db, email, Hr, isAdmin: true, isManager: false, status: AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApprovalStageRule.WithManagerMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db)).Status);
        Assert.Equal(20m, await BalanceAsync(db));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Hr_approving_from_the_edit_dialog_finishes_a_request_that_is_with_them()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);
        var email = new FakeEmailService();

        var result = await EditAsync(db, email, Hr, isAdmin: true, isManager: false, status: AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredAsync(db)).Status);
        Assert.Equal(15m, await BalanceAsync(db));
        Assert.Contains(email.Sent, m => m.Recipient == "del@t.local");
    }

    /// <summary>
    /// The same clock reaches the edit dialog's status path: the leave starts on
    /// 1 June 2026, which is behind the real clock, so cancelling it is refused.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_started_request_from_the_edit_dialog_is_refused()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, ManagerOnlyType, AnnualLeaveStatus.Approved);
        var email = new FakeEmailService();

        var result = await EditAsync(db, email, Hr, isAdmin: true, isManager: false, status: AnnualLeaveStatus.Cancelled, leaveTypeId: ManagerOnlyType);

        Assert.False(result.IsSuccess);
        Assert.Equal(CancellationRule.AlreadyStartedMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, (await StoredAsync(db)).Status);
    }

    // ── Still open ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_employee_can_cancel_a_request_that_is_with_hr()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);

        var result = await new DeleteAnnualLeave.Handler(db).Handle(new DeleteAnnualLeave.Command
        {
            Id = "L1", RequestingUserId = Employee, IsAdmin = false, IsManager = false,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(db.AnnualLeaves);
    }

    [Fact]
    public async Task A_request_with_hr_blocks_an_overlapping_one()
    {
        using var db = await WorldAsync();
        await SeedLeaveAsync(db, BothType, AnnualLeaveStatus.AwaitingHrApproval);

        var validator = new Application.AnnualLeaves.Validators.CreateAnnualLeaveRequestValidator(db);
        var outcome = await validator.ValidateAsync(new CreateAnnualLeave.Command
        {
            AnnualLeave = new CreateAnnualLeaveRequest
            {
                EmployeeId = Employee, LeaveTypeId = ManagerOnlyType, StartDate = Start.AddDays(2), EndDate = End.AddDays(2),
                Reason = "Overlaps", DelegateId = Delegate,
            },
        });

        Assert.Contains(outcome.Errors, e => e.ErrorMessage.Contains("overlaps"));
    }

    // ── The manager is on leave ───────────────────────────────────────────────

    /// <summary>An approved leave for the given user that covers today.</summary>
    private static async Task SeedManagerLeaveAsync(AppDbContext db, string userId, string profileId, int daysFromToday, string id = "mgr-leave")
    {
        var today = DateTime.UtcNow.Date;
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = id, EmployeeId = userId, EmployeeProfileId = profileId, DepartmentId = Dept,
            LeaveTypeId = ManagerOnlyType, StartDate = today.AddDays(daysFromToday - 1), EndDate = today.AddDays(daysFromToday + 1),
            Reason = "Away", Status = AnnualLeaveStatus.Approved, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_request_goes_to_hr_when_the_only_manager_is_on_leave_today()
    {
        using var db = await WorldAsync();
        await SeedManagerLeaveAsync(db, Manager, "p-mgr", daysFromToday: 0);
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, ManagerOnlyType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db, result.Value!)).Status);
        var toHr = Assert.Single(email.Sent, m => m.Recipient == "hr@t.local");
        Assert.Equal(HrApprovalNotification.Subject, toHr.Subject);
        Assert.Contains("Nikos Manager", toHr.HtmlBody);
        Assert.Contains("on leave", toHr.HtmlBody);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "mgr@t.local");

        var history = await db.LeaveStatusHistories.AsNoTracking().SingleAsync(h => h.AnnualLeaveId == result.Value);
        Assert.Equal(AnnualLeaveStatus.Pending, history.OldStatus);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, history.NewStatus);
        Assert.Contains("Nikos Manager", history.Comment);
        Assert.Contains("on leave", history.Comment);
    }

    [Fact]
    public async Task A_managers_leave_that_ended_yesterday_does_not_reroute()
    {
        using var db = await WorldAsync();
        await SeedManagerLeaveAsync(db, Manager, "p-mgr", daysFromToday: -2);
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, ManagerOnlyType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db, result.Value!)).Status);
        Assert.Contains(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "hr@t.local");
    }

    [Fact]
    public async Task One_available_manager_is_enough_to_keep_the_manager_stage()
    {
        using var db = await WorldAsync();
        db.Users.Add(new User { Id = "u-mgr2", UserName = "u-mgr2", Email = "mgr2@t.local", DisplayName = "Eleni Manager" });
        db.UserRoles.Add(new UserRole { UserId = "u-mgr2", RoleId = "r-mgr" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-mgr2", UserId = "u-mgr2", DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 });
        await db.SaveChangesAsync();
        await SeedManagerLeaveAsync(db, Manager, "p-mgr", daysFromToday: 0);
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, ManagerOnlyType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, (await StoredAsync(db, result.Value!)).Status);
        Assert.Contains(email.Sent, m => m.Recipient == "mgr2@t.local");
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "hr@t.local");
    }

    /// <summary>
    /// A department with no manager has nobody to take the manager stage, and HR
    /// no longer decides Pending rows in their place — so the request goes to HR
    /// at filing, with a note saying why, rather than waiting on nobody.
    /// </summary>
    [Fact]
    public async Task A_department_with_no_manager_files_straight_to_hr()
    {
        using var db = await WorldAsync();
        var managerProfile = await db.EmployeeProfiles.FirstAsync(p => p.Id == "p-mgr");
        managerProfile.DepartmentId = 99;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var email = new FakeEmailService();

        var result = await CreateAsync(db, email, BothType);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, (await StoredAsync(db, result.Value!)).Status);
        var toHr = Assert.Single(email.Sent, m => m.Recipient == "hr@t.local");
        Assert.Contains("no manager in the department", toHr.HtmlBody);

        var history = await db.LeaveStatusHistories.AsNoTracking().SingleAsync(h => h.AnnualLeaveId == result.Value);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, history.NewStatus);
        Assert.Contains("no manager in the department", history.Comment);
    }
}
