import apiClient from './client'
import type { TimesheetTaskOption, UpsertWorkTaskRequest, WorkTask, WorkTaskAssignee, WorkTaskDepartment, WorkTaskIdlePerson, WorkTaskProject, WorkTaskStatus } from '../types'

// Every task in the caller's departments. Zero-arg so it is safe as a queryFn.
export async function getWorkTasks() {
    const response = await apiClient.get<WorkTask[]>('/worktasks')
    return response.data
}

export async function getWorkTaskDepartments() {
    const response = await apiClient.get<WorkTaskDepartment[]>('/worktasks/departments')
    return response.data
}

export async function getWorkTaskAssignees(departmentId: number) {
    const response = await apiClient.get<WorkTaskAssignee[]>('/worktasks/assignees', { params: { departmentId } })
    return response.data
}

// Who in the caller's departments has no task In Progress. Managers and HR only.
export async function getIdleTaskPeople() {
    const response = await apiClient.get<WorkTaskIdlePerson[]>('/worktasks/idle-people')
    return response.data
}

// The active projects assigned to one of the caller's departments.
export async function getWorkTaskProjects(departmentId: number) {
    const response = await apiClient.get<WorkTaskProject[]>('/worktasks/projects', { params: { departmentId } })
    return response.data
}

export async function createWorkTask(request: UpsertWorkTaskRequest) {
    const response = await apiClient.post<WorkTask>('/worktasks', request)
    return response.data
}

export async function updateWorkTask(id: number, request: UpsertWorkTaskRequest) {
    const response = await apiClient.put<WorkTask>(`/worktasks/${id}`, request)
    return response.data
}

/** The server decides what Done means: an assignee's lands in AwaitingConfirmation. `reason` is required to send a waiting task back. */
export async function updateWorkTaskStatus(id: number, status: WorkTaskStatus, reason?: string) {
    const response = await apiClient.patch<WorkTask>(`/worktasks/${id}/status`, reason === undefined ? { status } : { status, reason })
    return response.data
}

/** Attaches one file; the server checks its kind and size (StoreFile) and returns the task. */
export async function addWorkTaskAttachment(id: number, file: File) {
    const formData = new FormData()
    formData.append('file', file)
    const response = await apiClient.post<WorkTask>(`/worktasks/${id}/attachments`, formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
    })
    return response.data
}

export async function removeWorkTaskAttachment(id: number, attachmentId: number) {
    const response = await apiClient.delete<WorkTask>(`/worktasks/${id}/attachments/${attachmentId}`)
    return response.data
}

export async function deleteWorkTask(id: number) {
    await apiClient.delete(`/worktasks/${id}`)
}

// The Task picker on a timesheet row. Without an id: the caller's own (a week not yet saved).
export async function getTimesheetTaskOptions(timesheetId?: string) {
    const response = await apiClient.get<TimesheetTaskOption[]>('/worktasks/timesheet-options', {
        params: timesheetId ? { timesheetId } : undefined,
    })
    return response.data
}
