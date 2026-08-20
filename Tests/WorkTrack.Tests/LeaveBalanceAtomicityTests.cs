using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Every leave command that touches the balance writes twice: the leave itself,
/// then the recalculated balance. The second write has to be a second write —
/// the calculator reads approved leave back out of the database to total it up,
/// so it cannot see anything still sitting in the change tracker.
///
/// The two used to run unprotected, so a failure on the balance write left the
/// leave committed and the balance describing the world as it was beforehand:
/// days taken that no longer count against anyone, or the reverse. Both writes
/// now share one transaction.
///
/// These run on SQLite rather than <see cref="TestDb"/> because the in-memory
/// provider ignores transactions altogether — see <see cref="TransactionalTestDb"/>.
/// </summary>
public class LeaveBalanceAtomicityTests
{
    private const int LeaveTypeId = 1;
    private const string EmployeeUserId = "u-employee";
    private const string EmployeeProfileId = "p-employee";
    private const string AdminUserId = "u-admin";
    private const string LeaveId = "L-1";

    // A Monday-to-Friday week: five business days, no weekend to discount.
    private static readonly DateTime LeaveStart = new(2024, 3, 4);
    private static readonly DateTime LeaveEnd = new(2024, 3, 8);

    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    /// <summary>
    /// SQLite enforces foreign keys, so the whole chain has to exist: department,
    /// both users, the employee profile and the leave type.
    /// </summary>
    private static async Task SeedWorldAsync(AppDbContext db, bool requiresApproval)
    {
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User
        {
            Id = EmployeeUserId,
            UserName = "employee@test.local",
            Email = "employee@test.local",
            DisplayName = "Employee",
        });
        db.Users.Add(new User
        {
            Id = AdminUserId,
            UserName = "admin@test.local",
            Email = "admin@test.local",
            DisplayName = "Admin",
        });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = EmployeeProfileId,
            UserId = EmployeeUserId,
            DepartmentId = 1,
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = LeaveTypeId,
            Name = "Annual",
            IsActive = true,
            AffectsBalance = true,
            RequiresApproval = requiresApproval,
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task SeedLeaveAsync(AppDbContext db, AnnualLeaveStatus status)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = LeaveId,
            EmployeeId = EmployeeUserId,
            EmployeeProfileId = EmployeeProfileId,
            DepartmentId = 1,
            LeaveTypeId = LeaveTypeId,
            Status = status,
            Reason = "Family holiday",
            StartDate = LeaveStart,
            EndDate = LeaveEnd,
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// The balance write is the second save in each of these handlers, so failing
    /// on save 2 is what "the balance write blew up" looks like from inside.
    /// </summary>
    private static FailOnNthSaveInterceptor FailBalanceWrite() => new(failOnSave: 2);

    private static async Task AssertBalanceWriteFailedAsync(
        FailOnNthSaveInterceptor interceptor, Func<Task> act)
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(FailOnNthSaveInterceptor.FailureMessage, thrown.Message);

        // Two saves reached: the leave write, then the balance write that failed.
        // If the handler had only saved once, the failure landed on the first write
        // instead — which rolls back on its own, making everything below pass
        // without saying anything about the pair.
        Assert.Equal(2, interceptor.SaveCount);
    }

    /* ── CreateAnnualLeave ──────────────────────────────────────────────────── */

    /// <summary>
    /// A leave type that needs no approval is created already Approved, which is
    /// what makes Create sync the balance at all.
    /// </summary>
    [Fact]
    public async Task A_failed_balance_write_rolls_back_the_created_leave()
    {
        var interceptor = FailBalanceWrite();
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedWorldAsync(db, requiresApproval: false);
        interceptor.Arm();

        var handler = new CreateAnnualLeave.Handler(db, BuildMapper(), new FakeEmailService());

        await AssertBalanceWriteFailedAsync(interceptor, () => handler.Handle(
            new CreateAnnualLeave.Command
            {
                AnnualLeave = new CreateAnnualLeaveRequest
                {
                    EmployeeId = EmployeeUserId,
                    LeaveTypeId = LeaveTypeId,
                    Reason = "Family holiday",
                    StartDate = LeaveStart,
                    EndDate = LeaveEnd,
                },
            },
            CancellationToken.None));

        db.ChangeTracker.Clear();
        // Unprotected, the leave is committed by the first save and survives —
        // an approved absence that no balance anywhere accounts for.
        Assert.False(await db.AnnualLeaves.AsNoTracking().AnyAsync());
        Assert.False(await db.LeaveStatusHistories.AsNoTracking().AnyAsync());
    }

    /* ── UpdateLeaveStatus ──────────────────────────────────────────────────── */

    [Fact]
    public async Task A_failed_balance_write_rolls_back_the_approval()
    {
        var interceptor = FailBalanceWrite();
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedWorldAsync(db, requiresApproval: true);
        await SeedLeaveAsync(db, AnnualLeaveStatus.Pending);
        interceptor.Arm();

        var handler = new UpdateLeaveStatus.Handler(db, new FakeEmailService(), new FakeChatNotificationService());

        await AssertBalanceWriteFailedAsync(interceptor, () => handler.Handle(
            new UpdateLeaveStatus.Command
            {
                LeaveId = LeaveId,
                ChangedByUserId = AdminUserId,
                IsAdmin = true,
                Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            },
            CancellationToken.None));

        db.ChangeTracker.Clear();
        var leave = await db.AnnualLeaves.AsNoTracking().SingleAsync();
        Assert.Equal(AnnualLeaveStatus.Pending, leave.Status);
        Assert.Null(leave.ApprovedById);
        Assert.Null(leave.ApprovedAt);
        // The audit row rides the same save as the status change, so it has to go
        // back too: an approval in the history that never happened is worse than
        // no history at all.
        Assert.False(await db.LeaveStatusHistories.AsNoTracking().AnyAsync());
    }

    /* ── EditAnnualLeave ────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_failed_balance_write_rolls_back_the_edit()
    {
        var interceptor = FailBalanceWrite();
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedWorldAsync(db, requiresApproval: true);
        await SeedLeaveAsync(db, AnnualLeaveStatus.Pending);
        interceptor.Arm();

        var handler = new EditAnnualLeave.Handler(db);

        await AssertBalanceWriteFailedAsync(interceptor, () => handler.Handle(
            new EditAnnualLeave.Command
            {
                ChangedByUserId = EmployeeUserId,
                AnnualLeave = new EditAnnualLeaveRequest
                {
                    Id = LeaveId,
                    LeaveTypeId = LeaveTypeId,
                    Reason = "Rebooked",
                    StartDate = LeaveStart.AddDays(7),
                    EndDate = LeaveEnd.AddDays(7),
                },
            },
            CancellationToken.None));

        db.ChangeTracker.Clear();
        var leave = await db.AnnualLeaves.AsNoTracking().SingleAsync();
        Assert.Equal(LeaveStart, leave.StartDate);
        Assert.Equal(LeaveEnd, leave.EndDate);
        Assert.Equal("Family holiday", leave.Reason);
    }

    /* ── DeleteAnnualLeave ──────────────────────────────────────────────────── */

    [Fact]
    public async Task A_failed_balance_write_rolls_back_the_cancellation()
    {
        var interceptor = FailBalanceWrite();
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedWorldAsync(db, requiresApproval: true);
        await SeedLeaveAsync(db, AnnualLeaveStatus.Pending);
        interceptor.Arm();

        var handler = new DeleteAnnualLeave.Handler(db);

        await AssertBalanceWriteFailedAsync(interceptor, () => handler.Handle(
            new DeleteAnnualLeave.Command
            {
                Id = LeaveId,
                RequestingUserId = EmployeeUserId,
            },
            CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.True(await db.AnnualLeaves.AsNoTracking().AnyAsync(l => l.Id == LeaveId));
    }

    /* ── The commit itself ──────────────────────────────────────────────────── */

    /// <summary>
    /// The counterweight to the four above: with nothing failing, the transaction
    /// has to actually commit. A missing CommitAsync would roll every one of these
    /// operations back in production while the in-memory tests stayed green, since
    /// that provider ignores transactions.
    /// </summary>
    [Fact]
    public async Task A_successful_approval_commits_both_the_leave_and_the_balance()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedWorldAsync(db, requiresApproval: true);
        await SeedLeaveAsync(db, AnnualLeaveStatus.Pending);

        // A deliberately stale stored balance. The sync recomputes only the current
        // leave year, and the seeded leave is in a past one, so it recomputes to the
        // full entitlement — which this can only observe if the second write inside
        // the transaction really did commit.
        var profile = await db.EmployeeProfiles.SingleAsync();
        profile.LeaveBalance = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateLeaveStatus.Handler(db, new FakeEmailService(), new FakeChatNotificationService());

        var result = await handler.Handle(
            new UpdateLeaveStatus.Command
            {
                LeaveId = LeaveId,
                ChangedByUserId = AdminUserId,
                IsAdmin = true,
                Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(
            AnnualLeaveStatus.Approved,
            (await db.AnnualLeaves.AsNoTracking().SingleAsync()).Status);
        Assert.True(await db.LeaveStatusHistories.AsNoTracking().AnyAsync());
        Assert.Equal(25, (await db.EmployeeProfiles.AsNoTracking().SingleAsync()).LeaveBalance);
    }
}
