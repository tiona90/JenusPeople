import type { TimesheetStatusHistory } from './types'

/**
 * A reviewer's note on a timesheet is a status-history row with a comment: the
 * reason a manager gave when rejecting, or the reason an HR Administrator gave
 * when cancelling an approval (ReopenTimesheet, Approved → Rejected, which hands
 * the sheet back to the employee to correct; rows written before that went
 * Approved → Submitted and still read as a cancelled approval). The
 * server writes it and emails it; these helpers put it on screen, because a
 * manager whose queue holds a week they already approved needs to know why it
 * is back without hunting for the email.
 */

/** The newest row that carries a comment, per timesheet. */
export function latestCommentByTimesheet(histories: TimesheetStatusHistory[]): Map<string, TimesheetStatusHistory> {
    const map = new Map<string, TimesheetStatusHistory>()
    for (const h of histories) {
        if (!h.comment) continue
        const prev = map.get(h.timesheetId)
        if (!prev || new Date(h.changedAt) > new Date(prev.changedAt)) {
            map.set(h.timesheetId, h)
        }
    }
    return map
}

/**
 * The note that put the sheet in its present status, or nothing. A rejection
 * reason is stale once the employee resubmits, and a cancelled approval's reason
 * is stale once the manager approves again — the sheet has moved on and the old
 * reason would read as a verdict on the new work.
 */
export function currentReviewNote(status: string, latest: TimesheetStatusHistory | undefined): TimesheetStatusHistory | undefined {
    if (!latest || !latest.comment) return undefined
    return latest.newStatus === status ? latest : undefined
}

export function isCancelledApproval(h: TimesheetStatusHistory): boolean {
    return h.oldStatus === 'Approved' && (h.newStatus === 'Rejected' || h.newStatus === 'Submitted')
}

/** "Approval cancelled by Helen HR", "Rejected by Mark Manager", or a plain "Note from …". */
export function reviewNoteLabel(h: TimesheetStatusHistory): string {
    if (isCancelledApproval(h)) return `Approval cancelled by ${h.changedByUserName}`
    if (h.newStatus === 'Rejected') return `Rejected by ${h.changedByUserName}`
    return `Note from ${h.changedByUserName}`
}
