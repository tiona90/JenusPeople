import { describe, expect, it } from 'vitest'
import { buildTimesheetsCsv, type TimesheetCsvSource } from './timesheet-csv'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'

const TIMESHEET = {
    id: 'ts-1',
    employeeName: 'Ada Lovelace',
    periodStart: '2026-08-31T00:00:00',
    periodEnd: '2026-09-06T00:00:00',
    totalHours: 5,
    status: 'Approved',
    submittedAt: null,
} as Timesheet

function entry(projectTypeId: number | null, projectComponentId: number | null, hours: number, workTaskId: number | null = null): TimesheetEntry {
    return {
        id: `e-${projectTypeId}-${projectComponentId}-${hours}`,
        timesheetId: 'ts-1',
        projectId: 1,
        projectTypeId,
        projectComponentId,
        date: '2026-09-02T00:00:00',
        hoursWorked: hours,
        notes: 'Triage',
        workTaskId,
    } as TimesheetEntry
}

function csv(entries: TimesheetEntry[]): string[][] {
    const source: TimesheetCsvSource = { timesheet: TIMESHEET, entries, departmentName: 'Engineering' }
    return buildTimesheetsCsv(
        [source],
        new Map([[1, { code: 'APL', name: 'Apollo' }]]),
        new Map([[10, { name: 'Support' }]]),
        new Map([[20, { name: 'Lasernet' }]]),
        new Map([
            [7, {
                title: 'Call', description: 'Ring the bank', status: 'InProgress', priority: 'High',
                startDate: '2026-09-28', dueDate: '2026-09-29', targetHours: 1, loggedHours: 8,
                createdByName: 'Grace Hopper', createdAtUtc: '2026-09-29T08:15:00Z',
            }],
            [8, { title: 'Report', description: null, dueDate: null, targetHours: 16, loggedHours: 4, createdAtUtc: '2026-09-01T10:00:00Z' }],
        ]),
    )
        .split('\r\n')
        .map((line) => line.split(','))
}

/** Column index of a header, so these tests survive the next inserted column. */
function col(header: string[], name: string): number {
    const i = header.indexOf(name)
    expect(i).toBeGreaterThanOrEqual(0)
    return i
}

describe('buildTimesheetsCsv', () => {
    it('names the entry type and component in their own columns', () => {
        const [header, row] = csv([entry(10, 20, 3)])

        expect(row[col(header, 'Type')]).toBe('Support')
        expect(row[col(header, 'Component')]).toBe('Lasernet')
    })

    // Every entry predating these fields has neither, as does anything logged
    // against an unclassified project — blank, not "undefined".
    it('leaves them blank for an entry that has neither', () => {
        const [header, row] = csv([entry(null, null, 3)])

        expect(row[col(header, 'Type')]).toBe('')
        expect(row[col(header, 'Component')]).toBe('')
    })

    // The empty-timesheet row is padded by hand, so it drifts out of step with
    // the header the moment a column is added.
    it('keeps every row the same width as the header', () => {
        const rows = csv([entry(10, 20, 3), entry(null, null, 2)])
        const empty = csv([])

        for (const row of [...rows, ...empty]) {
            expect(row).toHaveLength(rows[0].length)
        }
    })

    it('describes the approved project a row was logged against', () => {
        const [header, row] = csv([entry(10, 20, 3, 7)])

        expect(header).not.toContain('Project Code')
        expect(header.some((h) => h.startsWith('Task'))).toBe(false)
        expect(row[col(header, 'Approved Project')]).toBe('Call')
        expect(row[col(header, 'Approved Project Description')]).toBe('Ring the bank')
        expect(row[col(header, 'Approved Project Status')]).toBe('In progress')
        expect(row[col(header, 'Approved Project Priority')]).toBe('High')
        expect(row[col(header, 'Approved Project Start Date')]).toBe('2026-09-28')
        expect(row[col(header, 'Approved Project Due Date')]).toBe('2026-09-29')
        expect(row[col(header, 'Approved Project Target Hours')]).toBe('1')
        expect(row[col(header, 'Approved Project Logged Hours')]).toBe('8')
        expect(row[col(header, 'Approved Project Hours Over Target')]).toBe('7')
        expect(row[col(header, 'Approved Project Created By')]).toBe('Grace Hopper')
        expect(row[col(header, 'Approved Project Created')]).toBe('2026-09-29')
        expect(row[col(header, 'Project Name')]).toBe('Apollo')
    })

    it('leaves "over" blank under target, and every approved-project cell blank with none', () => {
        const [header, under, none] = csv([entry(10, 20, 3, 8), { ...entry(null, null, 2), date: '2026-09-03T00:00:00' }])

        expect(under[col(header, 'Approved Project Target Hours')]).toBe('16')
        expect(under[col(header, 'Approved Project Hours Over Target')]).toBe('')
        expect(under[col(header, 'Approved Project Due Date')]).toBe('')
        for (const name of header.filter((h) => h.startsWith('Approved Project'))) {
            expect(none[col(header, name)]).toBe('')
        }
    })

    it('puts the empty-timesheet marker under the notes column', () => {
        const [header, row] = csv([])

        expect(row[col(header, 'Notes (what was worked on)')]).toBe('(no entries)')
        expect(row[col(header, 'Status')]).toBe('Approved')
    })
})
