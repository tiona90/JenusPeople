import { describe, expect, it } from 'vitest'
import { describeMismatch, describeMismatchCount, describeShortDay } from './daily-hours'

/*
 * The server decides (DailyHoursRule); the client only words it. A missing or null
 * figure — an older API, or nothing to say — renders nothing.
 */
describe('describeShortDay', () => {
    it('words the minutes short', () => {
        expect(describeShortDay(90)).toBe('1h 30m short')
        expect(describeShortDay(480)).toBe('8h short')
        expect(describeShortDay(16)).toBe('16 min short')
    })
    it('says nothing without a figure', () => {
        expect(describeShortDay(null)).toBeNull()
        expect(describeShortDay(undefined)).toBeNull()
    })
})

describe('describeMismatch', () => {
    it('quotes both sides', () => {
        expect(describeMismatch(8, 390, 90)).toBe('Logged 8h · attended 6h 30m')
        expect(describeMismatch(6, 480, -120)).toBe('Logged 6h · attended 8h')
        expect(describeMismatch(7.5, 300, 150)).toBe('Logged 7h 30m · attended 5h')
    })
    it('says when nothing was attended', () => {
        expect(describeMismatch(8, 0, 480)).toBe('Logged 8h · no attendance')
    })
    it('says nothing when the day agrees', () => {
        expect(describeMismatch(8, 480, null)).toBeNull()
        expect(describeMismatch(8, undefined, undefined)).toBeNull()
    })
})

describe('describeMismatchCount', () => {
    it('counts days', () => {
        expect(describeMismatchCount(1)).toBe("1 day doesn't match attendance")
        expect(describeMismatchCount(3)).toBe("3 days don't match attendance")
    })
    it('says nothing for none', () => {
        expect(describeMismatchCount(0)).toBeNull()
        expect(describeMismatchCount(undefined)).toBeNull()
    })
})
