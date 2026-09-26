import { formatBreakMinutes } from './break-policy'

/**
 * Wording for the server's short-day and timesheet-mismatch verdicts
 * (`Application/Attendance/Support/DailyHoursRule.cs`). Nothing here decides
 * anything: the target, the leave, the working week and the 15-minute grace are
 * all applied on the server, and every function renders nothing for a missing
 * figure, so an API predating the fields shows no flag rather than a false one.
 */

/** "1h 30m short", or null when there is nothing to say. */
export function describeShortDay(shortByMinutes: number | null | undefined): string | null {
    if (shortByMinutes === null || shortByMinutes === undefined) return null
    return `${formatBreakMinutes(shortByMinutes)} short`
}

/** "Logged 8h · attended 6h 30m", or null when the day agrees with attendance. */
export function describeMismatch(
    loggedHours: number,
    attendedMinutes: number | null | undefined,
    mismatchMinutes: number | null | undefined,
): string | null {
    if (mismatchMinutes === null || mismatchMinutes === undefined) return null
    const logged = formatBreakMinutes(Math.round(loggedHours * 60))
    const attended = attendedMinutes ? `attended ${formatBreakMinutes(attendedMinutes)}` : 'no attendance'
    return `Logged ${logged} · ${attended}`
}

/** "2 days don't match attendance", or null for none. */
export function describeMismatchCount(count: number | null | undefined): string | null {
    if (!count) return null
    return count === 1 ? "1 day doesn't match attendance" : `${count} days don't match attendance`
}
