using Domain;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Turns a leave type's two approval switches into the status a request is filed
/// in and the status an "Approve" lands it in. The client always asks for
/// <see cref="AnnualLeaveStatus.Approved"/>; this decides whether that means the
/// HR stage or the end. <c>client/src/lib/approval-stage.ts</c> mirrors it so no
/// page offers a button the API is certain to refuse — keep the two in step, the
/// way <see cref="AttachmentPolicyRule"/> and <c>attachment-policy.ts</c> are.
///
/// <para>
/// Manager first, then HR, and each stage belongs to its own role. A Manager
/// cannot decide a request that is with HR — approve, reject or cancel — because
/// they already had their say at stage one. An HR Administrator cannot decide a
/// Pending request on a type that asks for the manager — the manager has not had
/// theirs yet — so HR sees such a request once it is approved (to cancel it before
/// it starts, <see cref="CancellationRule"/>) or once the manager has passed it to
/// them. HR does decide a Pending row on a type with no manager stage, which can
/// only be a legacy row or one whose type was reconfigured after filing: nobody
/// else could. A request whose managers are all away, or whose department has
/// none, is filed straight into the HR stage (<see cref="ManagerAvailability"/>)
/// rather than left Pending for nobody.
/// </para>
///
/// A <c>null</c> leave type (a row whose type has since been deleted) is treated as
/// manager-only, which is the one-switch rule it was filed under.
/// </summary>
public static class ApprovalStageRule
{
    public const string AwaitingHrMessage =
        "This request is awaiting HR approval; only an HR Administrator can decide it.";

    public const string WithManagerMessage =
        "This request is awaiting the manager's decision; an HR Administrator does not decide the manager stage.";

    public const string BackToPendingMessage =
        "A decided request cannot be put back to Pending. Cancel or reject it instead.";

    public const string CancelledIsFinalMessage =
        "This request was cancelled and is closed; file a new request instead.";

    public const string StageIsDerivedMessage =
        "A request cannot be put into 'Awaiting HR approval' directly. Approve it and the stage is decided for you.";

    /// <summary>
    /// The status a fresh request is filed in. <paramref name="managerAvailable"/>
    /// is false when nobody can take the manager stage — every manager who could
    /// decide the request is on leave today, or the department has none
    /// (<see cref="ManagerAvailability"/>): the manager stage then goes to HR
    /// rather than waiting on somebody who is away or does not exist. A type that
    /// never asked for the manager is unaffected by it.
    /// </summary>
    public static AnnualLeaveStatus InitialStatus(LeaveType leaveType, bool managerAvailable = true) =>
        (leaveType.RequiresManagerApproval, leaveType.RequiresHrApproval) switch
        {
            (false, false) => AnnualLeaveStatus.Approved,
            (false, true) => AnnualLeaveStatus.AwaitingHrApproval,
            (true, _) when !managerAvailable => AnnualLeaveStatus.AwaitingHrApproval,
            _ => AnnualLeaveStatus.Pending,
        };

    /// <summary>Still waiting on somebody's decision.</summary>
    public static bool IsOpen(AnnualLeaveStatus status) =>
        status is AnnualLeaveStatus.Pending or AnnualLeaveStatus.AwaitingHrApproval;

    /// <summary>
    /// Whether a Pending request is the manager's to decide — the type asks for
    /// the manager, or has been deleted and reads as manager-only.
    /// </summary>
    public static bool IsWithManager(LeaveType? leaveType, AnnualLeaveStatus status) =>
        status == AnnualLeaveStatus.Pending && (leaveType is null || leaveType.RequiresManagerApproval);

    /// <summary>The status to store, or why the request cannot be changed by this caller.</summary>
    public readonly record struct Outcome(AnnualLeaveStatus? Status, string? Error);

    public static Outcome Resolve(
        LeaveType? leaveType,
        AnnualLeaveStatus current,
        AnnualLeaveStatus requested,
        bool isHrAdministrator)
    {
        // Asking for the status a request already has changes nothing — a retried
        // Approve on an approved row must not send it back to HR.
        if (requested == current)
            return new Outcome(current, null);

        // Cancelled is the end of a request; the employee files again.
        if (current == AnnualLeaveStatus.Cancelled)
            return new Outcome(null, CancelledIsFinalMessage);

        // Pending is where a request is filed, never where a decision sends it:
        // putting an approved leave back there dodged CancellationRule, since an
        // open request can then be withdrawn and the days handed back.
        if (requested == AnnualLeaveStatus.Pending)
            return new Outcome(null, BackToPendingMessage);

        if (requested == AnnualLeaveStatus.AwaitingHrApproval)
            return new Outcome(null, StageIsDerivedMessage);

        if (current == AnnualLeaveStatus.AwaitingHrApproval && !isHrAdministrator)
            return new Outcome(null, AwaitingHrMessage);

        if (isHrAdministrator && IsWithManager(leaveType, current))
            return new Outcome(null, WithManagerMessage);

        if (requested != AnnualLeaveStatus.Approved)
            return new Outcome(requested, null);

        var needsHr = leaveType?.RequiresHrApproval == true;
        if (needsHr && !isHrAdministrator)
            return new Outcome(AnnualLeaveStatus.AwaitingHrApproval, null);

        return new Outcome(AnnualLeaveStatus.Approved, null);
    }
}
