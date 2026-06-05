import apiClient from './client'
import type { ProjectActivityType } from '../types'

export interface UpsertProjectActivityTypeRequest {
    name: string
    description: string
    icon: string
    colorKey: string
    isActive: boolean
}

export async function getProjectActivityTypes() {
    const response = await apiClient.get<ProjectActivityType[]>('/projectactivitytypes')
    return response.data
}

export async function createProjectActivityType(request: UpsertProjectActivityTypeRequest) {
    const response = await apiClient.post<ProjectActivityType>('/projectactivitytypes', request)
    return response.data
}

export async function updateProjectActivityType(id: number, request: UpsertProjectActivityTypeRequest) {
    const response = await apiClient.put<ProjectActivityType>(`/projectactivitytypes/${id}`, request)
    return response.data
}

export async function deleteProjectActivityType(id: number) {
    await apiClient.delete(`/projectactivitytypes/${id}`)
}
