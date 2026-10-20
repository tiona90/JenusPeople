import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import type { WorkTaskPriority, WorkTaskStatus } from '../../lib/types/work-task'
import { PRIORITY_LABELS, STATUS_LABELS } from '../../lib/work-tasks'

/** Minimal project shape needed to label a CSV row. */
export interface ProjectRef {
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

/** Minimal approved-project (work task) shape needed to describe what a row was logged against. */
export interface WorkTaskRef {
    title: string
    description?: string | null
    status?: WorkTaskStatus | null
    priority?: WorkTaskPriority | null
    /** Calendar date, `YYYY-MM-DD`. */
    startDate?: string | null
    /** Calendar date, `YYYY-MM-DD`. */
    dueDate?: string | null
    targetHours?: number | null
    /** Every hour logged against the task, on any sheet — what "over" is measured on. */
    loggedHours?: number | null
    createdByName?: string | null
    createdAtUtc?: string | null
}

/** One timesheet plus its (already fetched) entries and resolved department name. */
export interface TimesheetCsvSource {
    timesheet: Timesheet
    entries: TimesheetEntry[]
    departmentName: string
}

/** The approved project (work task) a row was logged against, in the order `approvedProjectCells` fills them. */
const APPROVED_PROJECT_HEADER = [
    'Approved Project',
    'Approved Project Description',
    'Approved Project Status',
    'Approved Project Priority',
    'Approved Project Start Date',
    'Approved Project Due Date',
    'Approved Project Target Hours',
    'Approved Project Logged Hours',
    'Approved Project Hours Over Target',
    'Approved Project Created By',
    'Approved Project Created',
]

const HEADER = [
    'Employee',
    'Department',
    'Week',
    'Date',
    'Day',
    'Type',
    ...APPROVED_PROJECT_HEADER,
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
const fmtHours = (h: number) => (Number.isInteger(h) ? String(h) : h.toFixed(2))

const escape = (v: string) => (/[",\n\r]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)

/** The approved-project cells, blank for a row logged against none (or one the caller cannot see). */
function approvedProjectCells(task: WorkTaskRef | undefined): string[] {
    if (!task) return APPROVED_PROJECT_HEADER.map(() => '')
    const target = task.targetHours ?? null
    const logged = task.loggedHours ?? null
    const over = target != null ? (logged ?? 0) - target : 0
    return [
        task.title,
        task.description ?? '',
        task.status ? STATUS_LABELS[task.status] ?? task.status : '',
        task.priority ? PRIORITY_LABELS[task.priority] ?? task.priority : '',
        task.startDate ? fmtDate(task.startDate) : '',
        task.dueDate ? fmtDate(task.dueDate) : '',
        target != null ? fmtHours(target) : '',
        logged != null ? fmtHours(logged) : '',
        // Blank unless the task has gone past its plan; on or under target is not news.
        over > 0 ? fmtHours(over) : '',
        task.createdByName ?? '',
        task.createdAtUtc ? fmtDate(task.createdAtUtc) : '',
    ]
}

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
    taskById: Map<number, WorkTaskRef> = new Map(),
): string {
    const csvRows: string[][] = []

    for (const { timesheet: t, entries, departmentName: dept } of sources) {
        const week = `${fmtDate(t.periodStart)} to ${fmtDate(t.periodEnd)}`
        const total = Number(t.totalHours).toFixed(1)
        const submitted = fmtSubmitted(t.submittedAt)
        const sorted = entries.slice().sort((a, b) => a.date.localeCompare(b.date))

        if (sorted.length === 0) {
            // Padded from the header, so it cannot drift out of step when a column is added.
            const row = new Array<string>(HEADER.length).fill('')
            row[0] = t.employeeName
            row[1] = dept
            row[2] = week
            row[HEADER.length - 4] = '(no entries)'
            row[HEADER.length - 3] = total
            row[HEADER.length - 2] = t.status
            row[HEADER.length - 1] = submitted
            csvRows.push(row)
            continue
        }

        for (const e of sorted) {
            const proj = projectById.get(e.projectId)
            // Blank for an entry logged with no type, which is every entry
            // predating the field and any logged against an unclassified project.
            const type = e.projectTypeId != null ? typeById.get(e.projectTypeId) : undefined
            const component = e.projectComponentId != null ? componentById.get(e.projectComponentId) : undefined
            const task = e.workTaskId != null ? taskById.get(e.workTaskId) : undefined
            csvRows.push([
                t.employeeName,
                dept,
                week,
                fmtDate(e.date),
                fmtDay(e.date),
                type?.name ?? '',
                ...approvedProjectCells(task),
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
