import apiClient from './client'
import type { EditEmployeeProfileRequest, EmployeeProfile, Teammate } from '../types'

export async function getEmployeeProfiles() {
    const response = await apiClient.get<EmployeeProfile[]>('/employeeprofiles')
    return response.data
}

// Colleagues in the caller's own department. Unlike getEmployeeProfiles this is
// readable by plain employees, so pickers can offer real people to choose from.
export async function getTeammates() {
    const response = await apiClient.get<Teammate[]>('/employeeprofiles/teammates')
    return response.data
}

export async function updateEmployeeProfile(request: EditEmployeeProfileRequest) {
    await apiClient.put('/employeeprofiles', request)
}