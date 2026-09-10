import apiClient from './client'
import type { Child, ChildLeaveEntitlementSummary, UpsertChildRequest } from '../types'

/**
 * `employeeId` is for an admin reading or editing someone else's children, and for
 * the on-behalf leave form. Omitted, every call is about the signed-in user. The
 * server authorizes it either way — passing an id is not permission to use it.
 */
export async function getChildren(employeeId?: string) {
    const response = await apiClient.get<Child[]>('/children', {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}

export async function createChild(request: UpsertChildRequest, employeeId?: string) {
    const response = await apiClient.post<Child>('/children', request, {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}

export async function updateChild(id: string, request: UpsertChildRequest) {
    const response = await apiClient.put<Child>(`/children/${id}`, request)
    return response.data
}

export async function deleteChild(id: string) {
    await apiClient.delete(`/children/${id}`)
}

export async function getChildLeaveEntitlements(employeeId?: string) {
    const response = await apiClient.get<ChildLeaveEntitlementSummary>('/children/entitlements', {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}
