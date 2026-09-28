import { describe, expect, it } from 'vitest'
import { copyableWorkTaskId, projectIdForTask, retainedWorkTaskId, taskOptionLabel, taskOptionsForRow } from './timesheet-tasks'
import type { TimesheetTaskOption } from './types'

const opt = (o: Partial<TimesheetTaskOption>): TimesheetTaskOption => ({
    id: 1, title: 'sdf', projectId: 7, projectCode: 'PAY-002', targetHours: 24, loggedHours: 8, isClosed: false, ...o,
})
const payroll = opt({ id: 1, projectId: 7 })
const crm = opt({ id: 2, projectId: 8, projectCode: 'CRM', title: 'crm' })
const closed = opt({ id: 3, projectId: 7, isClosed: true, title: 'old' })

// Task is the row's first pick and decides its project, so the list is never
// narrowed by a project the row happens to hold.
describe('taskOptionsForRow', () => {
    it('offers every open task whatever the row project', () => {
        expect(taskOptionsForRow([payroll, crm], '').map((o) => o.id)).toEqual([1, 2])
    })
    it('hides a closed task unless the row already carries it', () => {
        expect(taskOptionsForRow([payroll, closed], '').map((o) => o.id)).toEqual([1])
        expect(taskOptionsForRow([payroll, closed], '3').map((o) => o.id)).toEqual([1, 3])
    })
    it('drops a legacy task with no project, which no row could save', () => {
        expect(taskOptionsForRow([payroll, opt({ id: 5, projectId: null })], '').map((o) => o.id)).toEqual([1])
    })
})

describe('projectIdForTask', () => {
    it('is the picked task project', () => expect(projectIdForTask('2', [payroll, crm])).toBe('8'))
    it('is nothing for no task, or one not listed yet', () => {
        expect(projectIdForTask('', [payroll])).toBeNull()
        expect(projectIdForTask('9', [payroll])).toBeNull()
    })
})

describe('retainedWorkTaskId', () => {
    it('keeps a task on the new project', () => expect(retainedWorkTaskId('1', '7', [payroll])).toBe('1'))
    it('drops a task the new project does not match', () => expect(retainedWorkTaskId('1', '8', [payroll])).toBe(''))
    it('keeps an unknown id while the options are still loading', () => expect(retainedWorkTaskId('1', '8', [])).toBe('1'))
})

describe('copyableWorkTaskId', () => {
    it('carries an open task', () => expect(copyableWorkTaskId('1', [payroll])).toBe('1'))
    it('drops a closed or unlisted one', () => {
        expect(copyableWorkTaskId('3', [closed])).toBe('')
        expect(copyableWorkTaskId('9', [payroll])).toBe('')
    })
})

describe('taskOptionLabel', () => {
    it('reads code, title and progress', () => expect(taskOptionLabel(payroll)).toBe('PAY-002 · sdf — 16h left'))
    it('marks a closed task', () => expect(taskOptionLabel(closed)).toBe('PAY-002 · old (closed)'))
    it('says over', () => expect(taskOptionLabel(opt({ loggedHours: 30 }))).toBe('PAY-002 · sdf — 6h over'))
    it('says logged with no target', () => expect(taskOptionLabel(opt({ targetHours: null }))).toBe('PAY-002 · sdf — 8h logged'))
})

// Still open, but the owner was taken off it since a row named it.
const unassigned = opt({ id: 4, projectId: 7, isAssigned: false, title: 'moved on' })

describe('a task the owner was taken off', () => {
    it('is offered only to the row that already names it', () => {
        expect(taskOptionsForRow([payroll, unassigned], '').map((o) => o.id)).toEqual([1])
        expect(taskOptionsForRow([payroll, unassigned], '4').map((o) => o.id)).toEqual([1, 4])
    })
    it('is not carried by a copy', () => expect(copyableWorkTaskId('4', [unassigned])).toBe(''))
    it('says so in its label', () => expect(taskOptionLabel(unassigned)).toBe('PAY-002 · moved on (no longer yours)'))
    it('reads a missing flag from an older API as assigned', () => {
        expect(copyableWorkTaskId('1', [{ ...payroll, isAssigned: undefined }])).toBe('1')
    })
})
