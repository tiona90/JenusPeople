import { describe, expect, it } from 'vitest'
import { isLeaveTypeOffered } from './parental-leave'
import type { Gender, LeaveType } from './types'

function leaveType(name: string): LeaveType {
    return {
        id: 1,
        name,
        requiresApproval: true,
        isActive: true,
        affectsBalance: false,
        icon: '👶',
        colorKey: 'maternity',
        description: '',
        paid: true,
        attachmentPolicy: 'Required',
        defaultAllowance: 90,
        allowanceUnit: 'days/event',
        maxCarryoverDays: 0,
        perChildEntitlement: false,
        perChildTotalWeeks: 0,
        perChildWeeksPerYear: 0,
        childEligibleUntilAge: 0,
        accrualNotes: '',
        minNoticeDays: 0,
        maxConsecutiveDays: 0,
        halfDayAllowed: false,
        eligibilityNotes: '',
        eligibilityScope: 'Limited',
    }
}

const maternity = leaveType('Maternity Leave')
const paternity = leaveType('Paternity Leave')
const annual = leaveType('Annual Leave')

function offered(type: LeaveType, gender: Gender | null | undefined, hasEligibleChild: boolean) {
    return isLeaveTypeOffered(type, gender, hasEligibleChild)
}

describe('isLeaveTypeOffered', () => {
    it('offers maternity leave to a female employee with an eligible child', () => {
        expect(offered(maternity, 'Female', true)).toBe(true)
    })

    it('withholds maternity leave from a male employee', () => {
        expect(offered(maternity, 'Male', true)).toBe(false)
    })

    it('offers paternity leave to a male employee with an eligible child', () => {
        expect(offered(paternity, 'Male', true)).toBe(true)
    })

    it('withholds paternity leave from a female employee', () => {
        expect(offered(paternity, 'Female', true)).toBe(false)
    })

    it('withholds both parental types when no child is eligible', () => {
        expect(offered(maternity, 'Female', false)).toBe(false)
        expect(offered(paternity, 'Male', false)).toBe(false)
    })

    /**
     * The fail-open rule the server makes too: null is "nobody has entered it",
     * which is every account predating the column, not "neither".
     */
    it('offers both parental types when the gender is not specified', () => {
        expect(offered(maternity, null, true)).toBe(true)
        expect(offered(paternity, undefined, true)).toBe(true)
    })

    it('still requires an eligible child when the gender is not specified', () => {
        expect(offered(maternity, null, false)).toBe(false)
    })

    it('leaves every other leave type alone', () => {
        expect(offered(annual, 'Male', false)).toBe(true)
        expect(offered(annual, null, false)).toBe(true)
    })

    /** Matched the way the server matches a system leave type by name. */
    it('matches the parental type names case-insensitively and ignores surrounding space', () => {
        expect(offered(leaveType('  maternity leave '), 'Male', true)).toBe(false)
    })
})
