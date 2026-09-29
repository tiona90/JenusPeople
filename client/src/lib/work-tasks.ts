import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from './types'

export type TaskTab = 'assigned' | 'created' | 'all'
/** `open` is To do + In progress, the page's default; `any` is everything. */
export type StatusFilter = 'open' | 'any' | WorkTaskStatus

export const STATUS_LABELS: Record<WorkTaskStatus, string> = {
    ToDo: 'To do',
    InProgress: 'In progress',
    Done: 'Done',
    Cancelled: 'Cancelled',
}

export const PRIORITY_LABELS: Record<WorkTaskPriority, string> = { Low: 'Low', Normal: 'Normal', High: 'High' }

/**
 * The one move a card offers as its main button: start it, finish it, or bring it
 * back. A finished task reopens as In progress (the work was under way); a
 * cancelled one as To do (it never really started). Every other move is in the
 * Status menu beside it.
 */
export function nextStatusAction(status: WorkTaskStatus): { label: string; icon: string; to: WorkTaskStatus } {
    switch (status) {
        case 'ToDo': return { label: 'Start', icon: '▶', to: 'InProgress' }
        case 'InProgress': return { label: 'Mark done', icon: '✓', to: 'Done' }
        case 'Done': return { label: 'Reopen', icon: '↺', to: 'InProgress' }
        case 'Cancelled': return { label: 'Reopen', icon: '↺', to: 'ToDo' }
    }
}

export function isOpenTask(task: WorkTask): boolean {
    return task.status === 'ToDo' || task.status === 'InProgress'
}

/**
 * Today as the viewer's local calendar date. Built from the local parts, never
 * `toISOString()`, which reads local midnight back as yesterday east of Greenwich.
 */
export function todayIso(now: Date = new Date()): string {
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Due dates are `YYYY-MM-DD`, so a string comparison is a date comparison. */
export function isOverdue(task: WorkTask, today: string): boolean {
    return isOpenTask(task) && task.dueDate != null && task.dueDate.slice(0, 10) < today
}

function inTab(task: WorkTask, tab: TaskTab, userId: string): boolean {
    if (tab === 'assigned') return task.assignees.some((a) => a.userId === userId)
    if (tab === 'created') return task.createdById === userId
    return true
}

function matchesStatus(task: WorkTask, status: StatusFilter): boolean {
    if (status === 'any') return true
    if (status === 'open') return isOpenTask(task)
    return task.status === status
}

/** Title, description and project name or code, ignoring case. A blank search matches everything. */
function matchesSearch(task: WorkTask, search: string | undefined): boolean {
    const needle = (search ?? '').trim().toLowerCase()
    if (needle === '') return true
    return [task.title, task.description, task.projectName, task.projectCode]
        .some((field) => (field ?? '').toLowerCase().includes(needle))
}

export function filterTasks(
    tasks: readonly WorkTask[],
    filter: {
        tab: TaskTab
        status: StatusFilter
        departmentId: number | null
        userId: string
        priority?: WorkTaskPriority | 'any'
        search?: string
    },
): WorkTask[] {
    return tasks.filter(
        (task) =>
            inTab(task, filter.tab, filter.userId) &&
            matchesStatus(task, filter.status) &&
            (filter.departmentId == null || task.departmentId === filter.departmentId) &&
            (filter.priority == null || filter.priority === 'any' || task.priority === filter.priority) &&
            matchesSearch(task, filter.search),
    )
}

/** Whole days an open task is past its due date; 0 when it is not overdue. */
export function overdueDays(task: WorkTask, today: string): number {
    if (!isOverdue(task, today)) return 0
    const [y1, m1, d1] = task.dueDate!.slice(0, 10).split('-').map(Number)
    const [y2, m2, d2] = today.split('-').map(Number)
    return Math.round((Date.UTC(y2, m2 - 1, d2) - Date.UTC(y1, m1 - 1, d1)) / 86_400_000)
}

/** The figures the summary tiles show, over every task the viewer can see. */
export function taskStats(tasks: readonly WorkTask[], userId: string, today: string) {
    const month = today.slice(0, 7)
    return {
        total: tasks.length,
        open: tasks.filter(isOpenTask).length,
        inProgress: tasks.filter((t) => t.status === 'InProgress').length,
        overdue: tasks.filter((t) => isOverdue(t, today)).length,
        doneThisMonth: tasks.filter((t) => t.status === 'Done' && (t.completedAtUtc ?? '').slice(0, 7) === month).length,
        assignedToMeOpen: openCount(tasks, 'assigned', userId),
    }
}

export function openCount(tasks: readonly WorkTask[], tab: TaskTab, userId: string): number {
    return tasks.filter((task) => inTab(task, tab, userId) && isOpenTask(task)).length
}

/** `8`, `1.5`, `22.5` — one decimal at most, no trailing `.0`. */
export function formatHours(hours: number): string {
    return `${Math.round(hours * 10) / 10}`
}

/**
 * How far through its target a task is, in the words the card and the timesheet
 * picker share. Over target is reported, never refused. A missing figure (an API
 * predating the field) reads as nothing logged.
 */
export function describeTaskProgress(targetHours: number | null, loggedHours: number): { text: string; over: boolean } {
    const logged = Number(loggedHours) || 0
    if (targetHours == null) return { text: logged > 0 ? `${formatHours(logged)}h logged` : 'nothing logged', over: false }
    const left = targetHours - logged
    if (left < 0) return { text: `${formatHours(-left)}h over`, over: true }
    return { text: `${formatHours(logged)}h logged · ${formatHours(left)}h left`, over: false }
}

/**
 * A text cell for a CSV. Quoted when it holds a comma, quote or line break, and
 * prefixed with an apostrophe when it opens with a character a spreadsheet would
 * read as a formula — a task titled "=HYPERLINK(...)" must arrive as text.
 */
function csvText(value: string | null | undefined): string {
    let text = value ?? ''
    if (/^[=+\-@\t\r]/.test(text)) text = `'${text}`
    return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

const CSV_HEADER = [
    'Title', 'Description', 'Department', 'Project code', 'Project', 'Status', 'Priority', 'Billable',
    'Assignees', 'Created by', 'Due date', 'Target hours', 'Logged hours', 'Created', 'Completed',
]

/**
 * The tasks as CSV, one row per task, in the order given (the page passes what it
 * is showing, so the file matches the filters). Assignees share one cell, joined
 * by "; ". Dates are `YYYY-MM-DD`; a legacy task with no billing answer is blank.
 */
export function tasksToCsv(tasks: readonly WorkTask[]): string {
    const rows = tasks.map((t) => [
        csvText(t.title),
        csvText(t.description),
        csvText(t.departmentName),
        csvText(t.projectCode),
        csvText(t.projectName),
        csvText(STATUS_LABELS[t.status]),
        csvText(PRIORITY_LABELS[t.priority]),
        t.isBillable == null ? '' : t.isBillable ? 'Yes' : 'No',
        csvText(t.assignees.map((a) => a.displayName).join('; ')),
        csvText(t.createdByName),
        t.dueDate?.slice(0, 10) ?? '',
        t.targetHours == null ? '' : String(t.targetHours),
        formatHours(Number(t.loggedHours) || 0),
        t.createdAtUtc.slice(0, 10),
        t.completedAtUtc?.slice(0, 10) ?? '',
    ].join(','))
    return [CSV_HEADER.join(','), ...rows].join('\r\n')
}
