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
    accrualNotes: string
    minNoticeDays: number
    maxConsecutiveDays: number
    halfDayAllowed: boolean
    eligibilityNotes: string
    eligibilityScope: EligibilityScope
}
