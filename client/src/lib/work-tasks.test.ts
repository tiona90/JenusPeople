import { describe, expect, it } from 'vitest'
import type { WorkTask } from './types'
import { filterTasks, isOverdue, openCount, todayIso } from './work-tasks'

const base: WorkTask = {
    id: 1, title: 't', description: null, departmentId: 1, departmentName: 'Sales', projectId: 10, projectName: 'CRM Rollout',
    assigneeId: 'me', assigneeName: 'Me', createdById: 'boss', createdByName: 'Boss',
    dueDate: null, priority: 'Normal', status: 'ToDo',
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
            t({ id: 2, assigneeId: 'x', createdById: 'me' }),
            t({ id: 3, assigneeId: 'x', createdById: 'y', departmentId: 2 }),
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
        const tasks = [t({ id: 1 }), t({ id: 2, status: 'Done' }), t({ id: 3, assigneeId: 'x', createdById: 'me' })]
        expect(openCount(tasks, 'assigned', 'me')).toBe(1)
        expect(openCount(tasks, 'created', 'me')).toBe(1)
        expect(openCount(tasks, 'all', 'me')).toBe(2)
    })
})
