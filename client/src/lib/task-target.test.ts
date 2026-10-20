import { describe, expect, it } from 'vitest'
import { dailyWorkingMinutes, describeEstimate, estimateTargetHours, workingDaysBetween, yearsBetween } from './task-target'

const monFri = { workingDays: 'mon-fri', workingDaysCustom: '', workingHoursStart: '09:00', workingHoursEnd: '17:00', breakMode: 'none' as const }
const none = new Set<string>()

describe('task target hours', () => {
    it('counts working days inclusively, skipping weekends and public holidays', () => {
        // Mon 19 Oct 2026 to Fri 30 Oct 2026: ten weekdays, one of them a holiday.
        expect(workingDaysBetween('2026-10-19', '2026-10-30', monFri, none)).toBe(10)
        expect(workingDaysBetween('2026-10-19', '2026-10-30', monFri, new Set(['2026-10-28']))).toBe(9)
        expect(workingDaysBetween('2026-10-24', '2026-10-25', monFri, none)).toBe(0)
        expect(workingDaysBetween('2026-10-20', '2026-10-20', monFri, none)).toBe(1)
    })

    it('follows the working-days preset', () => {
        expect(workingDaysBetween('2026-10-19', '2026-10-25', { ...monFri, workingDays: 'mon-sat' }, none)).toBe(6)
        expect(workingDaysBetween('2026-10-19', '2026-10-25', { ...monFri, workingDays: 'custom', workingDaysCustom: 'mon,wed,fri' }, none)).toBe(3)
    })

    it('reads nothing into dates out of order or missing', () => {
        expect(workingDaysBetween('2026-10-30', '2026-10-19', monFri, none)).toBeNull()
        expect(estimateTargetHours('', '2026-10-19', monFri, none, 9999)).toBeNull()
        expect(yearsBetween('2026-10-30', '2026-10-19')).toEqual([])
    })

    it('takes the break off the working day', () => {
        expect(dailyWorkingMinutes(monFri)).toBe(480)
        expect(dailyWorkingMinutes({ ...monFri, workingHoursEnd: '18:00', breakMode: 'flexible', breakMinutes: 60 })).toBe(480)
        expect(dailyWorkingMinutes({ ...monFri, breakMode: 'fixed', breakStart: '12:00', breakEnd: '12:30' })).toBe(450)
    })

    it('multiplies working days by the day, rounding up to a whole hour', () => {
        const week = estimateTargetHours('2026-10-19', '2026-10-23', monFri, none, 9999)
        expect(week).toEqual({ hours: 40, workingDays: 5, dailyMinutes: 480 })
        expect(describeEstimate(week!)).toBe('5 working days × 8h')

        const halfHourBreak = estimateTargetHours('2026-10-19', '2026-10-19', { ...monFri, breakMode: 'flexible', breakMinutes: 30 }, none, 9999)
        expect(halfHourBreak?.hours).toBe(8)
        expect(describeEstimate(halfHourBreak!)).toBe('1 working day × 7h 30m')
    })

    it('suggests nothing when the range holds no working day', () => {
        expect(estimateTargetHours('2026-10-24', '2026-10-25', monFri, none, 9999)).toBeNull()
    })

    it('lists every year a range touches, for the holiday lookups', () => {
        expect(yearsBetween('2026-12-20', '2027-01-10')).toEqual([2026, 2027])
    })
})
