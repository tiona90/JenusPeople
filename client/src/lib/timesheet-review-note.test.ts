import { describe, expect, it } from 'vitest'
import type { TimesheetStatusHistory } from './types'
import { currentReviewNote, latestCommentByTimesheet, reviewNoteLabel } from './timesheet-review-note'

function row(overrides: Partial<TimesheetStatusHistory>): TimesheetStatusHistory {
    return {
        id: 'h-1',
        timesheetId: 'ts-1',
        employeeId: 'p-1',
        employeeName: 'Athos Lamprou',
        changedByUserId: 'u-hr',
        changedByUserName: 'Helen HR',
        oldStatus: 'Approved',
        newStatus: 'Rejected',
        comment: 'Hours on Wednesday do not match the project log.',
        changedAt: '2026-09-25T08:00:00Z',
        ...overrides,
    }
}

describe('latestCommentByTimesheet', () => {
    it('keeps the newest row that carries a comment, per timesheet', () => {
        const map = latestCommentByTimesheet([
            row({ id: 'old', comment: 'First rejection', newStatus: 'Rejected', changedAt: '2026-09-20T08:00:00Z' }),
            row({ id: 'silent', comment: null, oldStatus: 'Submitted', newStatus: 'Approved', changedAt: '2026-09-24T08:00:00Z' }),
            row({ id: 'newest', changedAt: '2026-09-25T08:00:00Z' }),
            row({ id: 'other', timesheetId: 'ts-2', changedAt: '2026-09-26T08:00:00Z' }),
        ])
        expect(map.get('ts-1')?.id).toBe('newest')
        expect(map.get('ts-2')?.id).toBe('other')
    })
})

describe('reviewNoteLabel', () => {
    it('names a cancelled approval', () => {
        expect(reviewNoteLabel(row({}))).toBe('Approval cancelled by Helen HR')
    })

    it('still names a cancelled approval written before it returned the sheet to the employee', () => {
        expect(reviewNoteLabel(row({ newStatus: 'Submitted' }))).toBe('Approval cancelled by Helen HR')
    })

    it('names a rejection', () => {
        expect(reviewNoteLabel(row({ oldStatus: 'Submitted', newStatus: 'Rejected', changedByUserName: 'Mark Manager' })))
            .toBe('Rejected by Mark Manager')
    })

    it('falls back to a plain note for anything else', () => {
        expect(reviewNoteLabel(row({ oldStatus: 'Submitted', newStatus: 'Approved' }))).toBe('Note from Helen HR')
    })
})

describe('currentReviewNote', () => {
    it('is the note that put the sheet in its present status', () => {
        const note = row({})
        expect(currentReviewNote('Rejected', note)).toBe(note)
        expect(currentReviewNote('Submitted', row({ newStatus: 'Submitted' }))).not.toBeUndefined()
    })

    it('is nothing once the sheet has moved on', () => {
        // A rejection reason is stale on a sheet that was resubmitted, and a sent-back
        // reason is stale once the manager approves again.
        expect(currentReviewNote('Resubmitted', row({ newStatus: 'Rejected' }))).toBeUndefined()
        expect(currentReviewNote('Approved', row({}))).toBeUndefined()
        expect(currentReviewNote('Submitted', undefined)).toBeUndefined()
    })
})
