import { observer } from 'mobx-react-lite'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useStore } from '../../lib/mobx'
import type { UserRole } from '../../lib/types'

interface ProtectedRouteProps {
    /**
     * If provided, the authenticated user must have at least one of these roles.
     * Otherwise they are redirected to /dashboard.
     */
    roles?: string[]
}

const ProtectedRoute = observer(function ProtectedRoute({ roles }: ProtectedRouteProps) {
    const { authStore } = useStore()
    const location = useLocation()

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

    if (!authStore.user) {
        // Preserve the originally-requested URL so the login flow can return here.
        return <Navigate to="/login" replace state={{ from: location }} />
    }

    if (roles && roles.length > 0) {
        const userRoles = authStore.user.roles ?? []
        const allowed = roles.some((role) => userRoles.includes(role as UserRole))
        if (!allowed) {
            return <Navigate to="/dashboard" replace />
        }
    }

    return <Outlet />
})

export default ProtectedRoute
