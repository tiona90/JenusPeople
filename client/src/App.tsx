import { useEffect, useState } from 'react'
import { BrowserRouter, Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { observer } from 'mobx-react-lite'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Container from '@mui/material/Container'
import Snackbar from '@mui/material/Snackbar'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import {
    AllLeaveAdminPage,
    AllTimesheetsPage,
    ApplyLeavePage,
    AttendancePage,
    AuthPage,
    CompanyAttendancePage,
    DashboardHome,
    MyLeavePage,
    MyTimesheetPage,
    NewTimesheetPage,
    TeamAttendancePage,
    TeamLeavePage,
    TeamTimesheetPage,
} from './components'
import ProtectedRoute from './components/auth/ProtectedRoute'
import ErrorBoundary from './components/ui/ErrorBoundary'
import { API_ERROR_EVENT } from './lib/api/error-events'
import { SESSION_EXPIRED_EVENT } from './lib/api/session-expiry'
import { keepConnected, reconnectPolicy } from './lib/notifications-hub'
import { apiBaseUrl } from './lib/api/client'
import { useStore } from './lib/mobx'
import { LEAVE_AND_TIME_ROLES, SYSTEM_ADMINISTRATOR_ROLES, TASK_ROLES, isAdministrator } from './lib/roles'
import TasksPage from './components/tasks/TasksPage'
import Sidebar from './components/layout/Sidebar'
import Topbar from './components/layout/Topbar'

type AuthView = 'login' | 'forgot-password' | 'reset-password'

function getAuthViewFromPath(pathname: string): AuthView {
    if (pathname.startsWith('/forgot-password')) return 'forgot-password'
    if (pathname.startsWith('/reset-password')) return 'reset-password'
    return 'login'
}

const AuthGate = observer(function AuthGate() {
    const { authStore } = useStore()
    const location = useLocation()
    const navigate = useNavigate()

    const authView = getAuthViewFromPath(location.pathname)
    const [authNotice, setAuthNotice] = useState<{ severity: 'success' | 'info' | 'error'; message: string } | null>(null)

    useEffect(() => {
        if (authStore.user) return

        const params = new URLSearchParams(window.location.search)
        const authStatus = params.get('authStatus')
        const authMessage = params.get('authMessage')?.trim()

        if (!authStatus && !authMessage) return

        setAuthNotice({
            severity: authStatus === 'error' ? 'error' : authStatus === 'info' ? 'info' : 'success',
            message: authMessage || 'Your account status has been updated. You can now continue.',
        })

        params.delete('authStatus')
        params.delete('authMessage')
        const query = params.toString()
        const cleanUrl = `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`
        window.history.replaceState({}, document.title, cleanUrl)
    }, [authStore.user])

    if (authStore.user) {
        const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname
        return <Navigate to={from ?? '/dashboard'} replace />
    }

    return (
        <AuthPage
            authView={authView}
            authNotice={authNotice}
            onClearNotice={() => setAuthNotice(null)}
            onForgotPassword={() => navigate('/forgot-password')}
            onBackToLogin={() => navigate('/login')}
            onRequestNewLink={() => navigate('/forgot-password')}
        />
    )
})

const AppShell = observer(function AppShell() {
    const { authStore } = useStore()
    const location = useLocation()

    if (!authStore.user) return null

    return (
        <Box sx={{ minHeight: '100vh', bgcolor: 'background.default', display: 'flex' }}>
            <Sidebar />
            <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                <Topbar />
                <Container component="main" maxWidth="lg" sx={{ pt: 3, pb: 4 }}>
                    {/* Keyed by route: a page that crashes is replaced by a message
                        while the sidebar and topbar stay, and navigating away
                        recovers. */}
                    <ErrorBoundary resetKey={location.pathname}>
                        <Outlet />
                    </ErrorBoundary>
                </Container>
            </Box>
        </Box>
    )
})

/**
 * Element wrappers that read the current user from `authStore` so we can declare
 * routes without ferrying the user prop through every Route element. AppShell
 * has already gated rendering on a non-null user.
 */
const MyLeaveRoute = observer(() => {
    const { authStore } = useStore()
    return authStore.user ? <MyLeavePage user={authStore.user} /> : null
})

const ApplyLeaveRoute = observer(() => {
    const { authStore } = useStore()
    return authStore.user ? <ApplyLeavePage user={authStore.user} /> : null
})

const TeamLeaveRoute = observer(() => {
    const { authStore } = useStore()
    const user = authStore.user
    if (!user) return null
    return isAdministrator(user.roles)
        ? <AllLeaveAdminPage user={user} />
        : <TeamLeavePage user={user} />
})

const TimesheetsRoute = observer(() => {
    const { authStore } = useStore()
    const user = authStore.user
    if (!user) return null
    return isAdministrator(user.roles)
        ? <TeamTimesheetPage user={user} />
        : <MyTimesheetPage user={user} />
})

const TeamTimesheetsRoute = observer(() => {
    const { authStore } = useStore()
    const user = authStore.user
    if (!user) return null
    return isAdministrator(user.roles)
        ? <AllTimesheetsPage />
        : <TeamTimesheetPage user={user} />
})

const NewTimesheetRoute = observer(() => {
    const { authStore } = useStore()
    return authStore.user ? <NewTimesheetPage user={authStore.user} /> : null
})

const AppInner = observer(function AppInner() {
    const { authStore, uiStore } = useStore()
    const queryClient = useQueryClient()
    const navigate = useNavigate()

    const [apiErrorOpen, setApiErrorOpen] = useState(false)
    const [apiErrorMessage, setApiErrorMessage] = useState('')
    const [isOffline, setIsOffline] = useState(() => typeof navigator !== 'undefined' && !navigator.onLine)

    useEffect(() => {
        // The PWA service worker can serve cached UI shells offline; this banner
        // tells the user that *new* data won't load until they're back online.
        const onOnline = () => setIsOffline(false)
        const onOffline = () => setIsOffline(true)
        window.addEventListener('online', onOnline)
        window.addEventListener('offline', onOffline)
        return () => {
            window.removeEventListener('online', onOnline)
            window.removeEventListener('offline', onOffline)
        }
    }, [])

    useEffect(() => {
        uiStore.setNavigate(navigate)
    }, [uiStore, navigate])

    useEffect(() => {
        void authStore.hydrateUser()
    }, [authStore])

    useEffect(() => {
        let lastMessage = ''
        let lastAt = 0

        const compactMessage = (message: string) => {
            const firstSentence = message.split('. ')[0]?.trim() ?? message.trim()
            const normalized = firstSentence.endsWith('.') ? firstSentence : `${firstSentence}.`
            return normalized.length > 120 ? `${normalized.slice(0, 117)}...` : normalized
        }

        const onApiError = (event: Event) => {
            const customEvent = event as CustomEvent<{ message?: string }>
            const message = customEvent.detail?.message?.trim()
            if (!message) return

            const compact = compactMessage(message)
            const now = Date.now()
            if (compact === lastMessage && now - lastAt < 2000) return

            lastMessage = compact
            lastAt = now
            setApiErrorMessage(compact)
            setApiErrorOpen(true)
        }

        window.addEventListener(API_ERROR_EVENT, onApiError as EventListener)
        return () => window.removeEventListener(API_ERROR_EVENT, onApiError as EventListener)
    }, [])

    // A 401 on an ordinary request: the session expired, or the account was
    // deactivated. Sign out locally and forget everything cached for that user;
    // the route guard takes it to the sign-in screen.
    useEffect(() => {
        const onSessionExpired = () => {
            if (!authStore.user) return
            authStore.expireSession()
            queryClient.clear()
            setApiErrorMessage('Your session has expired. Please sign in again.')
            setApiErrorOpen(true)
        }
        window.addEventListener(SESSION_EXPIRED_EVENT, onSessionExpired)
        return () => window.removeEventListener(SESSION_EXPIRED_EVENT, onSessionExpired)
    }, [authStore, queryClient])

    // Keyed on the id, not the user object: editing your own profile replaces
    // the object, and used to tear the connection down and build it again.
    const signedInUserId = authStore.user?.id
    useEffect(() => {
        if (!signedInUserId) return

        const hubBaseUrl = apiBaseUrl.endsWith('/api') ? apiBaseUrl.slice(0, -4) : apiBaseUrl
        const connection = new HubConnectionBuilder()
            .withUrl(`${hubBaseUrl}/hubs/notifications`, { withCredentials: true })
            .withAutomaticReconnect(reconnectPolicy)
            .configureLogging(LogLevel.Warning)
            .build()

        const refresh = () => {
            void queryClient.invalidateQueries({ queryKey: ['leaveStatusHistories'] })
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            void queryClient.invalidateQueries({ queryKey: ['teamAwayThisWeekCount'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
            void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        }
        connection.on('notificationsUpdated', refresh)

        // Retries a failed start and a closed connection for as long as it takes,
        // and refreshes after every gap, since events sent meanwhile are lost.
        const stop = keepConnected(connection, refresh)

        return () => {
            connection.off('notificationsUpdated')
            stop()
        }
    }, [signedInUserId, queryClient])

    if (!authStore.hasCheckedAuth && authStore.isLoadingUser) {
        return (
            <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'background.default' }}>
                <Stack spacing={2} alignItems="center">
                    <CircularProgress />
                    <Typography color="text.secondary">Loading your workspace...</Typography>
                </Stack>
            </Box>
        )
    }

    return (
        <>
            <Routes>
                {/* Public auth routes. Accounts are created by an administrator,
                    so /register is retired — it redirects to sign-in rather than
                    leaving a usable public self-registration screen behind. */}
                <Route path="/login" element={<AuthGate />} />
                <Route path="/register" element={<Navigate to="/login" replace />} />
                <Route path="/forgot-password" element={<AuthGate />} />
                <Route path="/reset-password" element={<AuthGate />} />

                {/* Protected app shell with layout-route + Outlet */}
                <Route element={<ProtectedRoute />}>
                    <Route element={<AppShell />}>
                        <Route path="/" element={<Navigate to="/dashboard" replace />} />
                        <Route path="/dashboard" element={<DashboardHome />} />
                        {/* Leave & Time — everyone but the System Administrator, who configures the
                            workspace and neither files nor decides leave. The HR Administrator runs
                            these pages company-wide; a Manager and an Employee see their own scope. */}
                        <Route element={<ProtectedRoute roles={[...LEAVE_AND_TIME_ROLES]} />}>
                            <Route path="/my-leave" element={<Navigate to="/my-leave/requests" replace />} />
                            <Route path="/my-leave/:section" element={<MyLeaveRoute />} />
                            <Route path="/apply-leave" element={<ApplyLeaveRoute />} />
                            <Route path="/leave-management" element={<TeamLeaveRoute />} />
                            <Route path="/timesheets" element={<TimesheetsRoute />} />
                            <Route path="/timesheets-management" element={<TeamTimesheetsRoute />} />
                            <Route path="/new-timesheet" element={<NewTimesheetRoute />} />
                            <Route path="/attendance" element={<AttendancePage />} />
                            <Route path="/team-attendance" element={<TeamAttendancePage />} />
                            <Route path="/attendance-management" element={<CompanyAttendancePage />} />
                        </Route>

                        {/* Approved Projects (the Tasks page) — Managers and HR Administrators hand each other follow-ups. */}
                        <Route element={<ProtectedRoute roles={[...TASK_ROLES]} />}>
                            <Route path="/approved-projects" element={<TasksPage />} />
                            {/* The page's old address, kept so a bookmark still lands. */}
                            <Route path="/tasks" element={<Navigate to="/approved-projects" replace />} />
                        </Route>

                        {/* System Administrator-only nested routes — gated by role inside ProtectedRoute. An HR Administrator has the reach but not the configuration. */}
                        <Route element={<ProtectedRoute roles={[...SYSTEM_ADMINISTRATOR_ROLES]} />}>
                            <Route path="/admin" element={<Navigate to="/admin/users" replace />} />
                            <Route path="/admin/general" element={<Navigate to="/admin/reminders-notifications" replace />} />
                            <Route path="/admin/activity-types" element={<Navigate to="/admin/project-activities" replace />} />
                            <Route path="/admin/:section" element={<DashboardHome />} />
                        </Route>

                        <Route path="*" element={<Navigate to="/dashboard" replace />} />
                    </Route>
                </Route>
            </Routes>

            <Snackbar
                open={apiErrorOpen}
                autoHideDuration={4500}
                onClose={() => setApiErrorOpen(false)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            >
                <Alert onClose={() => setApiErrorOpen(false)} severity="error" variant="filled" sx={{ width: '100%' }}>
                    {apiErrorMessage}
                </Alert>
            </Snackbar>

            {/* Persistent offline banner — no autoHideDuration so it stays visible
              * until the connection comes back. Anchored top-center to keep it out
              * of the way of the API-error toast at the bottom-right. */}
            <Snackbar
                open={isOffline}
                anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
            >
                <Alert severity="warning" variant="filled" sx={{ width: '100%' }}>
                    You're offline — changes won't sync until your connection returns.
                </Alert>
            </Snackbar>
        </>
    )
})

export default function App() {
    return (
        <BrowserRouter>
            <AppInner />
        </BrowserRouter>
    )
}
