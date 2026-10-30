/**
 * What a `notificationsUpdated` event says changed — the server's
 * `API.Hubs.NotificationKind`. Keep the two in step.
 */
export type NotificationKind = 'leave' | 'timesheet' | 'task'

const KEYS_BY_KIND: Record<NotificationKind, string[][]> = {
    leave: [['leaveStatusHistories'], ['annualLeaves'], ['teamAwayThisWeekCount']],
    timesheet: [['timesheets'], ['timesheetStatusHistories']],
    // A task's logged hours are timesheet rows, and Log time writes them.
    task: [['work-tasks'], ['timesheets']],
}

const EVERYTHING = Object.values(KEYS_BY_KIND).flat()

function isKind(kind: string | undefined): kind is NotificationKind {
    return kind !== undefined && kind in KEYS_BY_KIND
}

/**
 * The query-key prefixes to invalidate for the kinds an event batch carried. An
 * event with no kind — an API predating them, or the refresh after a reconnect —
 * or one this client does not know refreshes everything, which is what every
 * event used to do.
 */
export function keysForKinds(kinds: Iterable<string | undefined>): string[][] {
    const seen = new Map<string, string[]>()
    for (const kind of kinds) {
        for (const key of isKind(kind) ? KEYS_BY_KIND[kind] : EVERYTHING) {
            seen.set(key.join('/'), key)
        }
    }
    return [...seen.values()]
}

/**
 * Folds a burst of events into one call: the first starts a timer, and every
 * kind seen before it fires is handed over together. A bulk approve of 30 sheets
 * sends 30 events, and each one used to cancel and refire the whole refresh.
 */
export function coalesce(run: (kinds: Set<string | undefined>) => void, ms: number): (kind?: string) => void {
    let pending = new Set<string | undefined>()
    let timer: ReturnType<typeof setTimeout> | undefined
    return (kind) => {
        pending.add(kind)
        if (timer) return
        timer = setTimeout(() => {
            const kinds = pending
            pending = new Set()
            timer = undefined
            run(kinds)
        }, ms)
    }
}
