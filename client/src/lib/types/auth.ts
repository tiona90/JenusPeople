export interface LoginRequest {
    email: string
    password: string
    rememberMe: boolean
}

export interface ForgotPasswordRequest {
    email: string
}

export interface ResetPasswordRequest {
    email: string
    token: string
    newPassword: string
    confirmPassword: string
}

export interface UpdateProfileRequest {
    displayName: string
    email: string
    phoneNumber?: string | null
    dateOfBirth?: string | null // ISO date "yyyy-MM-dd" or null to clear
}
