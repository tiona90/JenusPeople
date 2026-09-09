import type { LeaveType } from './types'

/**
 * Where a leave allowance comes from.
 *
 * Three tables carry a number that reads like "the annual allowance", and they do
 * not have to agree. This module fixes the order they are consulted in so every
 * surface quotes the same figure:
 *
 *  1. `LeaveType.defaultAllowance` — **authoritative**, and the only place the figure
 *     is edited. It is the only one that can vary per leave type, which is what an
 *     allowance actually does: annual leave and sick leave are different budgets, not
 *     one budget quoted twice. It is also where a new joiner's entitlement comes from
 *     (see Application/AdminUsers/Commands/CreateAdminUser.cs, which reads the
 *     `affectsBalance` type). A type that sets none reads as 0, rendered "—".
 *  2. `EmployeeProfile.annualLeaveEntitlement` — the per-employee override of the
 *     annual-leave budget, and the pool the API enforces on approval. A non-zero
 *     value beats the leave type's figure *for that person only*, which is how
 *     pro-rata and part-time entitlements are expressed.
 *
 * There was a third: `AppSettings.defaultAnnualEntitlement`, an org-wide number on
 * Leave Settings that was free to disagree with (1) and out of the box did — 20
 * against annual leave's 25. It is gone; nothing falls back to it any more.
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

/** A leave type's own allowance. 0 means the type sets none — callers render that "—". */
export function allowanceForLeaveType(type: LeaveType | undefined) {
    return type && type.defaultAllowance > 0 ? type.defaultAllowance : 0
}

/** The annual-leave allowance as configured on Leave Types. */
export function annualLeaveAllowance(leaveTypes: LeaveType[]) {
    const annual = leaveTypes.find((t) => t.isActive && isAnnualLeaveType(t.name))
        ?? leaveTypes.find((t) => isAnnualLeaveType(t.name))
    return allowanceForLeaveType(annual)
}

/**
 * The leave type the pooled balance is kept in — the one `affectsBalance` marks, which
 * is annual leave in practice. Distinct from `isAnnualLeaveType`, which matches on name:
 * this asks which type the API actually enforces a balance for.
 */
export function balanceLeaveType(leaveTypes: LeaveType[]) {
    return leaveTypes.find((t) => t.isActive && t.affectsBalance)
        ?? leaveTypes.find((t) => t.affectsBalance)
}

/**
 * How many unused annual-leave days survive the year-end rollover, as configured on
 * Leave Types beside the allowance they cap. This was an org-wide AppSettings column,
 * free to disagree with the per-type allowance and unable to say that sick leave
 * carries nothing. 0 means nothing carries over.
 */
export function annualCarryoverCap(leaveTypes: LeaveType[]) {
    return balanceLeaveType(leaveTypes)?.maxCarryoverDays ?? 0
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
) {
    const typeAllowance = allowanceForLeaveType(type)
    return isAnnualLeaveType(type?.name) ? employeeAnnualEntitlement(profile, typeAllowance) : typeAllowance
}
