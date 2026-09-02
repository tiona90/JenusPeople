import apiClient from './client'
import type { ProjectType } from '../types'

export interface UpsertProjectTypeRequest {
    name: string
    description: string
    icon: string
    colorKey: string
    isActive: boolean
}

export async function getProjectTypes() {
    const response = await apiClient.get<ProjectType[]>('/projecttypes')
    return response.data
}

export async function createProjectType(request: UpsertProjectTypeRequest) {
    const response = await apiClient.post<ProjectType>('/projecttypes', request)
    return response.data
}

export async function updateProjectType(id: number, request: UpsertProjectTypeRequest) {
    const response = await apiClient.put<ProjectType>(`/projecttypes/${id}`, request)
    return response.data
}

export async function deleteProjectType(id: number) {
    await apiClient.delete(`/projecttypes/${id}`)
}
