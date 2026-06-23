import { z } from 'zod'

/**
 * Per-task (timesheet entry) rules, mirroring the server-side
 * TimesheetEntryValidator (hours > 0 and <= 24) plus the form's own pairing
 * rule (a row must have BOTH a project and hours, or neither). The client uses
 * a 0.5h minimum increment, matching the existing input step/validation.
 *
 * A completely empty row (no project, no hours) is valid — it's simply ignored
 * on save — so only half-filled or out-of-range rows produce errors.
 */
export const timesheetTaskSchema = z
    .object({
        projectId: z.string(),
        hours: z.string(),
    })
    .superRefine((t, ctx) => {
        const hasProject = t.projectId.trim() !== ''
        const hasHours = t.hours.trim() !== ''

        if (hasProject && !hasHours) {
            ctx.addIssue({ code: 'custom', path: ['hours'], message: 'Enter hours for this task.' })
        }
        if (!hasProject && hasHours) {
            ctx.addIssue({ code: 'custom', path: ['projectId'], message: 'Select a project for this task.' })
        }
        if (hasHours) {
            const h = Number(t.hours)
            if (Number.isNaN(h) || h < 0.5 || h > 24) {
                ctx.addIssue({ code: 'custom', path: ['hours'], message: 'Hours must be between 0.5 and 24.' })
            }
        }
    })

export interface TimesheetTaskFieldErrors {
    projectId?: string
    hours?: string
}

/** Runs {@link timesheetTaskSchema} and flattens issues to per-field messages. */
export function validateTimesheetTask(task: { projectId: string; hours: string }): TimesheetTaskFieldErrors {
    const result = timesheetTaskSchema.safeParse({ projectId: task.projectId, hours: task.hours })
    if (result.success) return {}
    const errors: TimesheetTaskFieldErrors = {}
    for (const issue of result.error.issues) {
        const field = issue.path[0]
        if (field === 'projectId' && !errors.projectId) errors.projectId = issue.message
        if (field === 'hours' && !errors.hours) errors.hours = issue.message
    }
    return errors
}
