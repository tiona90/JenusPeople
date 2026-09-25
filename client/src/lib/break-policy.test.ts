import { describe, expect, it } from 'vitest'
import { breakAllowanceMinutes, breakClock, describeBreakPolicy, formatClock } from './break-policy'
import type { AppSettings } from './types'

/*
 * The client's reading of the break setting, mirroring what
 * `WorkingDaySchedule.BreakMinutes` makes of the same columns: the fixed window's
 * length, the flexible duration, or nothing — with a figure that does not fit the
 * working day reading as no break, so a screen never quotes an allowance the server
 * does not count.
 */
type BreakSettings = Pick<AppSettings, 'workingHoursStart' | 'workingHoursEnd' | 'breakMode' | 'breakStart' | 'breakEnd' | 'breakMinutes'>

function settings(over: Partial<BreakSettings> = {}): BreakSettings {
    return {
        workingHoursStart: '08:00',
        workingHoursEnd: '17:00',
        breakMode: 'none',
        breakStart: '13:00',
        breakEnd: '14:00',
        breakMinutes: 0,
        ...over,
    }
}

describe('breakAllowanceMinutes', () => {
    it('is the window length for a fixed break', () => {
        expect(breakAllowanceMinutes(settings({ breakMode: 'fixed', breakStart: '13:00', breakEnd: '14:00' }))).toBe(60)
        expect(breakAllowanceMinutes(settings({ breakMode: 'fixed', breakStart: '12:15', breakEnd: '12:45' }))).toBe(30)
    })

    it('is the duration for a flexible break', () => {
        expect(breakAllowanceMinutes(settings({ breakMode: 'flexible', breakMinutes: 45 }))).toBe(45)
    })

    it('is nothing when the break is off, whatever the other columns hold', () => {
        expect(breakAllowanceMinutes(settings({ breakMode: 'none', breakMinutes: 45 }))).toBe(0)
    })

    it('reads a window or duration that does not fit the day as no break, like the server', () => {
        expect(breakAllowanceMinutes(settings({ breakMode: 'fixed', breakStart: '14:00', breakEnd: '13:00' }))).toBe(0)
        expect(breakAllowanceMinutes(settings({ breakMode: 'fixed', breakStart: '07:00', breakEnd: '08:30' }))).toBe(0)
        expect(breakAllowanceMinutes(settings({ breakMode: 'fixed', breakStart: '16:30', breakEnd: '17:30' }))).toBe(0)
        expect(breakAllowanceMinutes(settings({ breakMode: 'flexible', breakMinutes: 9 * 60 }))).toBe(0)
        expect(breakAllowanceMinutes(settings({ breakMode: 'flexible', breakMinutes: -5 }))).toBe(0)
    })

    it('reads an API predating the columns as no break', () => {
        expect(breakAllowanceMinutes({ workingHoursStart: '08:00', workingHoursEnd: '17:00' })).toBe(0)
        expect(breakAllowanceMinutes(undefined)).toBe(0)
    })
})

describe('describeBreakPolicy', () => {
    it('names the window for a fixed break', () => {
        expect(describeBreakPolicy(settings({ breakMode: 'fixed', breakStart: '13:00', breakEnd: '14:00' }))).toBe('Break 13:00–14:00 (1h)')
        expect(describeBreakPolicy(settings({ breakMode: 'fixed', breakStart: '12:15', breakEnd: '12:45' }))).toBe('Break 12:15–12:45 (30 min)')
    })

    it('names the allowance for a flexible break', () => {
        expect(describeBreakPolicy(settings({ breakMode: 'flexible', breakMinutes: 45 }))).toBe('Break allowance 45 min')
        expect(describeBreakPolicy(settings({ breakMode: 'flexible', breakMinutes: 90 }))).toBe('Break allowance 1h 30m')
    })

    it('is null when there is no break to describe', () => {
        expect(describeBreakPolicy(settings())).toBeNull()
        expect(describeBreakPolicy(undefined)).toBeNull()
        expect(describeBreakPolicy(settings({ breakMode: 'fixed', breakStart: '14:00', breakEnd: '13:00' }))).toBeNull()
    })
})

describe('breakClock', () => {
    const since = '2026-09-25T12:00:00.000Z'
    const at = (seconds: number) => new Date(since).getTime() + seconds * 1000

    it('counts the allowance down while the break runs', () => {
        const clock = breakClock({ onBreakSince: since, totalBreakMinutes: 0 }, 60, at(61))
        expect(clock).toEqual({ kind: 'remaining', seconds: 3539 })
        expect(formatClock(clock!.seconds)).toBe('58:59')
    })

    it('takes the breaks already taken today off what is left', () => {
        const clock = breakClock({ onBreakSince: since, totalBreakMinutes: 45 }, 60, at(0))
        expect(clock).toEqual({ kind: 'remaining', seconds: 900 })
    })

    it('counts up the time over once the allowance is used', () => {
        const clock = breakClock({ onBreakSince: since, totalBreakMinutes: 50 }, 60, at(12 * 60 + 5))
        expect(clock).toEqual({ kind: 'over', seconds: 125 })
    })

    it('reads exactly on the allowance as nothing left, not over', () => {
        expect(breakClock({ onBreakSince: since, totalBreakMinutes: 0 }, 60, at(3600)))
            .toEqual({ kind: 'remaining', seconds: 0 })
    })

    it('counts the running break up when no allowance is configured', () => {
        expect(breakClock({ onBreakSince: since, totalBreakMinutes: 30 }, 0, at(90)))
            .toEqual({ kind: 'elapsed', seconds: 90 })
    })

    it('has nothing to show when not on a break', () => {
        expect(breakClock({ onBreakSince: null, totalBreakMinutes: 10 }, 60, at(0))).toBeNull()
    })

    it('never counts a clock skewed ahead of the start as negative time', () => {
        expect(breakClock({ onBreakSince: since, totalBreakMinutes: 0 }, 0, at(-5)))
            .toEqual({ kind: 'elapsed', seconds: 0 })
    })

    it('formats an hour or more with the hour in front', () => {
        expect(formatClock(0)).toBe('00:00')
        expect(formatClock(3725)).toBe('1:02:05')
    })
})
