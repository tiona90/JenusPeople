import { makeAutoObservable, runInAction } from 'mobx'
import { getCurrentUser, login, logout } from '../api'
import type { LoginRequest, UserInfo } from '../types'

class AuthStore {
    user: UserInfo | null = null
    hasCheckedAuth = false
    isLoadingUser = false

    constructor() {
        makeAutoObservable(this, {}, { autoBind: true })
    }

    get isAuthenticated() {
        return this.user !== null
    }

    async hydrateUser() {
        if (this.hasCheckedAuth || this.isLoadingUser) {
            return
        }

        this.isLoadingUser = true

        try {
            const user = await getCurrentUser()
            runInAction(() => {
                this.user = user
            })
        } catch {
            runInAction(() => {
                this.user = null
            })
        } finally {
            runInAction(() => {
                this.hasCheckedAuth = true
                this.isLoadingUser = false
            })
        }
    }

    async signIn(credentials: LoginRequest) {
        this.isLoadingUser = true

        try {
            await login(credentials)
            const user = await getCurrentUser()

            runInAction(() => {
                this.user = user
                this.hasCheckedAuth = true
            })

            return user
        } finally {
            runInAction(() => {
                this.isLoadingUser = false
            })
        }
    }

    async signOut() {
        try {
            await logout()
        } finally {
            runInAction(() => {
                this.user = null
                this.hasCheckedAuth = true
            })
        }
    }

    setUserImageUrl(imageUrl: string) {
        if (!this.user) {
            return
        }

        this.user = {
            ...this.user,
            imageUrl,
        }
    }

    setUserDisplayName(displayName: string) {
        if (!this.user) {
            return
        }

        this.user = {
            ...this.user,
            displayName,
        }
    }

    setUserProfile(profile: { displayName: string; email: string; phoneNumber?: string | null; dateOfBirth?: string | null; departmentId?: number | null; departmentName?: string | null }) {
        if (!this.user) {
            return
        }

        this.user = {
            ...this.user,
            displayName: profile.displayName,
            email: profile.email,
            userName: profile.email,
            phoneNumber: profile.phoneNumber ?? null,
            dateOfBirth: profile.dateOfBirth ?? null,
            departmentId: profile.departmentId ?? this.user.departmentId ?? null,
            departmentName: profile.departmentName ?? this.user.departmentName ?? null,
        }
    }
}

export default AuthStore