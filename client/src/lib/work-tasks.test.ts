import { describe, expect, it } from 'vitest'
import type { WorkTask } from './types'
import { DEFAULT_TASK_SETTINGS } from './task-settings'
import { SETTABLE_STATUSES, STATUS_LABELS, boardStatuses, describeTaskProgress, filterTasks, isAwaitingConfirmation, isOpenTask, isOverdue, nextStatusAction, openCount, overdueDays, taskStats, tasksToCsv, todayIso } from './work-tasks'

const base: WorkTask = {
    id: 1, title: 't', description: null, departmentId: 1, departmentName: 'Sales', projectId: 10, projectName: 'CRM Rollout', projectCode: 'CRM', projectColorKey: 'p1',
    assignees: [{ userId: 'me', displayName: 'Me' }], createdById: 'boss', createdByName: 'Boss',
    dueDate: null, targetHours: null, loggedHours: 0, isBillable: true, priority: 'Normal', status: 'ToDo',
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

    it('is never overdue with the due date hidden, whatever the date', () => {
        expect(isOverdue(t({ dueDate: '2026-09-27' }), '2026-09-28', false)).toBe(false)
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

    it('counts no days overdue with the due date hidden', () => {
        expect(overdueDays(t({ dueDate: '2026-09-25' }), '2026-09-28', false)).toBe(0)
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
            total: 5, open: 2, inProgress: 1, overdue: 1, doneThisMonth: 1, assignedToMeOpen: 1, awaitingMyConfirmation: 0,
        })
        // With the due date hidden, nothing is overdue.
        expect(taskStats(tasks, 'me', '2026-09-28', false).overdue).toBe(0)
    })

    it('offers the obvious next step for each status', () => {
        expect(nextStatusAction('ToDo')).toEqual({ label: 'Start', icon: '▶', to: 'InProgress' })
        expect(nextStatusAction('InProgress')).toEqual({ label: 'Mark done', icon: '✓', to: 'Done' })
        expect(nextStatusAction('Done')).toEqual({ label: 'Reopen', icon: '↺', to: 'InProgress' })
        expect(nextStatusAction('Cancelled')).toEqual({ label: 'Reopen', icon: '↺', to: 'ToDo' })
    })
})

describe('describeTaskProgress', () => {
    it('says what is logged and what is left', () => {
        expect(describeTaskProgress(24, 8)).toEqual({ text: '8h logged · 16h left', over: false })
    })
    it('keeps half hours to one decimal', () => {
        expect(describeTaskProgress(24, 1.5)).toEqual({ text: '1.5h logged · 22.5h left', over: false })
    })
    it('reads exactly on target as nothing left, not over', () => {
        expect(describeTaskProgress(24, 24)).toEqual({ text: '24h logged · 0h left', over: false })
    })
    it('says how far over', () => {
        expect(describeTaskProgress(24, 28)).toEqual({ text: '4h over', over: true })
    })
    it('has no remaining figure without a target', () => {
        expect(describeTaskProgress(null, 8)).toEqual({ text: '8h logged', over: false })
        expect(describeTaskProgress(null, 0)).toEqual({ text: 'nothing logged', over: false })
    })
    it('reads a missing figure from an older API as nothing logged', () => {
        expect(describeTaskProgress(24, undefined as unknown as number)).toEqual({ text: '0h logged · 24h left', over: false })
    })
})

describe('tasksToCsv', () => {
    it('writes a header and one row per task', () => {
        const csv = tasksToCsv([t({
            title: 'Chase notes', assignees: [{ userId: 'a', displayName: 'Ann' }, { userId: 'b', displayName: 'Bob' }],
            dueDate: '2026-10-09', targetHours: 24, loggedHours: 26.25, status: 'InProgress', priority: 'High',
            completedAtUtc: null,
        })])
        const [header, row] = csv.split('\r\n')
        expect(header).toBe('Title,Description,Department,Project code,Project,Status,Priority,Billable,Assignees,Created by,Due date,Target hours,Logged hours,Created,Completed')
        expect(row).toBe('Chase notes,,Sales,CRM,CRM Rollout,In progress,High,Yes,Ann; Bob,Boss,2026-10-09,24,26.3,2026-09-01,')
    })

    it('quotes commas, quotes and line breaks, and defuses formulas', () => {
        const [, row] = tasksToCsv([t({ title: '=HYPERLINK("x")', description: 'one, two\nthree' })]).split('\r\n')
        expect(row.startsWith(`"'=HYPERLINK(""x"")","one, two\nthree",`)).toBe(true)
    })

    it('leaves an unanswered billing question blank', () => {
        const [, row] = tasksToCsv([t({ isBillable: null })]).split('\r\n')
        expect(row.split(',')[7]).toBe('')
    })

    it('leaves hidden fields out of the export', () => {
        const csv = tasksToCsv([t({})], { ...DEFAULT_TASK_SETTINGS, descriptionRequirement: 'Hidden', showPriority: false })
        const header = csv.split('\r\n')[0]
        expect(header).not.toMatch(/Description/)
        expect(header).not.toMatch(/Priority/)
        expect(header).toMatch(/Title/)
    })
})

describe('awaiting confirmation', () => {
    it('is open, but never overdue — the work is in', () => {
        const waiting = t({ status: 'AwaitingConfirmation', dueDate: '2020-01-01' })
        expect(isAwaitingConfirmation(waiting)).toBe(true)
        expect(isOpenTask(waiting)).toBe(true)
        expect(isOverdue(waiting, '2026-09-29')).toBe(false)
    })

    it('is labelled, and is never offered as a status to pick', () => {
        expect(STATUS_LABELS.AwaitingConfirmation).toBe('Awaiting confirmation')
        expect(SETTABLE_STATUSES).toEqual(['ToDo', 'InProgress', 'Done', 'Cancelled'])
    })

    it("offers the assignee Withdraw as the card's next step", () => {
        expect(nextStatusAction('AwaitingConfirmation')).toEqual({ label: 'Withdraw', icon: '↺', to: 'InProgress' })
    })

    it('counts the tasks waiting for the viewer, and filters to waiting ones', () => {
        const tasks = [t({ id: 1, status: 'AwaitingConfirmation', canConfirm: true }), t({ id: 2, status: 'AwaitingConfirmation' }), t({ id: 3 })]
        expect(taskStats(tasks, 'me', '2026-09-29').awaitingMyConfirmation).toBe(1)
        expect(filterTasks(tasks, { tab: 'all', status: 'AwaitingConfirmation', departmentId: null, userId: 'me' }).map((x) => x.id)).toEqual([1, 2])
    })
})

describe('boardStatuses', () => {
    it('follows the status filter, in workflow order', () => {
        expect(boardStatuses('open', true, [])).toEqual(['ToDo', 'InProgress', 'AwaitingConfirmation'])
        expect(boardStatuses('any', true, [])).toEqual(['ToDo', 'InProgress', 'AwaitingConfirmation', 'Done', 'Cancelled'])
        expect(boardStatuses('Done', true, [])).toEqual(['Done'])
    })

    it('drops the waiting column when confirmation is off and nothing waits', () => {
        expect(boardStatuses('open', false, [])).toEqual(['ToDo', 'InProgress'])
        expect(boardStatuses('open', false, [t({ status: 'AwaitingConfirmation' })])).toEqual(['ToDo', 'InProgress', 'AwaitingConfirmation'])
    })
})
