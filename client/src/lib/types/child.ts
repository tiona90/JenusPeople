/**
 * A declared child. `ageYears`, `isEligible` and `lastEligibleDate` are computed by
 * the server from `dateOfBirth` on every read — nothing about them is stored, which
 * is how a child ages out of paternity-leave eligibility with no job to run.
 */
export interface Child {
    id: string
    name: string
    /** ISO date "yyyy-MM-dd". */
    dateOfBirth: string
    ageYears: number
    isEligible: boolean
    /** The last date leave for this child may end on. ISO date. */
    lastEligibleDate: string
}

export interface UpsertChildRequest {
    name: string
    dateOfBirth: string
}

/**
 * One child's per-child leave ledger, in business days with a weeks figure for
 * display. An ineligible child is present with `isEligible: false` and zeroed
 * entitlement — `usedDays` still shows what they used while eligible.
 */
export interface ChildLeaveEntitlement {
    childId: string
    name: string
    dateOfBirth: string
    ageYears: number
    isEligible: boolean
    lastEligibleDate: string
    totalDays: number
    totalWeeks: number
    usedDays: number
    remainingDays: number
    thisYearCapDays: number
    thisYearUsedDays: number
    thisYearRemainingDays: number
    /** Full ISO timestamp (DateTime), not date-only. */
    leaveYearStart: string
    /** Full ISO timestamp (DateTime), not date-only. */
    leaveYearEnd: string
}

/**
 * The employee's ledger. The totals cover eligible children only, so they fall on
 * their own when a child turns 15.
 */
export interface ChildLeaveEntitlementSummary {
    /** Null when no active leave type carries a per-child entitlement. */
    leaveTypeId: number | null
    leaveTypeName: string
    eligibleChildCount: number
    totalRemainingDays: number
    thisYearCapDays: number
    thisYearRemainingDays: number
    children: ChildLeaveEntitlement[]
}
