import apiClient from './client'
import type {
    AdminCreateUserRequest,
    AdminSetUserRolesRequest,
    AdminUpdateUserRequest,
    AdminUser,
} from '../types'

export async function getAdminUsers() {
    const response = await apiClient.get<AdminUser[]>('/adminusers')
    return response.data
}

export async function createAdminUser(request: AdminCreateUserRequest) {
    const response = await apiClient.post<AdminUser>('/adminusers', request)
    return response.data
}

export async function updateAdminUser(id: string, request: AdminUpdateUserRequest) {
    const response = await apiClient.put<AdminUser>(`/adminusers/${id}`, request)
    return response.data
}

export async function setAdminUserRoles(id: string, request: AdminSetUserRolesRequest) {
    const response = await apiClient.put<AdminUser>(`/adminusers/${id}/roles`, request)
    return response.data
}

export async function confirmAdminUserEmail(id: string) {
    const response = await apiClient.post<AdminUser>(`/adminusers/${id}/confirm-email`)
    return response.data
}

export async function deleteAdminUser(id: string) {
    await apiClient.delete(`/adminusers/${id}`)
}