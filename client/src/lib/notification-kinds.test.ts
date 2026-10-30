import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { coalesce, keysForKinds } from './notification-kinds'

/**
 * Every notificationsUpdated event used to refetch six query families — leave,
 * timesheets, tasks and their histories — whatever had changed, and a bulk
 * approve of 30 sheets sent 30 events, each one cancelling and refiring the lot.
 * The server now says what changed, and a burst is folded into one refresh.
 */
describe('keysForKinds', () => {
    it('refreshes only what a leave event touches', () => {
        expect(keysForKinds(['leave'])).toEqual([['leaveStatusHistories'], ['annualLeaves'], ['teamAwayThisWeekCount']])
    })

    it('refreshes only what a timesheet event touches', () => {
        expect(keysForKinds(['timesheet'])).toEqual([['timesheets'], ['timesheetStatusHistories']])
    })

    it('refreshes tasks, and the timesheets their logged hours live on, for a task event', () => {
        expect(keysForKinds(['task'])).toEqual([['work-tasks'], ['timesheets']])
    })

    it('refreshes everything for an event that says nothing (an API predating kinds) or something unknown', () => {
        const everything = keysForKinds([undefined])
        expect(everything).toHaveLength(6)
        expect(keysForKinds(['something-new'])).toEqual(everything)
    })

    it('does not repeat a key two kinds share', () => {
        const keys = keysForKinds(['timesheet', 'task']).map((k) => k.join('/'))
        expect(new Set(keys).size).toBe(keys.length)
    })
})

describe('coalesce', () => {
    beforeEach(() => vi.useFakeTimers())
    afterEach(() => vi.useRealTimers())

    it('folds a burst into one call carrying every kind seen', () => {
        const run = vi.fn()
        const push = coalesce(run, 300)

        for (let i = 0; i < 30; i++) push('timesheet')
        push('leave')
        vi.advanceTimersByTime(300)

        expect(run).toHaveBeenCalledTimes(1)
        expect([...run.mock.calls[0][0]].sort()).toEqual(['leave', 'timesheet'])
    })

    it('starts a fresh batch after the last one ran', () => {
        const run = vi.fn()
        const push = coalesce(run, 300)

        push('leave')
        vi.advanceTimersByTime(300)
        push('task')
        vi.advanceTimersByTime(300)

        expect(run).toHaveBeenCalledTimes(2)
        expect([...run.mock.calls[1][0]]).toEqual(['task'])
    })
})
