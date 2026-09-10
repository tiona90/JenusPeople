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
/// The per-child rules only matter if a handler enforces them. One deliberate
/// difference from the pooled annual-leave balance, which is checked at creation
/// only when the type auto-approves: a per-child type is checked at creation even
/// when approval is required, so the employee learns immediately rather than waiting
/// days for a manager to hit an error the manager cannot fix.
/// </summary>
public class PerChildLeaveHandlerTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<string>> Create(AppDbContext db, CreateAnnualLeaveRequest request) =>
        new CreateAnnualLeave.Handler(db, BuildMapper(), new FakeEmailService())
            .Handle(new CreateAnnualLeave.Command { AnnualLeave = request }, CancellationToken.None);

    private static CreateAnnualLeaveRequest Request(string? childId, DateTime start, DateTime end) => new()
    {
        EmployeeId = PerChildLeaveWorld.UserId,
        ChildId = childId,
        LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
        StartDate = start,
        EndDate = end,
        Reason = "Paternity",
    };

    [Fact]
    public async Task Creating_paternity_leave_without_a_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var result = await Create(db, Request(childId: null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)));

        Assert.False(result.IsSuccess);
        Assert.Equal("Select the child this Paternity Leave is for.", result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task Creating_paternity_leave_stores_the_child()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        var result = await Create(db, Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)));

        Assert.True(result.IsSuccess);
        var stored = await db.AnnualLeaves.SingleAsync();
        Assert.Equal(child.Id, stored.ChildId);
    }

    /// <summary>
    /// The cap is enforced at creation even though Paternity Leave requires
    /// approval — the employee finds out now, not after a manager tries.
    /// </summary>
    [Fact]
    public async Task The_yearly_cap_is_enforced_at_creation_despite_needing_approval()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var result = await Create(db, Request(child.Id, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11)));

        Assert.False(result.IsSuccess);
        Assert.Contains("left for the leave year", result.Error);
    }

    /// <summary>
    /// Switching a request off a per-child type must not leave a stale child
    /// attached, or the ledger would keep charging a child for annual leave.
    /// </summary>
    [Fact]
    public async Task A_child_is_not_kept_on_a_type_that_has_no_per_child_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        var request = Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));
        request.LeaveTypeId = PerChildLeaveWorld.AnnualLeaveTypeId;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess);
        var stored = await db.AnnualLeaves.SingleAsync();
        Assert.Null(stored.ChildId);
    }

    [Fact]
    public async Task Approving_paternity_leave_re_checks_the_cap()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        // Two pending five-week requests each passed the creation check, because
        // nothing pending counts as used. The second approval is where it breaks.
        var first = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        var second = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));
        db.AnnualLeaves.AddRange(first, second);
        db.Users.Add(new User { Id = "admin-1", UserName = "admin@example.com", Email = "admin@example.com", DisplayName = "Admin" });
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveStatus.Handler(db, new FakeEmailService());

        var firstResult = await handler.Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = first.Id,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            ChangedByUserId = "admin-1",
            IsAdmin = true,
        }, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await handler.Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = second.Id,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            ChangedByUserId = "admin-1",
            IsAdmin = true,
        }, CancellationToken.None);

        Assert.False(secondResult.IsSuccess);
        Assert.Contains("left for the leave year", secondResult.Error);
    }
}
