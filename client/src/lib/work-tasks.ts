import type { WorkTaskSettings } from './api/work-task-settings'
import { DEFAULT_TASK_SETTINGS, isShown, needsAttachmentBeforeDone } from './task-settings'
import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from './types'

export type TaskTab = 'assigned' | 'created' | 'all'
/** `open` is To do, In progress and Awaiting confirmation, the page's default; `any` is everything. */
export type StatusFilter = 'open' | 'any' | WorkTaskStatus

export const STATUS_LABELS: Record<WorkTaskStatus, string> = {
    ToDo: 'To do',
    InProgress: 'In progress',
    AwaitingConfirmation: 'Awaiting confirmation',
    Done: 'Done',
    Cancelled: 'Cancelled',
}

/** What a Status menu or select may offer. Awaiting confirmation is reached by marking a task done, never picked. */
export const SETTABLE_STATUSES: WorkTaskStatus[] = ['ToDo', 'InProgress', 'Done', 'Cancelled']

/** Mirrors WorkTask.SentBackReasonMaxLength. */
export const SEND_BACK_REASON_MAX = 500

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
        case 'AwaitingConfirmation': return { label: 'Withdraw', icon: '↺', to: 'InProgress' }
    }
}

export function isAwaitingConfirmation(task: WorkTask): boolean {
    return task.status === 'AwaitingConfirmation'
}

export function isOpenTask(task: WorkTask): boolean {
    return task.status === 'ToDo' || task.status === 'InProgress' || task.status === 'AwaitingConfirmation'
}

/**
 * Today as the viewer's local calendar date. Built from the local parts, never
 * `toISOString()`, which reads local midnight back as yesterday east of Greenwich.
 */
export function todayIso(now: Date = new Date()): string {
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * Due dates are `YYYY-MM-DD`, so a string comparison is a date comparison. A task
 * waiting for confirmation is handed in, so it is never late. With the due date
 * hidden by the Task Settings, nothing is overdue — there is no date left to judge.
 */
export function isOverdue(task: WorkTask, today: string, dueDateShown = true): boolean {
    return dueDateShown && isOpenTask(task) && !isAwaitingConfirmation(task) && task.dueDate != null && task.dueDate.slice(0, 10) < today
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

export function plural(n: number, one: string, many = `${one}s`) {
    return `${n} ${n === 1 ? one : many}`
}

/** What the card, the board and the list all read off a task, under the Task Settings. */
export function taskFacts(task: WorkTask, today: string, settings: WorkTaskSettings) {
    const closed = !isOpenTask(task)
    const showDue = isShown(settings, 'dueDate')
    const attachmentCount = task.attachments?.length ?? 0
    return {
        closed,
        showDue,
        showTarget: isShown(settings, 'targetHours'),
        late: overdueDays(task, today, showDue),
        progress: describeTaskProgress(task.targetHours, task.loggedHours),
        attachmentCount,
        fileNeeded: !closed && needsAttachmentBeforeDone(settings, attachmentCount),
    }
}

/** How the Tasks page lays its tasks out: grouped cards, a dense table, or a board by status. */
export type TaskLayout = 'cards' | 'list' | 'board'

/**
 * The board's columns: the statuses the status filter lets through, in workflow
 * order. Awaiting confirmation is dropped when confirmation is off and nothing is
 * still waiting — switching it off sweeps every waiting task to Done, so the column
 * would only ever be empty.
 */
export function boardStatuses(filter: StatusFilter, confirmation: boolean, tasks: readonly WorkTask[]): WorkTaskStatus[] {
    const all: WorkTaskStatus[] = ['ToDo', 'InProgress', 'AwaitingConfirmation', 'Done', 'Cancelled']
    const admitted = all.filter((s) => filter === 'any' || (filter === 'open' ? s !== 'Done' && s !== 'Cancelled' : s === filter))
    return admitted.filter((s) => s !== 'AwaitingConfirmation' || confirmation || tasks.some(isAwaitingConfirmation) || filter === s)
}

/** Whole days an open task is past its due date; 0 when it is not overdue. */
export function overdueDays(task: WorkTask, today: string, dueDateShown = true): number {
    if (!isOverdue(task, today, dueDateShown)) return 0
    const [y1, m1, d1] = task.dueDate!.slice(0, 10).split('-').map(Number)
    const [y2, m2, d2] = today.split('-').map(Number)
    return Math.round((Date.UTC(y2, m2 - 1, d2) - Date.UTC(y1, m1 - 1, d1)) / 86_400_000)
}

/** The figures the summary tiles show, over every task the viewer can see. */
export function taskStats(tasks: readonly WorkTask[], userId: string, today: string, dueDateShown = true) {
    const month = today.slice(0, 7)
    return {
        total: tasks.length,
        open: tasks.filter(isOpenTask).length,
        inProgress: tasks.filter((t) => t.status === 'InProgress').length,
        overdue: tasks.filter((t) => isOverdue(t, today, dueDateShown)).length,
        doneThisMonth: tasks.filter((t) => t.status === 'Done' && (t.completedAtUtc ?? '').slice(0, 7) === month).length,
        assignedToMeOpen: openCount(tasks, 'assigned', userId),
        awaitingMyConfirmation: tasks.filter((t) => t.canConfirm === true).length,
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

/**
 * The tasks as CSV, one row per task, in the order given (the page passes what it
 * is showing, so the file matches the filters). Assignees share one cell, joined
 * by "; ". Dates are `YYYY-MM-DD`; a legacy task with no billing answer is blank.
 * A column whose field the Task Settings hide drops out of the header and every
 * row together, built from the one list below.
 */
export function tasksToCsv(tasks: readonly WorkTask[], settings: WorkTaskSettings = DEFAULT_TASK_SETTINGS): string {
    const columns: { header: string; shown: boolean; value: (t: WorkTask) => string }[] = [
        { header: 'Title', shown: true, value: (t) => csvText(t.title) },
        { header: 'Description', shown: isShown(settings, 'description'), value: (t) => csvText(t.description) },
        { header: 'Department', shown: true, value: (t) => csvText(t.departmentName) },
        { header: 'Project code', shown: true, value: (t) => csvText(t.projectCode) },
        { header: 'Project', shown: true, value: (t) => csvText(t.projectName) },
        { header: 'Status', shown: true, value: (t) => csvText(STATUS_LABELS[t.status]) },
        { header: 'Priority', shown: isShown(settings, 'priority'), value: (t) => csvText(PRIORITY_LABELS[t.priority]) },
        { header: 'Billable', shown: isShown(settings, 'billable'), value: (t) => (t.isBillable == null ? '' : t.isBillable ? 'Yes' : 'No') },
        { header: 'Assignees', shown: true, value: (t) => csvText(t.assignees.map((a) => a.displayName).join('; ')) },
        { header: 'Created by', shown: true, value: (t) => csvText(t.createdByName) },
        { header: 'Due date', shown: isShown(settings, 'dueDate'), value: (t) => t.dueDate?.slice(0, 10) ?? '' },
        { header: 'Target hours', shown: isShown(settings, 'targetHours'), value: (t) => (t.targetHours == null ? '' : String(t.targetHours)) },
        { header: 'Logged hours', shown: true, value: (t) => formatHours(Number(t.loggedHours) || 0) },
        { header: 'Created', shown: true, value: (t) => t.createdAtUtc.slice(0, 10) },
        { header: 'Completed', shown: true, value: (t) => t.completedAtUtc?.slice(0, 10) ?? '' },
    ]
    const shown = columns.filter((c) => c.shown)
    return [shown.map((c) => c.header).join(','), ...tasks.map((t) => shown.map((c) => c.value(t)).join(','))].join('\r\n')
}

/** A task's date (`YYYY-MM-DD`, or an instant whose calendar day is read from its first ten characters) as "Sep 29, 2026". */
export function formatTaskDate(iso: string): string {
    const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}
