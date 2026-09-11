import type { Gender, LeaveType } from './types'

/**
 * Which leave types an employee is offered.
 *
 * Maternity and Paternity Leave are shown only to an employee whose recorded
 * gender matches the type and who has a child young enough to qualify. Everything
 * else is offered to everybody, so this is a filter over the whole list rather
 * than a special case around two cards.
 *
 * The rule is enforced on the server too
 * (`Application/AnnualLeaves/Commands/ParentalLeaveEligibility.cs`), which is what
 * makes hiding the card mean something. Keep the two in step: this decides what a
 * person sees, that decides what the API accepts, and a disagreement shows up as a
 * card that only fails when pressed.
 */

/** The two types the rule reaches, and the gender each is offered to. */
const OFFERED_TO: Record<string, Gender> = {
    'maternity leave': 'Female',
    'paternity leave': 'Male',
}

/**
 * Trimmed and lower-cased, matching how `SystemLeaveTypes` compares on the server.
 * These two names are frozen — neither can be renamed — which is what makes
 * matching on them sound.
 */
function offeredTo(name: string): Gender | undefined {
    return OFFERED_TO[name.trim().toLowerCase()]
}

/** Whether this leave type is one the gender + eligible-child rule applies to. */
export function isParentalLeaveType(name: string): boolean {
    return offeredTo(name) !== undefined
}

/**
 * Whether to offer `type` to an employee.
 *
 * `gender` null or undefined means "not specified" — the state of every account
 * created before the field existed — and passes the gender half of the rule. The
 * server reads it the same way. Failing closed instead would take parental leave
 * away from the whole company until an administrator filled the field in one
 * person at a time.
 *
 * `hasEligibleChild` still applies in that case: it is a fact about the employee's
 * own declared children, not a field nobody got round to.
 */
export function isLeaveTypeOffered(
    type: Pick<LeaveType, 'name'>,
    gender: Gender | null | undefined,
    hasEligibleChild: boolean,
): boolean {
    const requiredGender = offeredTo(type.name)
    if (requiredGender === undefined) return true

    if (gender && gender !== requiredGender) return false

    return hasEligibleChild
}
