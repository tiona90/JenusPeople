import { describe, expect, it } from 'vitest'
import { thisWeekActionLabel } from './timesheet-week'

describe('thisWeekActionLabel', () => {
    it('starts a week with no sheet', () => {
        expect(thisWeekActionLabel(null)).toBe('Start this week')
        expect(thisWeekActionLabel(undefined)).toBe('Start this week')
    })

    it('continues a week the employee can still edit, a rejected one included', () => {
        expect(thisWeekActionLabel('Draft')).toBe('Continue this week')
        expect(thisWeekActionLabel('Rejected')).toBe('Continue this week')
    })

    it('views a week that is with the approver or done', () => {
        expect(thisWeekActionLabel('Submitted')).toBe('View this week')
        expect(thisWeekActionLabel('Resubmitted')).toBe('View this week')
        expect(thisWeekActionLabel('Approved')).toBe('View this week')
    })
})
