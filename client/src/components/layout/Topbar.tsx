import { useEffect, useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import CircleRoundedIcon from '@mui/icons-material/CircleRounded'
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded'
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded'
import NotificationsNoneRoundedIcon from '@mui/icons-material/NotificationsNoneRounded'
import SettingsBrightnessRoundedIcon from '@mui/icons-material/SettingsBrightnessRounded'
import Badge from '@mui/material/Badge'
import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { getAnnualLeaves, getLeaveStatusHistories, getTimesheets } from '../../lib/api'
import { useStore } from '../../lib/mobx'
import type { ThemePreference } from '../../lib/mobx/uiStore'
import AttendanceWidget from './AttendanceWidget'

const recentWindowDays = 7
const managerReadPrefix = 'manager-read-leave-notifications:'
const managerTsReadPrefix = 'manager-read-timesheet-notifications:'
const employeeReadPrefix = 'employee-read-status-notifications:'
const notificationRefreshMs = 15000

function getStoredIds(key: string): string[] {
    const raw = window.localStorage.getItem(key)
    if (!raw) return []
    try {
        const parsed = JSON.parse(raw)
        return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []
    } catch {
        return []
    }
}

function formatChangedAt(changedAt: string) {
    const date = new Date(changedAt)
    if (Number.isNaN(date.getTime())) return 'Recently'
    return new Intl.DateTimeFormat(undefined, {
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
    }).format(date)
}

function scrollToId(id: string) {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

const Topbar = observer(function Topbar() {
    const { authStore, uiStore } = useStore()
    const location = useLocation()
    const isAdminUser = authStore.user?.roles?.includes('Admin') ?? false
    const isManagerUser = authStore.user?.roles?.includes('Manager') ?? false
    const shouldUseManagerNotifications = isManagerUser && !isAdminUser

    const managerKey = `${managerReadPrefix}${authStore.user?.id ?? ''}`
    const managerTsKey = `${managerTsReadPrefix}${authStore.user?.id ?? ''}`
    const employeeKey = `${employeeReadPrefix}${authStore.user?.id ?? ''}`

    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null)
    const [readManagerIds, setReadManagerIds] = useState<string[]>(() => getStoredIds(managerKey))
    const [readManagerTsIds, setReadManagerTsIds] = useState<string[]>(() => getStoredIds(managerTsKey))
    const [readEmployeeIds, setReadEmployeeIds] = useState<string[]>(() => getStoredIds(employeeKey))

    const { data: statusHistories, isLoading: isLoadingStatus } = useQuery({
        queryKey: ['leaveStatusHistories'],
        queryFn: getLeaveStatusHistories,
        enabled: authStore.isAuthenticated,
        refetchInterval: authStore.isAuthenticated ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const { data: annualLeaves, isLoading: isLoadingLeaves } = useQuery({
        queryKey: ['annualLeaves'],
        queryFn: getAnnualLeaves,
        enabled: authStore.isAuthenticated && shouldUseManagerNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseManagerNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const { data: timesheets, isLoading: isLoadingTimesheets } = useQuery({
        queryKey: ['timesheets'],
        queryFn: getTimesheets,
        enabled: authStore.isAuthenticated && shouldUseManagerNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseManagerNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const sortedStatusNotifs = (statusHistories ?? [])
        .slice()
        .sort((a, b) => new Date(b.changedAt).getTime() - new Date(a.changedAt).getTime())
        .slice(0, 6)
    const employeeNotifications = sortedStatusNotifs.filter((item) => item.changedByUserId !== authStore.user?.id)

    const managerPendingRequests = (annualLeaves ?? [])
        .filter((l) => l.status === 'Pending' && l.employeeId !== authStore.user?.id)
        .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())

    const managerPendingTimesheets = (timesheets ?? [])
        .filter((t) => t.status === 'Submitted' || t.status === 'Resubmitted')
        .sort((a, b) => {
            const aDate = a.submittedAt ?? a.createdAt
            const bDate = b.submittedAt ?? b.createdAt
            return new Date(bDate).getTime() - new Date(aDate).getTime()
        })

    const readManagerSet = useMemo(() => new Set(readManagerIds), [readManagerIds])
    const readManagerTsSet = useMemo(() => new Set(readManagerTsIds), [readManagerTsIds])
    const readEmployeeSet = useMemo(() => new Set(readEmployeeIds), [readEmployeeIds])

    const unreadManagerRequests = managerPendingRequests.filter((item) => !readManagerSet.has(item.id))
    const unreadManagerTimesheets = managerPendingTimesheets.filter((item) => !readManagerTsSet.has(item.id))
    const unreadEmployeeNotifs = employeeNotifications.filter((item) => !readEmployeeSet.has(item.id))
    const recentThreshold = Date.now() - recentWindowDays * 24 * 60 * 60 * 1000
    const unreadCount = shouldUseManagerNotifications
        ? unreadManagerRequests.length + unreadManagerTimesheets.length
        : unreadEmployeeNotifs.filter((item) => new Date(item.changedAt).getTime() >= recentThreshold).length

    const managerNotifications = unreadManagerRequests.slice(0, 6)
    const managerTsNotifications = unreadManagerTimesheets.slice(0, 6)
    const isLoading = shouldUseManagerNotifications ? (isLoadingLeaves || isLoadingTimesheets) : isLoadingStatus

    useEffect(() => { setReadManagerIds(getStoredIds(managerKey)) }, [managerKey])
    useEffect(() => { setReadManagerTsIds(getStoredIds(managerTsKey)) }, [managerTsKey])
    useEffect(() => { setReadEmployeeIds(getStoredIds(employeeKey)) }, [employeeKey])

    useEffect(() => {
        if (!shouldUseManagerNotifications || isLoadingLeaves || !annualLeaves) return
        const pendingIds = new Set(managerPendingRequests.map((l) => l.id))
        const pruned = readManagerIds.filter((id) => pendingIds.has(id))
        if (pruned.length !== readManagerIds.length) {
            setReadManagerIds(pruned)
            window.localStorage.setItem(managerKey, JSON.stringify(pruned))
        }
    }, [annualLeaves, isLoadingLeaves, managerPendingRequests, managerKey, readManagerIds, shouldUseManagerNotifications])

    useEffect(() => {
        if (!shouldUseManagerNotifications || isLoadingTimesheets || !timesheets) return
        const pendingIds = new Set(managerPendingTimesheets.map((t) => t.id))
        const pruned = readManagerTsIds.filter((id) => pendingIds.has(id))
        if (pruned.length !== readManagerTsIds.length) {
            setReadManagerTsIds(pruned)
            window.localStorage.setItem(managerTsKey, JSON.stringify(pruned))
        }
    }, [timesheets, isLoadingTimesheets, managerPendingTimesheets, managerTsKey, readManagerTsIds, shouldUseManagerNotifications])

    useEffect(() => {
        if (!statusHistories) return
        const historyIds = new Set(statusHistories.map((item) => item.id))
        const pruned = readEmployeeIds.filter((id) => historyIds.has(id))
        if (pruned.length !== readEmployeeIds.length) {
            setReadEmployeeIds(pruned)
            window.localStorage.setItem(employeeKey, JSON.stringify(pruned))
        }
    }, [statusHistories, employeeKey, readEmployeeIds])

    const handleManagerClick = (leaveId: string) => {
        const updated = Array.from(new Set([...readManagerIds, leaveId]))
        setReadManagerIds(updated)
        window.localStorage.setItem(managerKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToTeamLeave()
        window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#team-leave-approvals`)
        window.dispatchEvent(new HashChangeEvent('hashchange'))
    }

    const handleManagerTimesheetClick = (timesheetId: string) => {
        const updated = Array.from(new Set([...readManagerTsIds, timesheetId]))
        setReadManagerTsIds(updated)
        window.localStorage.setItem(managerTsKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToTeamTimesheets()
    }

    const handleEmployeeClick = (notifId: string, leaveId: string) => {
        const updated = Array.from(new Set([...readEmployeeIds, notifId]))
        setReadEmployeeIds(updated)
        window.localStorage.setItem(employeeKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToMyLeave('requests')
        window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#my-requests`)
        window.dispatchEvent(new HashChangeEvent('hashchange'))
        window.setTimeout(() => scrollToId(`leave-card-${leaveId}`), 100)
    }

    let pageTitle = 'Dashboard'
    const path = location.pathname
    if (path.startsWith('/my-leave')) pageTitle = 'My Leave'
    else if (path.startsWith('/apply-leave')) pageTitle = 'Apply for Leave'
    else if (path.startsWith('/team-leave')) pageTitle = isAdminUser ? 'All Leave Requests' : 'Team Leave'
    else if (path.startsWith('/team-timesheets')) pageTitle = isAdminUser ? 'All Timesheets' : 'Team Timesheets'
    else if (path.startsWith('/timesheets')) pageTitle = isAdminUser ? 'All Timesheets' : 'My Timesheets'
    else if (path.startsWith('/new-timesheet')) pageTitle = 'Apply Timesheet'
    else if (path.startsWith('/team-attendance')) pageTitle = 'Team Attendance'
    else if (path.startsWith('/company-attendance')) pageTitle = 'Company Attendance'
    else if (path.startsWith('/attendance')) pageTitle = 'My Attendance'
    else if (path.startsWith('/admin/')) {
        const s = path.split('/')[2]
        if (s === 'users') pageTitle = 'User Management'
        else if (s === 'departments') pageTitle = 'Departments'
        else if (s === 'leave-types' || s === 'leave') pageTitle = 'Leave Types'
        else if (s === 'projects') pageTitle = 'Projects'
        else if (s === 'settings') pageTitle = 'Leave Settings'
        else pageTitle = 'Administration'
    }

    return (
        <Box
            component="header"
            sx={{
                position: 'sticky',
                top: 0,
                zIndex: 1100,
                bgcolor: 'background.paper',
                borderBottom: 1,
                borderColor: 'divider',
                height: 56,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                px: 3,
                flexShrink: 0,
            }}
        >
            <Typography sx={{ fontSize: 16, fontWeight: 600, color: 'text.primary' }}>
                {pageTitle}
            </Typography>

            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <AttendanceWidget enabled={!!authStore.user && !isAdminUser} />
                <ToggleButtonGroup
                    size="small"
                    exclusive
                    value={uiStore.themePreference}
                    onChange={(_, next: ThemePreference | null) => { if (next) uiStore.setThemePreference(next) }}
                    aria-label="Color mode"
                    sx={{ '& .MuiToggleButton-root': { px: 1, py: 0.5, border: 0 } }}
                >
                    <Tooltip title="Light">
                        <ToggleButton value="light" aria-label="Light mode">
                            <LightModeRoundedIcon fontSize="small" />
                        </ToggleButton>
                    </Tooltip>
                    <Tooltip title="Dark">
                        <ToggleButton value="dark" aria-label="Dark mode">
                            <DarkModeRoundedIcon fontSize="small" />
                        </ToggleButton>
                    </Tooltip>
                    <Tooltip title="System · dark chrome, light body">
                        <ToggleButton value="system" aria-label="System mode">
                            <SettingsBrightnessRoundedIcon fontSize="small" />
                        </ToggleButton>
                    </Tooltip>
                </ToggleButtonGroup>
                <Tooltip title="Notifications">
                    <IconButton size="small" onClick={(e) => setAnchorEl(e.currentTarget)}>
                        <Badge badgeContent={unreadCount} color="error">
                            <NotificationsNoneRoundedIcon />
                        </Badge>
                    </IconButton>
                </Tooltip>
            </Box>

            <Menu
                anchorEl={anchorEl}
                open={Boolean(anchorEl)}
                onClose={() => setAnchorEl(null)}
                transformOrigin={{ horizontal: 'right', vertical: 'top' }}
                anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
                slotProps={{ paper: { sx: { minWidth: 280, maxWidth: 360 } } }}
            >
                <MenuItem disabled>
                    <Typography variant="subtitle2" fontWeight={700}>Notifications</Typography>
                </MenuItem>
                <Divider />
                {isLoading && (
                    <MenuItem disabled><ListItemText primary="Loading notifications..." /></MenuItem>
                )}
                {!isLoading && shouldUseManagerNotifications && managerNotifications.length === 0 && managerTsNotifications.length === 0 && (
                    <MenuItem disabled><ListItemText primary="No notifications yet" /></MenuItem>
                )}
                {!isLoading && !shouldUseManagerNotifications && employeeNotifications.length === 0 && (
                    <MenuItem disabled><ListItemText primary="No notifications yet" /></MenuItem>
                )}
                {!isLoading && shouldUseManagerNotifications && managerNotifications.map((item) => (
                    <MenuItem key={item.id} onClick={() => handleManagerClick(item.id)}>
                        <ListItemIcon>
                            <CircleRoundedIcon sx={{ fontSize: 10, color: 'error.main' }} />
                        </ListItemIcon>
                        <ListItemText
                            primary={`New leave request from ${item.employeeName}`}
                            secondary={`Submitted ${formatChangedAt(item.createdAt)}`}
                        />
                    </MenuItem>
                ))}
                {!isLoading && shouldUseManagerNotifications && managerTsNotifications.map((item) => (
                    <MenuItem key={item.id} onClick={() => handleManagerTimesheetClick(item.id)}>
                        <ListItemIcon>
                            <CircleRoundedIcon sx={{ fontSize: 10, color: 'error.main' }} />
                        </ListItemIcon>
                        <ListItemText
                            primary={`${item.status === 'Resubmitted' ? 'Resubmitted' : 'New'} timesheet from ${item.employeeName}`}
                            secondary={`Submitted ${formatChangedAt(item.submittedAt ?? item.createdAt)}`}
                        />
                    </MenuItem>
                ))}
                {!isLoading && !shouldUseManagerNotifications && employeeNotifications.map((item) => {
                    const changedAt = new Date(item.changedAt).getTime()
                    const isUnread = !readEmployeeSet.has(item.id)
                    const isRecent = !Number.isNaN(changedAt) && changedAt >= recentThreshold
                    return (
                        <MenuItem key={item.id} onClick={() => handleEmployeeClick(item.id, item.annualLeaveId)}>
                            <ListItemIcon>
                                <CircleRoundedIcon sx={{ fontSize: 10, color: isUnread && isRecent ? 'error.main' : 'divider' }} />
                            </ListItemIcon>
                            <ListItemText
                                primary={`Status changed to ${item.newStatus}`}
                                secondary={`Updated ${formatChangedAt(item.changedAt)}`}
                            />
                        </MenuItem>
                    )
                })}
            </Menu>
        </Box>
    )
})

export default Topbar
