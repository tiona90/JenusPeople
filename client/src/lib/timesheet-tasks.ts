import type { TimesheetTaskOption } from './types'
import { describeTaskProgress } from './work-tasks'

/*
 * The Task column on a timesheet row. Mirrors TimesheetEntryTaskRule: a row may
 * name one of its owner's open tasks on its own project. The server re-checks
 * only a changed task or project, so a closed task already on a row stays listed
 * for that row, and for no other.
 */

export function taskOptionsForRow(options: TimesheetTaskOption[], projectId: string, currentTaskId: string): TimesheetTaskOption[] {
    return options.filter((o) =>
        (!o.isClosed || String(o.id) === currentTaskId)
        && (!projectId || String(o.projectId) === projectId))
}

/**
 * The row's task after its project moves: kept if it belongs to the new project,
 * otherwise cleared, since the server would refuse it. An id not in the list yet
 * (options still loading) is left alone rather than wiped.
 */
export function retainedWorkTaskId(workTaskId: string, projectId: string, options: TimesheetTaskOption[]): string {
    if (!workTaskId) return ''
    const option = options.find((o) => String(o.id) === workTaskId)
    if (!option) return workTaskId
    return String(option.projectId) === projectId ? workTaskId : ''
}

/** "Copy to rest of week" carries a task only while it is still open and listed. */
export function copyableWorkTaskId(workTaskId: string, options: TimesheetTaskOption[]): string {
    const option = options.find((o) => String(o.id) === workTaskId)
    return option && !option.isClosed ? workTaskId : ''
}

export function taskOptionLabel(option: TimesheetTaskOption): string {
    const name = option.projectCode ? `${option.projectCode} · ${option.title}` : option.title
    if (option.isClosed) return `${name} (closed)`
    const { text } = describeTaskProgress(option.targetHours, option.loggedHours)
    // The picker has room for one figure: what is left, how far over, or what is logged.
    const short = text.includes(' · ') ? text.split(' · ')[1] : text
    return `${name} — ${short === 'nothing logged' ? '0h logged' : short}`
}
