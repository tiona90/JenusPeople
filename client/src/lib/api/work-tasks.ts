import apiClient from './client'
import type { UpsertWorkTaskRequest, WorkTask, WorkTaskAssignee, WorkTaskDepartment, WorkTaskStatus } from '../types'

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

export async function createWorkTask(request: UpsertWorkTaskRequest) {
    const response = await apiClient.post<WorkTask>('/worktasks', request)
    return response.data
}

export async function updateWorkTask(id: number, request: UpsertWorkTaskRequest) {
    const response = await apiClient.put<WorkTask>(`/worktasks/${id}`, request)
    return response.data
}

export async function updateWorkTaskStatus(id: number, status: WorkTaskStatus) {
    const response = await apiClient.patch<WorkTask>(`/worktasks/${id}/status`, { status })
    return response.data
}

export async function deleteWorkTask(id: number) {
    await apiClient.delete(`/worktasks/${id}`)
}
