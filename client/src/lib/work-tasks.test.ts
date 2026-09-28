import { describe, expect, it } from 'vitest'
import type { WorkTask } from './types'
import { filterTasks, isOverdue, nextStatusAction, openCount, overdueDays, taskStats, todayIso } from './work-tasks'

const base: WorkTask = {
    id: 1, title: 't', description: null, departmentId: 1, departmentName: 'Sales', projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
    assignees: [{ userId: 'me', displayName: 'Me' }], createdById: 'boss', createdByName: 'Boss',
    dueDate: null, targetHours: null, targetWeeks: null, priority: 'Normal', status: 'ToDo',
    createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
    canEdit: false, canChangeStatus: true,
}
const t = (over: Partial<WorkTask>): WorkTask => ({ ...base, ...over })

describe('work-tasks helpers', () => {
    it('reads today as a local calendar date', () => {
        expect(todayIso(new Date(2026, 8, 28, 23, 30))).toBe('2026-09-28')
    })

    it('is overdue only when open and due before today', () => {
        expect(isOverdue(t({ dueDate: '2026-09-27' }), '2026-09-28')).toBe(true)
        expect(isOverdue(t({ dueDate: '2026-09-28' }), '2026-09-28')).toBe(false)
        expect(isOverdue(t({ dueDate: '2026-09-27', status: 'Done' }), '2026-09-28')).toBe(false)
        expect(isOverdue(t({ dueDate: null }), '2026-09-28')).toBe(false)
    })

    it('filters by tab, status and department', () => {
        const tasks = [
            t({ id: 1 }),
            t({ id: 2, assignees: [{ userId: 'x', displayName: 'X' }], createdById: 'me' }),
            t({ id: 3, assignees: [{ userId: 'x', displayName: 'X' }], createdById: 'y', departmentId: 2 }),
            t({ id: 4, status: 'Done' }),
        ]
        const ids = (xs: WorkTask[]) => xs.map((x) => x.id)
        expect(ids(filterTasks(tasks, { tab: 'assigned', status: 'open', departmentId: null, userId: 'me' }))).toEqual([1])
        expect(ids(filterTasks(tasks, { tab: 'created', status: 'open', departmentId: null, userId: 'me' }))).toEqual([2])
        expect(ids(filterTasks(tasks, { tab: 'all', status: 'any', departmentId: null, userId: 'me' }))).toEqual([1, 2, 3, 4])
        expect(ids(filterTasks(tasks, { tab: 'all', status: 'Done', departmentId: null, userId: 'me' }))).toEqual([4])
        expect(ids(filterTasks(tasks, { tab: 'all', status: 'open', departmentId: 2, userId: 'me' }))).toEqual([3])
    })

    it('counts open tasks per tab', () => {
        const tasks = [t({ id: 1 }), t({ id: 2, status: 'Done' }), t({ id: 3, assignees: [{ userId: 'x', displayName: 'X' }], createdById: 'me' })]
        expect(openCount(tasks, 'assigned', 'me')).toBe(1)
        expect(openCount(tasks, 'created', 'me')).toBe(1)
        expect(openCount(tasks, 'all', 'me')).toBe(2)
    })

    it('counts a task as assigned to me when I am one of several', () => {
        const shared = t({ id: 9, createdById: 'boss', assignees: [{ userId: 'x', displayName: 'X' }, { userId: 'me', displayName: 'Me' }] })
        expect(filterTasks([shared], { tab: 'assigned', status: 'open', departmentId: null, userId: 'me' })).toHaveLength(1)
        expect(openCount([shared], 'assigned', 'x')).toBe(1)
    })

    it('searches title, description and project, ignoring case', () => {
        const tasks = [
            t({ id: 1, title: 'Chase sick notes' }),
            t({ id: 2, title: 'Other', description: 'about the NOTES' }),
            t({ id: 3, title: 'Third', projectName: 'Payroll Automation', projectCode: 'PAY' }),
        ]
        const ids = (search: string) =>
            filterTasks(tasks, { tab: 'all', status: 'any', departmentId: null, userId: 'me', search }).map((x) => x.id)
        expect(ids('notes')).toEqual([1, 2])
        expect(ids('pay')).toEqual([3])
        expect(ids('  ')).toEqual([1, 2, 3])
    })

    it('filters by priority', () => {
        const tasks = [t({ id: 1, priority: 'High' }), t({ id: 2, priority: 'Low' })]
        expect(filterTasks(tasks, { tab: 'all', status: 'any', departmentId: null, userId: 'me', priority: 'High' }).map((x) => x.id)).toEqual([1])
    })

    it('counts days overdue only for an open task past its date', () => {
        expect(overdueDays(t({ dueDate: '2026-09-25' }), '2026-09-28')).toBe(3)
        expect(overdueDays(t({ dueDate: '2026-09-28' }), '2026-09-28')).toBe(0)
        expect(overdueDays(t({ dueDate: '2026-09-25', status: 'Done' }), '2026-09-28')).toBe(0)
    })

    it('adds up the summary tiles', () => {
        const tasks = [
            t({ id: 1, status: 'ToDo', dueDate: '2026-09-01' }),
            t({ id: 2, status: 'InProgress', assignees: [{ userId: 'x', displayName: 'X' }] }),
            t({ id: 3, status: 'Done', completedAtUtc: '2026-09-10T08:00:00' }),
            t({ id: 4, status: 'Done', completedAtUtc: '2026-08-30T08:00:00' }),
            t({ id: 5, status: 'Cancelled' }),
        ]
        expect(taskStats(tasks, 'me', '2026-09-28')).toEqual({
            total: 5, open: 2, inProgress: 1, overdue: 1, doneThisMonth: 1, assignedToMeOpen: 1,
        })
    })

    it('offers the obvious next step for each status', () => {
        expect(nextStatusAction('ToDo')).toEqual({ label: 'Start', icon: '▶', to: 'InProgress' })
        expect(nextStatusAction('InProgress')).toEqual({ label: 'Mark done', icon: '✓', to: 'Done' })
        expect(nextStatusAction('Done')).toEqual({ label: 'Reopen', icon: '↺', to: 'InProgress' })
        expect(nextStatusAction('Cancelled')).toEqual({ label: 'Reopen', icon: '↺', to: 'ToDo' })
    })
})
