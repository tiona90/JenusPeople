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

function entry(projectTypeId: number | null, hours: number): TimesheetEntry {
    return {
        id: `e-${projectTypeId}-${hours}`,
        timesheetId: 'ts-1',
        projectId: 1,
        projectTypeId,
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
    )
        .split('\r\n')
        .map((line) => line.split(','))
}

describe('buildTimesheetsCsv', () => {
    it('names the entry type in its own column', () => {
        const [header, row] = csv([entry(10, 3)])

        expect(header[5]).toBe('Type')
        expect(row[5]).toBe('Support')
    })

    // Every entry predating the field has no type, as does anything logged
    // against an unclassified project — blank, not "undefined".
    it('leaves the type blank for an entry that has none', () => {
        const [, row] = csv([entry(null, 3)])

        expect(row[5]).toBe('')
    })

    // The empty-timesheet row is padded by hand, so it drifts out of step with
    // the header the moment a column is added.
    it('keeps every row the same width as the header', () => {
        const rows = csv([entry(10, 3), entry(null, 2)])
        const empty = csv([])

        for (const row of [...rows, ...empty]) {
            expect(row).toHaveLength(rows[0].length)
        }
    })
})
