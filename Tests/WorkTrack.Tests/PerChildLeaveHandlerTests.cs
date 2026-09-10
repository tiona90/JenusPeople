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

    private static Task<Result<Unit>> Edit(AppDbContext db, EditAnnualLeaveRequest request, bool isAdmin = false) =>
        new EditAnnualLeave.Handler(db)
            .Handle(new EditAnnualLeave.Command
            {
                AnnualLeave = request,
                ChangedByUserId = PerChildLeaveWorld.UserId,
                IsAdmin = isAdmin,
            }, CancellationToken.None);

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

    /// <summary>
    /// Mirrors <see cref="A_child_is_not_kept_on_a_type_that_has_no_per_child_entitlement"/>
    /// on the edit path. If <c>EditAnnualLeave</c> ever assigned
    /// <c>request.AnnualLeave.ChildId</c> straight from the client instead of gating it
    /// on the (possibly just-changed) leave type, this request would keep the child
    /// attached after being switched to an ordinary Annual Leave type — and the
    /// calculator's usage query (which filters on child and status, not leave type)
    /// would then silently charge that child's ledger for annual leave never spent
    /// against the per-child cap.
    /// </summary>
    [Fact]
    public async Task Editing_off_a_per_child_type_clears_the_child()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        var leave = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));
        db.AnnualLeaves.Add(leave);
        await db.SaveChangesAsync();

        var result = await Edit(db, new EditAnnualLeaveRequest
        {
            Id = leave.Id,
            LeaveTypeId = PerChildLeaveWorld.AnnualLeaveTypeId,
            ChildId = child.Id, // still posted by the client — the handler must ignore it
            StartDate = leave.StartDate,
            EndDate = leave.EndDate,
            Reason = "Switched to annual leave",
        });

        Assert.True(result.IsSuccess);
        var stored = await db.AnnualLeaves.SingleAsync();
        Assert.Null(stored.ChildId);
    }

    /// <summary>
    /// If <c>EditAnnualLeave</c> called the calculator without <c>excludeLeaveId</c>,
    /// the row being edited would still be read back as its own pre-edit approved
    /// usage and count against the very cap the edit is trying to satisfy — refusing
    /// every edit of an approved per-child request, even ones that only shrink it.
    /// With the exclusion, the request's own prior usage is not double-counted.
    /// </summary>
    [Fact]
    public async Task Editing_an_approved_request_does_not_count_it_against_itself()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        // Five weeks = 25 business days: the full yearly cap for this child, used up
        // entirely by this one approved request.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        var leave = await db.AnnualLeaves.SingleAsync();

        var result = await Edit(db, new EditAnnualLeaveRequest
        {
            Id = leave.Id,
            LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
            ChildId = child.Id,
            StartDate = leave.StartDate,
            EndDate = leave.EndDate.AddDays(-1), // shrink it by one business day
            Reason = "Paternity",
        }, isAdmin: true); // editing an Approved request requires admin

        Assert.True(result.IsSuccess);
    }
}
