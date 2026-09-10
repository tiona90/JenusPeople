import { z } from 'zod'

const MS_PER_DAY = 86_400_000

/**
 * Mirrors the server-side FluentValidation for annual-leave requests
 * (BaseAnnualLeaveValidator + the DTO Range/Required attributes):
 *   - start/end dates required, end on-or-after start, span <= 365 days
 *   - leaveTypeId >= 1
 *   - reason required (non-whitespace), max 500 chars
 *   - employeeId required only on the admin "assign to employee" create path
 *   - childId required only when the selected leave type carries a per-child
 *     entitlement (paternity leave in practice) — an affordance only, since the
 *     server clears the id for any other type and is the one that enforces it
 *
 * The overlap / leave-type-active / employee-exists checks stay server-side
 * (they need the database); this covers the purely client-checkable rules.
 */
export function buildAnnualLeaveSchema(requireEmployee: boolean, perChildLeaveTypeIds: number[] = []) {
    return z
        .object({
            employeeId: requireEmployee
                ? z.string().min(1, 'Please select an employee.')
                : z.string(),
            // Required only when the *selected* type carries a per-child
            // entitlement, which is checked in superRefine below rather than here:
            // the requirement follows leaveTypeId, and a precomputed flag would
            // have to be derived from the form's own value before the form exists.
            // The server decides for real — it clears the id on any other type —
            // so this is the form-level affordance, not the rule.
            childId: z.string(),
            startDate: z.string().min(1, 'Start date is required.'),
            endDate: z.string().min(1, 'End date is required.'),
            leaveTypeId: z.number().int().min(1, 'Please select a leave type.'),
            reason: z
                .string()
                .max(500, 'Reason must not exceed 500 characters.')
                .refine((v) => v.trim().length > 0, 'Reason is required.'),
        })
        .superRefine((val, ctx) => {
            if (perChildLeaveTypeIds.includes(val.leaveTypeId) && !val.childId) {
                ctx.addIssue({
                    code: 'custom',
                    path: ['childId'],
                    message: 'Please select the child this leave is for.',
                })
            }

            if (!val.startDate || !val.endDate) return
            const start = new Date(val.startDate)
            const end = new Date(val.endDate)
            if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) return
            if (end < start) {
                ctx.addIssue({ code: 'custom', path: ['endDate'], message: 'End date must be on or after the start date.' })
            } else if ((end.getTime() - start.getTime()) / MS_PER_DAY > 365) {
                ctx.addIssue({ code: 'custom', path: ['endDate'], message: 'Leave request cannot exceed 365 calendar days.' })
            }
        })
}

export type AnnualLeaveFormValues = {
    employeeId: string
    childId: string
    startDate: string
    endDate: string
    leaveTypeId: number
    reason: string
}
