import apiClient from './client'
import type {
    ApiMessageResponse,
    ForgotPasswordRequest,
    LoginRequest,
    ResetPasswordRequest,
    UpdateProfileRequest,
    UserInfo,
} from '../types'

export async function login(request: LoginRequest) {
    const response = await apiClient.post<ApiMessageResponse>('/account/login', request)
    return response.data
}

export async function getCurrentUser() {
    const response = await apiClient.get<UserInfo>('/account/user-info')
    return response.data
}

export async function logout() {
    const response = await apiClient.post<ApiMessageResponse>('/account/logout')
    return response.data
}

export async function forgotPassword(request: ForgotPasswordRequest) {
    const response = await apiClient.post<ApiMessageResponse>('/account/forgot-password', request, {
        headers: {
            'x-suppress-global-error': 'true',
        },
    })

    return response.data
}

export async function resetPassword(request: ResetPasswordRequest) {
    const response = await apiClient.post<ApiMessageResponse>('/account/reset-password', request, {
        headers: {
            'x-suppress-global-error': 'true',
        },
    })

    return response.data
}

export async function uploadProfileImage(file: File) {
    const formData = new FormData()
    formData.append('file', file)

    const response = await apiClient.post<{ imageUrl: string }>('/account/profile-image', formData, {
        headers: {
            'Content-Type': 'multipart/form-data',
        },
    })

    return response.data
}

export async function updateProfile(request: UpdateProfileRequest) {
    const response = await apiClient.put<ApiMessageResponse & {
        displayName: string
        email: string
        phoneNumber: string | null
        dateOfBirth: string | null
        departmentId: number
        departmentName: string
    }>('/account/profile', request)
    return response.data
}