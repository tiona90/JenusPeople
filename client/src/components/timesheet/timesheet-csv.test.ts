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

function entry(projectTypeId: number | null, projectComponentId: number | null, hours: number): TimesheetEntry {
    return {
        id: `e-${projectTypeId}-${projectComponentId}-${hours}`,
        timesheetId: 'ts-1',
        projectId: 1,
        projectTypeId,
        projectComponentId,
        date: '2026-09-02T00:00:00',
        hoursWorked: hours,
        notes: 'Triage',
    } as TimesheetEntry
}

function csv(entries: TimesheetEntry[]): string[][] {
    const source: TimesheetCsvSource = { timesheet: TIMESHEET, entries, departmentName: 'Engineering' }
    return buildTimesheetsCsv(
        [source],
        new Map([[1, { code: 'APL', name: 'Apollo' }]]),
        new Map([[10, { name: 'Support' }]]),
        new Map([[20, { name: 'Lasernet' }]]),
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
})
