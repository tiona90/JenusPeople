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
import { API_ERROR_EVENT } from './lib/api/error-events'
import { apiBaseUrl } from './lib/api/client'
import { useStore } from './lib/mobx'
import Sidebar from './components/layout/Sidebar'
import Topbar from './components/layout/Topbar'

type AuthView = 'login' | 'register' | 'forgot-password' | 'reset-password'

function getAuthViewFromPath(pathname: string): AuthView {
    if (pathname.startsWith('/register')) return 'register'
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
            onSwitchToLogin={() => navigate('/login')}
            onSwitchToRegister={() => navigate('/register')}
            onForgotPassword={() => navigate('/forgot-password')}
            onBackToLogin={() => navigate('/login')}
            onRequestNewLink={() => navigate('/forgot-password')}
        />
    )
})

const AppShell = observer(function AppShell() {
    const { authStore } = useStore()

    if (!authStore.user) return null

    return (
        <Box sx={{ minHeight: '100vh', bgcolor: 'background.default', display: 'flex' }}>
            <Sidebar />
            <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                <Topbar />
                <Container component="main" maxWidth="lg" sx={{ pt: 3, pb: 4 }}>
                    <Outlet />
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
    return user.roles.includes('Admin')
        ? <AllLeaveAdminPage user={user} />
        : <TeamLeavePage user={user} />
})

const TimesheetsRoute = observer(() => {
    const { authStore } = useStore()
    const user = authStore.user
    if (!user) return null
    return user.roles.includes('Admin')
        ? <TeamTimesheetPage user={user} />
        : <MyTimesheetPage user={user} />
})

const TeamTimesheetsRoute = observer(() => {
    const { authStore } = useStore()
    const user = authStore.user
    if (!user) return null
    return user.roles.includes('Admin')
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

    useEffect(() => {
        if (!authStore.user) return

        const hubBaseUrl = apiBaseUrl.endsWith('/api') ? apiBaseUrl.slice(0, -4) : apiBaseUrl
        const connection = new HubConnectionBuilder()
            .withUrl(`${hubBaseUrl}/hubs/notifications`, { withCredentials: true })
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Warning)
            .build()

        connection.on('notificationsUpdated', () => {
            void queryClient.invalidateQueries({ queryKey: ['leaveStatusHistories'] })
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            void queryClient.invalidateQueries({ queryKey: ['teamAwayThisWeekCount'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
        })

        void connection.start().catch(() => { /* polling fallback */ })

        return () => {
            connection.off('notificationsUpdated')
            void connection.stop()
        }
    }, [authStore.user, queryClient])

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
                {/* Public auth routes */}
                <Route path="/login" element={<AuthGate />} />
                <Route path="/register" element={<AuthGate />} />
                <Route path="/forgot-password" element={<AuthGate />} />
                <Route path="/reset-password" element={<AuthGate />} />

                {/* Protected app shell with layout-route + Outlet */}
                <Route element={<ProtectedRoute />}>
                    <Route element={<AppShell />}>
                        <Route path="/" element={<Navigate to="/dashboard" replace />} />
                        <Route path="/dashboard" element={<DashboardHome />} />
                        <Route path="/my-leave" element={<Navigate to="/my-leave/requests" replace />} />
                        <Route path="/my-leave/:section" element={<MyLeaveRoute />} />
                        <Route path="/apply-leave" element={<ApplyLeaveRoute />} />
                        <Route path="/team-leave" element={<TeamLeaveRoute />} />
                        <Route path="/timesheets" element={<TimesheetsRoute />} />
                        <Route path="/team-timesheets" element={<TeamTimesheetsRoute />} />
                        <Route path="/new-timesheet" element={<NewTimesheetRoute />} />
                        <Route path="/attendance" element={<AttendancePage />} />
                        <Route path="/team-attendance" element={<TeamAttendancePage />} />
                        <Route path="/company-attendance" element={<CompanyAttendancePage />} />

                        {/* Admin-only nested routes — gated by role inside ProtectedRoute */}
                        <Route element={<ProtectedRoute roles={['Admin']} />}>
                            <Route path="/admin" element={<Navigate to="/admin/users" replace />} />
                            <Route path="/admin/general" element={<Navigate to="/admin/reminders-notifications" replace />} />
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
