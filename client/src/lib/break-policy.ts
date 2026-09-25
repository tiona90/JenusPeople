import type { AppSettings } from './types'

/**
 * The organisation's break, as the client reads it.
 *
 * This mirrors `Application/Attendance/Support/WorkingDaySchedule.cs`, whose
 * `BreakMinutes` is what the server actually takes off the working day when it
 * judges overtime: the fixed window's length, the flexible duration, or nothing —
 * and a figure that does not fit the working day (an inverted or out-of-hours
 * window, a duration as long as the day) reads as no break rather than as a
 * negative day. Keep the two in step, the same way `leave-limits.ts` is kept in
 * step with `NoticePeriodRule`: a screen must never quote an allowance the server
 * does not count.
 *
 * Every field is optional so an API predating the columns reads as no break.
 */
export type BreakMode = 'none' | 'fixed' | 'flexible'

type BreakSettings = Partial<Pick<AppSettings,
    'workingHoursStart' | 'workingHoursEnd' | 'breakMode' | 'breakStart' | 'breakEnd' | 'breakMinutes'>>

/** "HH:mm" → minutes since midnight, or null for anything that is not a time. */
export function parseTimeMinutes(value: string | null | undefined): number | null {
    const match = /^(\d{1,2}):(\d{2})$/.exec((value ?? '').trim())
    if (!match) return null
    const hours = Number(match[1])
    const minutes = Number(match[2])
    if (hours > 23 || minutes > 59) return null
    return hours * 60 + minutes
}

/** The working day's gross length in minutes, or null when the hours are not a day. */
function workingDayMinutes(settings: BreakSettings): { start: number; end: number } | null {
    const start = parseTimeMinutes(settings.workingHoursStart)
    const end = parseTimeMinutes(settings.workingHoursEnd)
    if (start === null || end === null || end <= start) return null
    return { start, end }
}

/** The fixed window, when the mode is fixed and the window fits the working day. */
function fixedWindow(settings: BreakSettings): { start: number; end: number } | null {
    if (settings.breakMode !== 'fixed') return null
    const day = workingDayMinutes(settings)
    const start = parseTimeMinutes(settings.breakStart)
    const end = parseTimeMinutes(settings.breakEnd)
    if (start === null || end === null || end <= start) return null
    if (day && (start < day.start || end > day.end)) return null
    return { start, end }
}

/** How long a break the working day allows for, in minutes; 0 when none is set. */
export function breakAllowanceMinutes(settings: BreakSettings | undefined): number {
    if (!settings) return 0
    const day = workingDayMinutes(settings)
    const gross = day ? day.end - day.start : null

    let allowance = 0
    if (settings.breakMode === 'fixed') {
        const window = fixedWindow(settings)
        allowance = window ? window.end - window.start : 0
    } else if (settings.breakMode === 'flexible') {
        allowance = Math.max(0, settings.breakMinutes ?? 0)
    }

    // A break as long as the day leaves nothing to work: the server reads it as 0.
    return gross !== null && allowance >= gross ? 0 : allowance
}

/** "1h", "45 min", "1h 30m" — the shape the attendance surfaces already use. */
export function formatBreakMinutes(minutes: number): string {
    const hours = Math.floor(minutes / 60)
    const rest = minutes % 60
    if (hours === 0) return `${rest} min`
    if (rest === 0) return `${hours}h`
    return `${hours}h ${rest}m`
}

/**
 * The server's verdict on somebody's break in a phrase: "20 min over", "30 min
 * under", "on allowance" for a finished day exactly on it, and null when the
 * server had nothing to say (null, or a field an older API does not send). The
 * comparison itself is never made here — `WorkingDaySchedule.BreakVariance` is
 * the one rule, so the board, the dashboard and the employee's own page agree.
 */
export function describeBreakVariance(variance: number | null | undefined): string | null {
    if (variance === null || variance === undefined) return null
    if (variance === 0) return 'on allowance'
    return `${formatBreakMinutes(Math.abs(variance))} ${variance > 0 ? 'over' : 'under'}`
}

function formatTime(minutes: number): string {
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
}

/**
 * The break policy in a phrase, for the surfaces that quote it beside somebody's
 * own break time. Null when there is nothing to say — no break configured, or a
 * setting the server counts as none — so a caller renders nothing rather than
 * "Break allowance 0 min".
 */
export function describeBreakPolicy(settings: BreakSettings | undefined): string | null {
    if (!settings) return null
    const allowance = breakAllowanceMinutes(settings)
    if (allowance === 0) return null

    if (settings.breakMode === 'fixed') {
        const window = fixedWindow(settings)!
        return `Break ${formatTime(window.start)}–${formatTime(window.end)} (${formatBreakMinutes(allowance)})`
    }
    return `Break allowance ${formatBreakMinutes(allowance)}`
}

/**
 * The settings validator's message for a break as the admin has it on the form, or
 * null when it would be accepted. Mirrors `Application/Settings/Support/BreakRules.cs`
 * so Save is held rather than refused; the wording is the server's. Reads the
 * working hours from the same form, as the server reads them from the same payload,
 * and stays quiet when those are not a valid day themselves — their own rule speaks.
 */
export function breakSettingsError(settings: BreakSettings): string | null {
    const day = workingDayMinutes(settings)
    if (settings.breakMode === 'fixed') {
        const start = parseTimeMinutes(settings.breakStart)
        const end = parseTimeMinutes(settings.breakEnd)
        if (start === null) return 'Break start must be a valid time (HH:mm).'
        if (end === null) return 'Break end must be a valid time (HH:mm).'
        if (end <= start) return 'Break end must be after the break start.'
        if (day && (start < day.start || start >= day.end)) return 'Break start must fall within the working hours.'
        if (day && end > day.end) return 'Break end must fall within the working hours.'
        return null
    }
    if (settings.breakMode === 'flexible') {
        const minutes = settings.breakMinutes ?? 0
        if (minutes < 1 || (day && minutes >= day.end - day.start)) {
            return 'Break length must be at least 1 minute and shorter than the working day.'
        }
    }
    return null
}

/**
 * The live clock on a running break, in seconds. With an allowance configured it
 * counts down what is left of it (`remaining`, reaching 0 exactly on it) and then
 * counts up the time past it (`over`); with none it counts the running break up
 * (`elapsed`), since there is nothing to count down to. Null when not on a break.
 *
 * "Taken" is what `WorkingDaySchedule.BreakMinutesTaken` counts: the day's closed
 * breaks plus the open one, an idle break included, so the clock turns to "over"
 * when the server's verdict does. `totalBreakMinutes` is whole minutes, so the
 * clock can sit up to a minute off that verdict, never more.
 */
export type BreakClock = { kind: 'remaining' | 'over' | 'elapsed'; seconds: number }

export function breakClock(
    today: { onBreakSince: string | null; totalBreakMinutes: number },
    allowanceMinutes: number,
    nowMs: number,
): BreakClock | null {
    if (!today.onBreakSince) return null
    const openSeconds = Math.max(0, Math.floor((nowMs - new Date(today.onBreakSince).getTime()) / 1000))
    if (allowanceMinutes <= 0) return { kind: 'elapsed', seconds: openSeconds }
    const left = allowanceMinutes * 60 - today.totalBreakMinutes * 60 - openSeconds
    return left >= 0 ? { kind: 'remaining', seconds: left } : { kind: 'over', seconds: -left }
}

/** "58:59", or "1:02:05" from an hour up — the face of the break clock. */
export function formatClock(totalSeconds: number): string {
    const s = Math.max(0, Math.floor(totalSeconds))
    const hours = Math.floor(s / 3600)
    const mm = String(Math.floor((s % 3600) / 60)).padStart(2, '0')
    const ss = String(s % 60).padStart(2, '0')
    return hours > 0 ? `${hours}:${mm}:${ss}` : `${mm}:${ss}`
}
