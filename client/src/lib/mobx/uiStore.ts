import { makeAutoObservable } from 'mobx'

export type MyLeaveSection = 'apply' | 'requests' | 'balance' | 'other' | 'history'
export type AdminSection = 'dashboard' | 'settings' | 'leave' | 'leave-types' | 'users' | 'departments' | 'projects'
export type ThemeMode = 'light' | 'dark'
// 'system' is the popular admin-dashboard hybrid: light body content with a
// dark sidebar + topbar (the "chrome"). 'light' / 'dark' make both surfaces
// match. The store derives concrete bodyMode/chromeMode from this preference.
export type ThemePreference = 'light' | 'system' | 'dark'

const THEME_MODE_KEY = 'worktrack:themeMode'

function readStoredThemePreference(): ThemePreference {
    try {
        const stored = window.localStorage.getItem(THEME_MODE_KEY)
        if (stored === 'dark' || stored === 'light' || stored === 'system') return stored
        return 'light'
    } catch {
        return 'light'
    }
}

type Navigate = (path: string) => void

/**
 * Client-only UI state that should NOT be in the URL: the create-leave drawer
 * open/closed state and an inter-page hand-off for "open the new-timesheet page
 * pre-loaded for this week".
 *
 * The `navigateTo*` methods are thin shims over react-router. App.tsx injects
 * the router's `navigate` via {@link setNavigate} once it mounts. This keeps
 * ~30 call sites across the codebase working without each having to call
 * `useNavigate()` directly.
 */
class UiStore {
    isCreateDrawerOpen = false
    pendingWeekStart: string | null = null
    themePreference: ThemePreference = readStoredThemePreference()

    private _navigate: Navigate | null = null

    constructor() { makeAutoObservable(this, { setNavigate: false }) }

    // Body + topbar palette: dark only when the user explicitly picks Dark.
    // Both Light and System render the body and topbar with the light palette.
    get themeMode(): ThemeMode {
        return this.themePreference === 'dark' ? 'dark' : 'light'
    }

    // Sidebar palette: dark for System and Dark, light only when the user
    // explicitly picks Light. System mode = dark left nav + light everything
    // else (the popular admin-dashboard layout).
    get sidebarMode(): ThemeMode {
        return this.themePreference === 'light' ? 'light' : 'dark'
    }

    cycleThemePreference() {
        const next: ThemePreference =
            this.themePreference === 'light' ? 'system'
            : this.themePreference === 'system' ? 'dark'
            : 'light'
        this.setThemePreference(next)
    }

    setThemePreference(preference: ThemePreference) {
        if (this.themePreference === preference) return
        this.themePreference = preference
        try {
            window.localStorage.setItem(THEME_MODE_KEY, preference)
        } catch {
            // localStorage may be unavailable (private mode, quota) — fall back to in-memory only.
        }
    }

    setNavigate(fn: Navigate) { this._navigate = fn }

    private go(path: string) {
        if (this._navigate) {
            this._navigate(path)
        } else {
            // Fallback for any nav fired before App mounts (shouldn't happen in practice).
            window.history.pushState(null, '', path)
        }
    }

    navigateToDashboard() { this.go('/dashboard') }
    navigateToAdminSection(section: AdminSection) { this.go(`/admin/${section}`) }
    navigateToMyLeave(section: MyLeaveSection = 'requests') { this.go(`/my-leave/${section}`) }
    navigateToApplyLeave() { this.go('/apply-leave') }
    navigateToTeamLeave() { this.go('/team-leave') }
    navigateToTimesheets() { this.go('/timesheets') }
    navigateToTeamTimesheets() { this.go('/team-timesheets') }
    navigateToNewTimesheet(targetWeekStart?: string) {
        this.pendingWeekStart = targetWeekStart ?? null
        this.go('/new-timesheet')
    }
    navigateToAttendance() { this.go('/attendance') }
    navigateToTeamAttendance() { this.go('/team-attendance') }
    navigateToCompanyAttendance() { this.go('/company-attendance') }

    consumePendingWeekStart(): string | null {
        const v = this.pendingWeekStart
        this.pendingWeekStart = null
        return v
    }

    openCreateDrawer() { this.isCreateDrawerOpen = true }
    closeCreateDrawer() { this.isCreateDrawerOpen = false }
    toggleCreateDrawer() { this.isCreateDrawerOpen = !this.isCreateDrawerOpen }

    resetAfterSignOut() {
        this.isCreateDrawerOpen = false
        this.pendingWeekStart = null
    }
}

export default UiStore
