using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Two approvals for the same person at the same moment. Each one checks the
/// balance against the approved rows it can see, and neither can see the other's
/// until it commits — so with five days left and two four-day requests, both
/// passed and eight days were charged against five. AnnualLeave.RowVersion only
/// protects the one row; nothing protected the ledger the two rows share.
///
/// Every approval now claims the employee's ledger (EmployeeProfile.LedgerVersion,
/// a concurrency token) in the same save, so the one that commits second is
/// refused with the ordinary concurrency message and leaves its row undecided.
///
/// The race is staged rather than timed: the first approval's save is intercepted
/// and the second approval runs to completion, on its own context, in between.
/// </summary>
public class LeaveApprovalConcurrencyTests
{
    private const string Hr = "u-hr";
    private const string Emp = "u-emp";

    private static AppDbContext Context(string databaseName, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptors)
            .Options);

    private static async Task SeedAsync(string databaseName)
    {
        using var db = Context(databaseName);
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.AddRange(
            new User { Id = Emp, UserName = Emp, Email = "emp@t.local", DisplayName = "Emp" },
            new User { Id = Hr, UserName = Hr, Email = "hr@t.local", DisplayName = "HR" });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = "p-emp", UserId = Emp, DepartmentId = 1, AnnualLeaveEntitlement = 5, LeaveBalance = 5 },
            new EmployeeProfile { Id = "p-hr", UserId = Hr, DepartmentId = null });
        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = 1 });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = 1, Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 5,
            RequiresHrApproval = true,
        });
        // Two Mon–Thu requests, four business days each, with HR.
        foreach (var (id, start) in new[] { ("L1", new DateTime(2024, 3, 4)), ("L2", new DateTime(2024, 3, 11)) })
        {
            db.AnnualLeaves.Add(new AnnualLeave
            {
                Id = id, EmployeeId = Emp, EmployeeProfileId = "p-emp", DepartmentId = 1, LeaveTypeId = 1,
                StartDate = start, EndDate = start.AddDays(3), Reason = "Trip",
                Status = AnnualLeaveStatus.AwaitingHrApproval, CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private static Task<Result<Unit>> ApproveAsync(AppDbContext db, string leaveId) =>
        new UpdateLeaveStatus.Handler(db, new FakeEmailService()).Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = leaveId, ChangedByUserId = Hr, IsAdmin = true,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
        }, CancellationToken.None);

    /// <summary>Runs <paramref name="interleave"/> once, just before the first save of the context it is attached to.</summary>
    private sealed class BeforeFirstSave(Func<Task> interleave) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await interleave();
            }
            return result;
        }
    }

    [Fact]
    public async Task The_second_of_two_racing_approvals_is_refused_rather_than_overspending()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);

        Result<Unit>? competing = null;
        using var other = Context(databaseName);
        using var first = Context(databaseName, new BeforeFirstSave(async () => competing = await ApproveAsync(other, "L2")));

        var result = await ApproveAsync(first, "L1");

        Assert.NotNull(competing);
        Assert.True(competing!.IsSuccess, competing.Error);
        Assert.False(result.IsSuccess);
        Assert.Equal(ConcurrencyError.Message, result.Error);

        // Only the approval that won claimed the ledger. The loser's row is not
        // asserted here: the in-memory provider applies a SaveChanges entry by entry
        // and keeps what it wrote before the conflicting one, whereas on SQL Server
        // one SaveChanges is one transaction and the refused approval leaves nothing.
        using var check = Context(databaseName);
        Assert.Equal(1, (await check.EmployeeProfiles.AsNoTracking().SingleAsync(p => p.Id == "p-emp")).LedgerVersion);
    }

    // Every path into Approved claims the ledger, not only the approve button.

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static async Task<int> LedgerVersionAsync(string databaseName)
    {
        using var check = Context(databaseName);
        return (await check.EmployeeProfiles.AsNoTracking().SingleAsync(p => p.Id == "p-emp")).LedgerVersion;
    }

    [Fact]
    public async Task Filing_on_an_auto_approving_type_claims_the_ledger()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);
        using (var db = Context(databaseName))
        {
            var type = await db.LeaveTypes.SingleAsync();
            type.RequiresHrApproval = false;
            await db.SaveChangesAsync();
        }

        using var ctx = Context(databaseName);
        var result = await new CreateAnnualLeave.Handler(ctx, Mapper(), new FakeEmailService()).Handle(new CreateAnnualLeave.Command
        {
            AnnualLeave = new CreateAnnualLeaveRequest
            {
                EmployeeId = Emp, LeaveTypeId = 1, Reason = "Trip",
                StartDate = new DateTime(2024, 4, 1), EndDate = new DateTime(2024, 4, 1),
            },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, await LedgerVersionAsync(databaseName));
    }

    [Fact]
    public async Task Approving_from_the_edit_dialog_claims_the_ledger()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);

        using var ctx = Context(databaseName);
        var result = await new EditAnnualLeave.Handler(ctx, new FakeEmailService()).Handle(new EditAnnualLeave.Command
        {
            ChangedByUserId = Hr, IsAdmin = true,
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1", LeaveTypeId = 1, Reason = "Trip", Status = AnnualLeaveStatus.Approved,
                StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 7),
            },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, await LedgerVersionAsync(databaseName));
    }

    [Fact]
    public async Task The_leave_type_sweep_claims_the_ledger()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);
        using (var db = Context(databaseName))
        {
            // Ten days covers both four-day requests, so the sweep approves cleanly.
            var profile = await db.EmployeeProfiles.SingleAsync(p => p.Id == "p-emp");
            profile.AnnualLeaveEntitlement = 10;
            await db.SaveChangesAsync();
        }

        using var ctx = Context(databaseName);
        var result = await new UpdateLeaveType.Handler(ctx, Mapper()).Handle(new UpdateLeaveType.Command
        {
            Id = 1,
            LeaveType = new UpsertLeaveTypeRequest
            {
                Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 5,
                RequiresManagerApproval = false, RequiresHrApproval = false,
            },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(await LedgerVersionAsync(databaseName) >= 1);
    }
}
