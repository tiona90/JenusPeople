import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'

/** Minimal project shape needed to label a CSV row. */
export interface ProjectRef {
    code?: string | null
    name?: string | null
}

/** Minimal project-type shape needed to label a CSV row. */
export interface ProjectTypeRef {
    name?: string | null
}

/** Minimal component shape needed to label a CSV row. */
export interface ProjectComponentRef {
    name?: string | null
}

/** One timesheet plus its (already fetched) entries and resolved department name. */
export interface TimesheetCsvSource {
    timesheet: Timesheet
    entries: TimesheetEntry[]
    departmentName: string
}

const HEADER = [
    'Employee',
    'Department',
    'Week',
    'Date',
    'Day',
    'Type',
    'Project Code',
    'Project Name',
    'Component',
    'Hours',
    'Notes (what was worked on)',
    'Timesheet Total Hours',
    'Status',
    'Submitted At',
]

const fmtDate = (iso: string) => iso.split('T')[0]
const fmtDay = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { weekday: 'short' })
const fmtSubmitted = (iso?: string | null) =>
    iso ? new Date(iso).toLocaleString('en-GB', { hour12: false }) : ''

const escape = (v: string) => (/[",\n\r]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)

/**
 * Builds the CSV body (CRLF-joined, no BOM) for a set of timesheets — one row per
 * entry, or a single "(no entries)" row for an empty timesheet. Pure and
 * deterministic given its inputs, so it can be unit-tested without the DOM or network.
 */
export function buildTimesheetsCsv(
    sources: TimesheetCsvSource[],
    projectById: Map<number, ProjectRef>,
    typeById: Map<number, ProjectTypeRef> = new Map(),
    componentById: Map<number, ProjectComponentRef> = new Map(),
): string {
    const csvRows: string[][] = []

    for (const { timesheet: t, entries, departmentName: dept } of sources) {
        const week = `${fmtDate(t.periodStart)} to ${fmtDate(t.periodEnd)}`
        const total = Number(t.totalHours).toFixed(1)
        const submitted = fmtSubmitted(t.submittedAt)
        const sorted = entries.slice().sort((a, b) => a.date.localeCompare(b.date))

        if (sorted.length === 0) {
            csvRows.push([
                t.employeeName,
                dept,
                week,
                '',
                '',
                '',
                '',
                '',
                '',
                '',
                '(no entries)',
                total,
                t.status,
                submitted,
            ])
            continue
        }

        for (const e of sorted) {
            const proj = projectById.get(e.projectId)
            // Blank for an entry logged with no type, which is every entry
            // predating the field and any logged against an unclassified project.
            const type = e.projectTypeId != null ? typeById.get(e.projectTypeId) : undefined
            const component = e.projectComponentId != null ? componentById.get(e.projectComponentId) : undefined
            csvRows.push([
                t.employeeName,
                dept,
                week,
                fmtDate(e.date),
                fmtDay(e.date),
                type?.name ?? '',
                proj?.code ?? '',
                proj?.name ?? `Project #${e.projectId}`,
                component?.name ?? '',
                Number(e.hoursWorked).toFixed(2),
                e.notes ?? '',
                total,
                t.status,
                submitted,
            ])
        }
    }

    const lines = [
        HEADER.map(escape).join(','),
        ...csvRows.map((cells) => cells.map(escape).join(',')),
    ]
    return lines.join('\r\n')
}
