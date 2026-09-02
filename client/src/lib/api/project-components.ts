import apiClient from './client'
import type { ProjectComponent } from '../types'

export interface UpsertProjectComponentRequest {
    name: string
    description: string
    icon: string
    colorKey: string
    isActive: boolean
}

export async function getProjectComponents() {
    const response = await apiClient.get<ProjectComponent[]>('/projectcomponents')
    return response.data
}

export async function createProjectComponent(request: UpsertProjectComponentRequest) {
    const response = await apiClient.post<ProjectComponent>('/projectcomponents', request)
    return response.data
}

export async function updateProjectComponent(id: number, request: UpsertProjectComponentRequest) {
    const response = await apiClient.put<ProjectComponent>(`/projectcomponents/${id}`, request)
    return response.data
}

export async function deleteProjectComponent(id: number) {
    await apiClient.delete(`/projectcomponents/${id}`)
}
