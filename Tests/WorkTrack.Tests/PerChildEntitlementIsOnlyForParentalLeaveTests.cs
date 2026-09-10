using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Validators;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A per-child entitlement belongs to Maternity and Paternity Leave and to nothing
/// else. The ledger is keyed by <c>AnnualLeave.ChildId</c> and a request against a
/// per-child type must name a child, which only means anything for the leave a birth
/// grants.
///
/// It used to be a switch on every leave type, so an admin could put a per-child
/// ledger on Sick Leave — and then every sick day would have had to be booked
/// against a child. The edit dialog now shows the three numbers for those two types
/// with no toggle and no section at all for the rest; these hold the server's half,
/// so the rule survives a caller that goes around the UI.
/// </summary>
public class PerChildEntitlementIsOnlyForParentalLeaveTests
{
    private static UpsertLeaveTypeRequest Request(string name, bool perChild) => new()
    {
        Name = name,
        RequiresApproval = true,
        IsActive = true,
        PerChildEntitlement = perChild,
        // Valid numbers throughout, so a refusal can only be about the name.
        PerChildTotalWeeks = perChild ? 18 : 0,
        PerChildWeeksPerYear = perChild ? 5 : 0,
        ChildEligibleUntilAge = perChild ? 15 : 0,
    };

    private const string Message = "Only Maternity Leave and Paternity Leave can carry a per-child entitlement.";

    [Theory]
    [InlineData(SystemLeaveTypes.MaternityLeave)]
    [InlineData(SystemLeaveTypes.PaternityLeave)]
    public void The_two_parental_types_may_carry_a_per_child_entitlement(string name)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Request(name, perChild: true));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    /// <summary>
    /// Annual Leave is in the list deliberately: being one of the built-in types does
    /// not make a type per-child. It is the pooled ledger, and carrying both would
    /// charge one day of leave twice.
    /// </summary>
    [Theory]
    [InlineData(SystemLeaveTypes.AnnualLeave)]
    [InlineData("Sick Leave")]
    [InlineData("Study Leave")]
    [InlineData("Bereavement")]
    public void No_other_type_may_carry_one(string name)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Request(name, perChild: true));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == Message);
    }

    [Theory]
    [InlineData("maternity leave")]
    [InlineData("PATERNITY LEAVE")]
    public void The_name_is_matched_case_insensitively(string name)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Request(name, perChild: true));

        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage == Message);
    }

    // The rule only bites on the flag being set. Every ordinary type saves as before.
    [Theory]
    [InlineData(SystemLeaveTypes.AnnualLeave)]
    [InlineData("Sick Leave")]
    [InlineData(SystemLeaveTypes.MaternityLeave)]
    public void A_type_that_sets_no_per_child_entitlement_is_unaffected(string name)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Request(name, perChild: false));

        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage == Message);
    }

    /* ── The flag the dialog reads ──────────────────────────────────────────── */

    [Theory]
    [InlineData(SystemLeaveTypes.MaternityLeave)]
    [InlineData(SystemLeaveTypes.PaternityLeave)]
    public void The_dto_marks_the_parental_types_as_per_child_capable(string name)
    {
        Assert.True(new LeaveTypeDto { Name = name }.SupportsPerChildEntitlement);
    }

    [Theory]
    [InlineData(SystemLeaveTypes.AnnualLeave)]
    [InlineData("Sick Leave")]
    [InlineData("")]
    public void The_dto_marks_everything_else_as_not(string name)
    {
        Assert.False(new LeaveTypeDto { Name = name }.SupportsPerChildEntitlement);
    }
}
