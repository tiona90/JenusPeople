using Application.AnnualLeaves.Commands;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The one place the two approval switches are turned into a status. Every path
/// that files or decides a leave calls this, so the tables here are the
/// behaviour; the handler tests only check the plumbing around them.
/// </summary>
public class ApprovalStageRuleTests
{
    private static LeaveType Type(bool manager, bool hr) =>
        new() { Id = 1, Name = "T", RequiresManagerApproval = manager, RequiresHrApproval = hr };

    [Theory]
    [InlineData(false, false, AnnualLeaveStatus.Approved)]
    [InlineData(true, false, AnnualLeaveStatus.Pending)]
    [InlineData(false, true, AnnualLeaveStatus.AwaitingHrApproval)]
    [InlineData(true, true, AnnualLeaveStatus.Pending)]
    public void A_request_is_filed_in_the_stage_the_flags_say(bool manager, bool hr, AnnualLeaveStatus expected) =>
        Assert.Equal(expected, ApprovalStageRule.InitialStatus(Type(manager, hr)));

    [Fact]
    public void Pending_and_awaiting_hr_are_the_open_states()
    {
        Assert.True(ApprovalStageRule.IsOpen(AnnualLeaveStatus.Pending));
        Assert.True(ApprovalStageRule.IsOpen(AnnualLeaveStatus.AwaitingHrApproval));
        Assert.False(ApprovalStageRule.IsOpen(AnnualLeaveStatus.Approved));
        Assert.False(ApprovalStageRule.IsOpen(AnnualLeaveStatus.Rejected));
        Assert.False(ApprovalStageRule.IsOpen(AnnualLeaveStatus.Cancelled));
    }

    [Theory]
    // From Pending on a manager-only type: the manager approves outright.
    [InlineData(false, AnnualLeaveStatus.Pending, false, AnnualLeaveStatus.Approved)]
    // From Pending on a type needing both: the manager advances it to HR.
    [InlineData(true, AnnualLeaveStatus.Pending, false, AnnualLeaveStatus.AwaitingHrApproval)]
    // From the HR stage, HR finishes it.
    [InlineData(true, AnnualLeaveStatus.AwaitingHrApproval, true, AnnualLeaveStatus.Approved)]
    // Reopening a rejected request from the edit dialog follows the Pending row.
    [InlineData(true, AnnualLeaveStatus.Rejected, true, AnnualLeaveStatus.Approved)]
    [InlineData(true, AnnualLeaveStatus.Rejected, false, AnnualLeaveStatus.AwaitingHrApproval)]
    public void Approve_lands_where_the_stage_table_says(bool needsHr, AnnualLeaveStatus current, bool isHr, AnnualLeaveStatus expected)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, needsHr), current, AnnualLeaveStatus.Approved, isHr);

        Assert.Null(outcome.Error);
        Assert.Equal(expected, outcome.Status);
    }

    /// <summary>
    /// The manager stage is the Manager's. An HR Administrator neither approves,
    /// rejects nor cancels a Pending request on a type that asks for the manager,
    /// whether or not HR comes after — they see it once the manager has decided.
    /// </summary>
    [Theory]
    [InlineData(false, AnnualLeaveStatus.Approved)]
    [InlineData(false, AnnualLeaveStatus.Rejected)]
    [InlineData(false, AnnualLeaveStatus.Cancelled)]
    [InlineData(true, AnnualLeaveStatus.Approved)]
    [InlineData(true, AnnualLeaveStatus.Rejected)]
    [InlineData(true, AnnualLeaveStatus.Cancelled)]
    public void Hr_cannot_decide_a_pending_request_that_is_with_the_manager(bool needsHr, AnnualLeaveStatus requested)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, needsHr), AnnualLeaveStatus.Pending, requested, isHrAdministrator: true);

        Assert.Null(outcome.Status);
        Assert.Equal(ApprovalStageRule.WithManagerMessage, outcome.Error);
    }

    /// <summary>
    /// A Pending row on a type that never asked for the manager can only be a
    /// legacy row or one whose type was reconfigured after filing. Nobody else
    /// can decide it, so HR may.
    /// </summary>
    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    public void Hr_may_decide_a_pending_request_on_a_type_with_no_manager_stage(AnnualLeaveStatus requested)
    {
        var outcome = ApprovalStageRule.Resolve(Type(false, true), AnnualLeaveStatus.Pending, requested, isHrAdministrator: true);

        Assert.Null(outcome.Error);
        Assert.Equal(requested, outcome.Status);
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    [InlineData(AnnualLeaveStatus.Cancelled)]
    public void A_manager_cannot_decide_a_request_that_is_with_hr(AnnualLeaveStatus requested)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, true), AnnualLeaveStatus.AwaitingHrApproval, requested, isHrAdministrator: false);

        Assert.Null(outcome.Status);
        Assert.Equal(ApprovalStageRule.AwaitingHrMessage, outcome.Error);
    }

    [Fact]
    public void Hr_may_reject_a_request_that_is_with_them()
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, true), AnnualLeaveStatus.AwaitingHrApproval, AnnualLeaveStatus.Rejected, isHrAdministrator: true);

        Assert.Equal(AnnualLeaveStatus.Rejected, outcome.Status);
    }

    [Fact]
    public void Rejecting_from_pending_is_the_managers()
    {
        Assert.Equal(AnnualLeaveStatus.Rejected, ApprovalStageRule.Resolve(Type(true, true), AnnualLeaveStatus.Pending, AnnualLeaveStatus.Rejected, false).Status);
        Assert.Equal(ApprovalStageRule.WithManagerMessage, ApprovalStageRule.Resolve(Type(true, true), AnnualLeaveStatus.Pending, AnnualLeaveStatus.Rejected, true).Error);
    }

    [Fact]
    public void The_hr_stage_cannot_be_asked_for_directly()
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, true), AnnualLeaveStatus.Pending, AnnualLeaveStatus.AwaitingHrApproval, isHrAdministrator: true);

        Assert.Null(outcome.Status);
        Assert.Equal(ApprovalStageRule.StageIsDerivedMessage, outcome.Error);
    }

    /// <summary>
    /// A row whose leave type has since been deleted carries no flags. It was
    /// filed under the one-switch rule, so it is treated as manager-only rather
    /// than refused or sent to HR — which also means HR does not decide it.
    /// </summary>
    [Fact]
    public void A_missing_leave_type_reads_as_manager_only()
    {
        Assert.Equal(AnnualLeaveStatus.Approved, ApprovalStageRule.Resolve(null, AnnualLeaveStatus.Pending, AnnualLeaveStatus.Approved, isHrAdministrator: false).Status);
        Assert.Equal(ApprovalStageRule.WithManagerMessage, ApprovalStageRule.Resolve(null, AnnualLeaveStatus.Pending, AnnualLeaveStatus.Approved, isHrAdministrator: true).Error);
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved, false)]
    [InlineData(AnnualLeaveStatus.Approved, true)]
    [InlineData(AnnualLeaveStatus.Rejected, false)]
    [InlineData(AnnualLeaveStatus.Pending, false)]
    [InlineData(AnnualLeaveStatus.Pending, true)]
    public void Asking_for_the_status_a_request_already_has_changes_nothing(AnnualLeaveStatus current, bool isHr)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, true), current, current, isHr);

        Assert.Null(outcome.Error);
        Assert.Equal(current, outcome.Status);
    }

    /// <summary>
    /// HR's standing power over an approved request is to cancel it before it
    /// starts. That is not the manager stage, so it is not refused.
    /// </summary>
    [Fact]
    public void Hr_may_cancel_an_approved_request_on_a_manager_type()
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, false), AnnualLeaveStatus.Approved, AnnualLeaveStatus.Cancelled, isHrAdministrator: true);

        Assert.Null(outcome.Error);
        Assert.Equal(AnnualLeaveStatus.Cancelled, outcome.Status);
    }

    /// <summary>
    /// Pending is where a request is filed, never where a decision sends it. Putting
    /// an approved leave back to Pending dodged CancellationRule — the employee could
    /// then withdraw a leave already taken and have the days handed back.
    /// </summary>
    [Theory]
    [InlineData(AnnualLeaveStatus.Approved, true)]
    [InlineData(AnnualLeaveStatus.Approved, false)]
    [InlineData(AnnualLeaveStatus.Rejected, false)]
    [InlineData(AnnualLeaveStatus.AwaitingHrApproval, true)]
    public void Nothing_is_put_back_to_pending(AnnualLeaveStatus current, bool isHr)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, false), current, AnnualLeaveStatus.Pending, isHr);

        Assert.Null(outcome.Status);
        Assert.Equal(ApprovalStageRule.BackToPendingMessage, outcome.Error);
    }

    /// <summary>A cancelled request is closed: nobody reopens it, the employee files again.</summary>
    [Theory]
    [InlineData(AnnualLeaveStatus.Approved, true)]
    [InlineData(AnnualLeaveStatus.Approved, false)]
    [InlineData(AnnualLeaveStatus.Rejected, false)]
    public void A_cancelled_request_is_not_reopened(AnnualLeaveStatus requested, bool isHr)
    {
        var outcome = ApprovalStageRule.Resolve(Type(true, false), AnnualLeaveStatus.Cancelled, requested, isHr);

        Assert.Null(outcome.Status);
        Assert.Equal(ApprovalStageRule.CancelledIsFinalMessage, outcome.Error);
    }

    /// <summary>
    /// With no manager available to decide it — every one of them away, or none
    /// at all — a request that would have waited on the manager goes to HR
    /// instead. A type that never asked for the manager is unaffected.
    /// </summary>
    [Theory]
    [InlineData(true, false, AnnualLeaveStatus.AwaitingHrApproval)]
    [InlineData(true, true, AnnualLeaveStatus.AwaitingHrApproval)]
    [InlineData(false, false, AnnualLeaveStatus.Approved)]
    [InlineData(false, true, AnnualLeaveStatus.AwaitingHrApproval)]
    public void With_no_manager_available_the_manager_stage_goes_to_hr(bool manager, bool hr, AnnualLeaveStatus expected) =>
        Assert.Equal(expected, ApprovalStageRule.InitialStatus(Type(manager, hr), managerAvailable: false));

    [Fact]
    public void A_department_with_no_manager_is_described_as_such()
    {
        Assert.Equal(
            "Sent to HR for approval: no manager in the department to decide it.",
            ManagerAvailability.Describe(ManagerAvailability.Report.NoManager));
        Assert.Equal(
            "Sent to HR for approval: Nikos Manager on leave.",
            ManagerAvailability.Describe(new ManagerAvailability.Report(false, ["Nikos Manager"])));
    }
}
