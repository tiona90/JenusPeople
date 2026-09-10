export type AttachmentPolicy = 'None' | 'Optional' | 'Required'
export type EligibilityScope = 'All' | 'Limited'

export interface LeaveType {
    id: number
    name: string
    requiresApproval: boolean
    isActive: boolean
    affectsBalance: boolean
    icon: string
    colorKey: string
    description: string
    paid: boolean
    attachmentPolicy: AttachmentPolicy
    defaultAllowance: number
    allowanceUnit: string
    /** Unused days of this type that survive the year-end rollover. 0 = none carry. */
    maxCarryoverDays: number
    /** When true this type's budget is per child, not per employee — see perChild* below. */
    perChildEntitlement: boolean
    /** Lifetime weeks per eligible child. 18 for paternity leave. */
    perChildTotalWeeks: number
    /** Weeks per eligible child per leave year. 5 for paternity leave. */
    perChildWeeksPerYear: number
    /** The age at which a child stops being eligible. 15 for paternity leave. */
    childEligibleUntilAge: number
    accrualNotes: string
    minNoticeDays: number
    maxConsecutiveDays: number
    halfDayAllowed: boolean
    eligibilityNotes: string
    eligibilityScope: EligibilityScope
    /**
     * One of the seeded types the app depends on by name (Annual, Maternity,
     * Paternity): it cannot be renamed or deleted, though every other setting on
     * it stays editable. Server-derived from the name, so there is no list to keep
     * in step here — and the server enforces it either way, so this only decides
     * which controls to lock. Optional because test fixtures predate it.
     */
    isSystem?: boolean
}
