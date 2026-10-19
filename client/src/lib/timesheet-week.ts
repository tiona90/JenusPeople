import type { TimesheetStatus } from './types/timesheet'

/**
 * The My Timesheets button that opens the editor on the current week, worded for
 * what the editor will let them do there: start a week with no sheet, continue
 * one they can still edit (a rejected one is theirs to fix), or view one that is
 * with the approver or done. The same editability the editor uses (`isEditable`
 * in NewTimesheetPage: Draft, Rejected, or no sheet).
 */
export function thisWeekActionLabel(status: TimesheetStatus | null | undefined): 'Start this week' | 'Continue this week' | 'View this week' {
    if (!status) return 'Start this week'
    return status === 'Draft' || status === 'Rejected' ? 'Continue this week' : 'View this week'
}
