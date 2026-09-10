using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Who a paternity request may be for. The caps are in PerChildLeaveCapTests; this
/// covers the four questions asked before them — is this even a per-child type, was
/// a child named, is that child this employee's, and is the child still eligible for
/// every day of the request.
///
/// The eligibility question is asked of the request's END date, so a request is
/// never part-eligible: an employee cannot start leave the day before the 15th
/// birthday and run five weeks past it.
/// </summary>
public class PerChildLeaveEntitlementTests
{
    // Andreas turns 15 on 04 Mar 2026.
    private static readonly DateOnly AndreasDob = new(2011, 3, 4);

    [Fact]
    public async Task An_ordinary_leave_type_is_left_alone()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request(
            childId: null,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 5),
            leaveTypeId: PerChildLeaveWorld.AnnualLeaveTypeId);

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task A_per_child_type_requires_a_child()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request(childId: null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.Equal("Select the child this Paternity Leave is for.", error);
    }

    [Fact]
    public async Task An_unknown_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request("no-such-child", new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        Assert.Equal("The selected child is not on your profile.", await PerChildLeaveWorld.CheckAsync(db, request));
    }

    /// <summary>
    /// The child id arrives from the client — including on an admin's on-behalf
    /// request, where the employee and the child are both chosen in the browser.
    /// Without this check one employee could spend another's entitlement.
    /// </summary>
    [Fact]
    public async Task Another_employees_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "profile-2", UserId = "employee-2" });
        var otherChild = new Child { EmployeeProfileId = "profile-2", Name = "Someone else's", DateOfBirth = AndreasDob };
        db.Children.Add(otherChild);
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(otherChild.Id, new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        Assert.Equal("The selected child is not on your profile.", await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task An_eligible_child_passes()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        // Mon 02 Mar - Tue 03 Mar 2026, both before the birthday on the 4th.
        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task A_request_that_straddles_the_fifteenth_birthday_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 10));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.Equal(
            "Andreas turns 15 on 04 Mar 2026 — Paternity Leave for this child must end on or before 03 Mar 2026.",
            error);
    }

    [Fact]
    public async Task A_request_after_the_fifteenth_birthday_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.NotNull(error);
        Assert.Contains("must end on or before 03 Mar 2026", error);
    }

    /// <summary>
    /// The last eligible day is bookable. An off-by-one here silently costs an
    /// employee a day of entitlement on the boundary that matters most.
    /// </summary>
    [Fact]
    public async Task The_day_before_the_birthday_is_still_bookable()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 3), new DateTime(2026, 3, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }
}
