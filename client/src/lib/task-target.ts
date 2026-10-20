import { breakAllowanceMinutes, parseTimeMinutes } from './break-policy'
import type { AppSettings } from './types'

/*
 * A task's target hours worked out from its start and due dates: the working days
 * from one to the other, both included, times the working day the Organization
 * settings describe net of the break. That is one person's effort, whatever the
 * number of assignees. The dialog fills the field with it and the user may type
 * over it; the server stores whatever is sent and derives nothing.
 *
 * "Working day" is the server's reading in Application/Settings/Support/WorkingWeek.cs
 * (the weekday preset, then public holidays) and the day's length that of
 * WorkingDaySchedule.ScheduledMinutes — keep the three in step.
 */

type ScheduleSettings = Partial<Pick<AppSettings,
    'workingDays' | 'workingDaysCustom' | 'workingHoursStart' | 'workingHoursEnd'
    | 'breakMode' | 'breakStart' | 'breakEnd' | 'breakMinutes'>>

// Date.getUTCDay() is Sunday=0 .. Saturday=6, as DayOfWeek is.
const DAY_TOKENS = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat']

/** Whether a weekday (0 = Sunday) is worked under the configured preset; public holidays aside. */
export function isConfiguredWorkingDay(settings: ScheduleSettings, day: number): boolean {
    if (settings.workingDays === 'custom') {
        return (settings.workingDaysCustom ?? '')
            .split(',')
            .map((t) => t.trim().toLowerCase())
            .includes(DAY_TOKENS[day])
    }
    switch (settings.workingDays) {
        case 'mon-sat': return day !== 0
        case 'sun-fri': return day !== 6
        default: return day !== 0 && day !== 6 // mon-fri
    }
}

/** A `YYYY-MM-DD` date as a UTC midnight, or null for anything else. */
function parseDate(value: string): Date | null {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
    const date = new Date(`${value}T00:00:00Z`)
    return Number.isNaN(date.getTime()) ? null : date
}

/**
 * Working days from `start` to `due`, both included, or null when either is not a
 * date or the start comes after the due date. `holidays` holds `YYYY-MM-DD` keys.
 */
export function workingDaysBetween(start: string, due: string, settings: ScheduleSettings, holidays: ReadonlySet<string>): number | null {
    const from = parseDate(start)
    const to = parseDate(due)
    if (!from || !to || from > to) return null
    let count = 0
    for (const day = new Date(from); day <= to; day.setUTCDate(day.getUTCDate() + 1)) {
        if (isConfiguredWorkingDay(settings, day.getUTCDay()) && !holidays.has(day.toISOString().slice(0, 10))) count++
    }
    return count
}

/** The working day net of the break, in minutes; null when the hours are not a day. */
export function dailyWorkingMinutes(settings: ScheduleSettings): number | null {
    const start = parseTimeMinutes(settings.workingHoursStart)
    const end = parseTimeMinutes(settings.workingHoursEnd)
    if (start === null || end === null || end <= start) return null
    const net = end - start - breakAllowanceMinutes(settings)
    return net > 0 ? net : null
}

export interface TargetEstimate {
    /** Whole hours, as the field takes them: rounded up, at least 1, at most `max`. */
    hours: number
    workingDays: number
    dailyMinutes: number
}

/**
 * The target hours the dates come to, or null when there is nothing to suggest:
 * a date missing or out of order, no working schedule, or no working day between them.
 */
export function estimateTargetHours(
    start: string, due: string, settings: ScheduleSettings | undefined, holidays: ReadonlySet<string>, max: number,
): TargetEstimate | null {
    if (!settings) return null
    const workingDays = workingDaysBetween(start, due, settings, holidays)
    const dailyMinutes = dailyWorkingMinutes(settings)
    if (!workingDays || dailyMinutes === null) return null
    const hours = Math.min(max, Math.max(1, Math.ceil((workingDays * dailyMinutes) / 60)))
    return { hours, workingDays, dailyMinutes }
}

/** "5 working days × 8h" — the helper text under the field. */
export function describeEstimate(estimate: TargetEstimate): string {
    const h = Math.floor(estimate.dailyMinutes / 60)
    const m = estimate.dailyMinutes % 60
    const perDay = m === 0 ? `${h}h` : `${h}h ${m}m`
    return `${estimate.workingDays} working day${estimate.workingDays === 1 ? '' : 's'} × ${perDay}`
}

/** Every calendar year a `YYYY-MM-DD` range touches, oldest first; at most a few. */
export function yearsBetween(start: string, due: string): number[] {
    const from = parseDate(start)
    const to = parseDate(due)
    if (!from || !to || from > to) return []
    const years: number[] = []
    for (let y = from.getUTCFullYear(); y <= to.getUTCFullYear() && years.length < 5; y++) years.push(y)
    return years
}
