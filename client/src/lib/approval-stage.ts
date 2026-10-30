import type { AnnualLeave, AnnualLeaveStatus, LeaveType } from './types'

/**
 * Which approval stage a request is in and who may move it, mirroring
 * `Application/AnnualLeaves/Commands/ApprovalStageRule.cs`. The server decides the
 * stage an Approve lands in — every page sends `'Approved'` — and this only decides
 * which buttons to offer, so a page never shows one the API is certain to refuse.
 * Keep the two in step, the same way `attachment-policy.ts` is kept in step with
 * `AttachmentPolicyRule`. A mirror may under-refuse (a type not yet loaded reads
 * as manager-only); it must never over-refuse.
 *
 * Manager first, then HR, and each stage belongs to its own role. A Manager cannot
 * decide a request that is with HR at all. An HR Administrator cannot decide a
 * Pending request on a type that asks for the manager — that stage is the
 * manager's, and HR's pages leave such rows out (`isWithManager`) until the manager
 * has decided. What HR holds over an approved request is cancelling it before it
 * starts (`canCancelApproved`, mirroring `CancellationRule.cs`).
 */
export type ApprovalFlags = Pick<LeaveType, 'requiresManagerApproval'> & Partial<Pick<LeaveType, 'requiresHrApproval'>>

export interface ApprovalViewer {
    isHrAdministrator: boolean
    /** The viewer's user id. When given, the viewer's own request is never theirs to decide (`UpdateLeaveStatus.OwnLeaveMessage`). */
    userId?: string
}

type DecidableLeave = Pick<AnnualLeave, 'status'> & Partial<Pick<AnnualLeave, 'employeeId'>>

function isOwnLeave(leave: DecidableLeave, viewer: ApprovalViewer): boolean {
    return !!viewer.userId && leave.employeeId === viewer.userId
}

export type ApproveOutcome = 'approved' | 'awaiting-hr'

export type ApprovalRule = 'auto' | 'manager' | 'hr' | 'manager-then-hr'

/** Still waiting on somebody's decision. */
export function isOpenStatus(status: AnnualLeaveStatus): boolean {
    return status === 'Pending' || status === 'AwaitingHrApproval'
}

/**
 * A Pending request that is the manager's to decide, seen by an HR Administrator:
 * not theirs to approve, reject or cancel, and not shown in their queue. A type not
 * loaded reads as manager-only, matching the server's reading of a deleted type. A
 * Pending row on a type with no manager stage can only be a legacy row or one whose
 * type was reconfigured after filing; nobody else can decide it, so HR may.
 */
export function isWithManager(
    leave: Pick<AnnualLeave, 'status'>,
    type: ApprovalFlags | undefined,
    viewer: ApprovalViewer,
): boolean {
    return leave.status === 'Pending' && viewer.isHrAdministrator && (type === undefined || type.requiresManagerApproval)
}

/**
 * A Rejected request the manager turned down, seen by an HR Administrator: the
 * manager's decision, not HR's, so HR's pages leave it out the way they leave out
 * a Pending row that is with the manager. `rejectedFrom` is the status the
 * rejection came out of, read from the status history — `'AwaitingHrApproval'`
 * means HR themselves rejected it, which they do see. With no history to say
 * (a legacy row) the type decides, as it does for a Pending row.
 */
export function isManagersRejection(
    leave: Pick<AnnualLeave, 'status'>,
    type: ApprovalFlags | undefined,
    viewer: ApprovalViewer,
    rejectedFrom: string | null | undefined,
): boolean {
    if (leave.status !== 'Rejected' || !viewer.isHrAdministrator) return false
    if (rejectedFrom === 'AwaitingHrApproval') return false
    return type === undefined || type.requiresManagerApproval
}

/**
 * A submitted timesheet that is the manager's to review, seen by an HR
 * Administrator — the timesheet counterpart of `isWithManager`, mirroring the
 * server's `TimesheetReviewRule`. The server says so per row (`awaitingManager`:
 * a manager other than the submitter is available today); HR's pages leave such
 * rows out and review the rest — a manager's own sheet, a department with no
 * manager, every manager on leave. An API predating the flag reads as not with the
 * manager, so the row is shown and the server answers.
 */
export function isTimesheetWithManager(
    timesheet: { status: string; awaitingManager?: boolean },
    viewer: ApprovalViewer,
): boolean {
    return viewer.isHrAdministrator
        && (timesheet.status === 'Submitted' || timesheet.status === 'Resubmitted')
        && timesheet.awaitingManager === true
}

/** Whether this viewer may approve or reject the request at its current stage. */
export function canDecide(leave: DecidableLeave, viewer: ApprovalViewer, type: ApprovalFlags | undefined): boolean {
    if (isOwnLeave(leave, viewer)) return false
    if (leave.status === 'Pending') return !isWithManager(leave, type, viewer)
    if (leave.status === 'AwaitingHrApproval') return viewer.isHrAdministrator
    return false
}

/**
 * Whether an approved request may still be cancelled: its start date is today or
 * later. Mirrors `CancellationRule.cs`, which compares UTC dates; a request starting
 * today is still cancellable on both sides.
 */
export function canCancelApproved(leave: Pick<AnnualLeave, 'status' | 'startDate'>, today: Date): boolean {
    if (leave.status !== 'Approved') return false
    const start = new Date(leave.startDate); start.setHours(0, 0, 0, 0)
    const day = new Date(today); day.setHours(0, 0, 0, 0)
    return start.getTime() >= day.getTime()
}

/**
 * Where this viewer's Approve lands the request. Any status but `AwaitingHrApproval`
 * itself can be the start of this — Pending ordinarily, but also a Rejected or
 * Cancelled row being reopened from the view dialog — matching the server's
 * `ApprovalStageRule.Resolve`, which applies the same HR check regardless of which
 * status the request is coming from.
 */
export function approveOutcome(
    leave: Pick<AnnualLeave, 'status'>,
    type: ApprovalFlags | undefined,
    viewer: ApprovalViewer,
): ApproveOutcome {
    if (leave.status !== 'AwaitingHrApproval' && !!type?.requiresHrApproval && !viewer.isHrAdministrator) return 'awaiting-hr'
    return 'approved'
}

export function approveButtonLabel(outcome: ApproveOutcome): string {
    return outcome === 'awaiting-hr' ? 'Approve & send to HR' : 'Approve'
}

/** The view dialog's Approve: an open row this viewer may decide, or a rejected one being reopened — never a row already approved or cancelled. */
export function canApproveInDialog(leave: DecidableLeave, viewer: ApprovalViewer, type: ApprovalFlags | undefined): boolean {
    if (isOwnLeave(leave, viewer)) return false
    if (leave.status === 'Rejected') return true
    return canDecide(leave, viewer, type)
}

/** The view dialog's Reject: an open row this viewer may decide, or an approval being taken back — never a row already rejected or cancelled. */
export function canRejectInDialog(leave: DecidableLeave, viewer: ApprovalViewer, type: ApprovalFlags | undefined): boolean {
    if (isOwnLeave(leave, viewer)) return false
    if (leave.status === 'Approved') return true
    return canDecide(leave, viewer, type)
}

/** Filing is approval: neither switch is on. Undefined (type not loaded) reads as not. */
export function autoApproves(type: ApprovalFlags | undefined): boolean {
    return !!type && !type.requiresManagerApproval && !type.requiresHrApproval
}

/** The one-line reading of the two switches, for the leave type's card. */
export function approvalRule(type: ApprovalFlags): ApprovalRule {
    const manager = type.requiresManagerApproval
    const hr = !!type.requiresHrApproval
    if (manager && hr) return 'manager-then-hr'
    if (hr) return 'hr'
    if (manager) return 'manager'
    return 'auto'
}

export function statusChipLabel(status: string): string {
    return status === 'AwaitingHrApproval' ? 'Awaiting HR approval' : status
}

/** How a status reads in a sentence ("Leave approved", "Leave awaiting HR approval"). Falls back to the lower-cased enum name for the four original statuses, as the notification text always did. */
export function statusPhrase(status: string): string {
    return status === 'AwaitingHrApproval' ? 'awaiting HR approval' : status.toLowerCase()
}
