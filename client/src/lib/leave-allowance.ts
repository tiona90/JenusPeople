import type { LeaveType } from './types'

/**
 * Where a leave allowance comes from.
 *
 * Three tables carry a number that reads like "the annual allowance", and they do
 * not have to agree. This module fixes the order they are consulted in so every
 * surface quotes the same figure:
 *
 *  1. `LeaveType.defaultAllowance` — **authoritative**. It is the only one that can
 *     vary per leave type, which is what an allowance actually does: annual leave and
 *     sick leave are different budgets, not one budget quoted twice.
 *  2. `AppSettings.defaultAnnualEntitlement` — fallback only, for a leave type that
 *     sets no allowance of its own (`defaultAllowance === 0`) and for an employee who
 *     has no entitlement on record. It is not "the annual allowance"; the Leave
 *     Settings screen labels it as the fallback it is.
 *  3. `EmployeeProfile.annualLeaveEntitlement` — the per-employee override of the
 *     annual-leave budget, and the pool the API enforces on approval. A non-zero
 *     value beats the leave type's figure *for that person only*.
 *
 * `LeaveType.affectsBalance` is a separate question from the allowance: it says whether
 * the type is deducted from the pooled budget the API enforces on approval (see
 * Application/AnnualLeaves/Commands/AnnualLeaveBalanceCalculator.cs). Only annual leave
 * has it set in practice, while sick leave still has a 10 days/year allowance of its
 * own — so a figure quoted here describes that type's allowance, not an enforced quota.
 */

/** Matches the leave type whose budget `EmployeeProfile.annualLeaveEntitlement` overrides. */
export function isAnnualLeaveType(name?: string | null) {
    const n = (name ?? '').toLowerCase()
    return n.includes('annual') || n.includes('vacation')
}

/** A leave type's own allowance, falling back to the app-wide default when it sets none. */
export function allowanceForLeaveType(type: LeaveType | undefined, fallbackDays: number) {
    return type && type.defaultAllowance > 0 ? type.defaultAllowance : fallbackDays
}

/** The annual-leave allowance as configured on Leave Types. */
export function annualLeaveAllowance(leaveTypes: LeaveType[], fallbackDays: number) {
    const annual = leaveTypes.find((t) => t.isActive && isAnnualLeaveType(t.name))
        ?? leaveTypes.find((t) => isAnnualLeaveType(t.name))
    return allowanceForLeaveType(annual, fallbackDays)
}

/**
 * How many annual-leave days one employee gets: their own entitlement when it is set,
 * otherwise the allowance from Leave Types. Replaces the literal `20` that several
 * rollups used to fall back to.
 */
export function employeeAnnualEntitlement(
    profile: { annualLeaveEntitlement: number } | undefined,
    annualAllowanceDays: number,
) {
    return profile && profile.annualLeaveEntitlement > 0 ? profile.annualLeaveEntitlement : annualAllowanceDays
}

/**
 * The budget a single request is measured against: its own leave type's allowance,
 * except for annual leave, where the employee's own entitlement wins. Independent of
 * `affectsBalance` — a type that is not deducted from the pooled budget still has an
 * allowance of its own to measure against.
 */
export function allowanceForRequest(
    type: LeaveType | undefined,
    profile: { annualLeaveEntitlement: number } | undefined,
    fallbackDays: number,
) {
    const typeAllowance = allowanceForLeaveType(type, fallbackDays)
    return isAnnualLeaveType(type?.name) ? employeeAnnualEntitlement(profile, typeAllowance) : typeAllowance
}
