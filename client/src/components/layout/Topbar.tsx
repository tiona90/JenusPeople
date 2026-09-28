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
import { getAnnualLeaves, getLeaveStatusHistories, getSystemErrors, getTimesheets, getTimesheetStatusHistories } from '../../lib/api'
import { canDecide, isTimesheetWithManager, statusPhrase } from '../../lib/approval-stage'
import { useStore } from '../../lib/mobx'
import { isAdministrator, isHrAdministrator, isSystemAdministrator } from '../../lib/roles'
import { formatServerDateTime as formatChangedAt, parseServerDate } from '../../lib/server-date'
import { shortExceptionType, systemErrorRowId } from '../../lib/system-errors'
import type { ThemePreference } from '../../lib/mobx/uiStore'
import AttendanceWidget from './AttendanceWidget'

const recentWindowDays = 7
const managerReadPrefix = 'manager-read-leave-notifications:'
const managerTsReadPrefix = 'manager-read-timesheet-notifications:'
const employeeReadPrefix = 'employee-read-status-notifications:'
const employeeTsReadPrefix = 'employee-read-timesheet-status-notifications:'
// A System Administrator's bell lists system errors, not leave. Keyed by row id plus
// last occurrence, so a fault that comes back after being read is unread again.
const systemReadPrefix = 'system-read-error-notifications:'
const systemErrorReadKey = (id: number, lastOccurredAtUtc: string) => `${id}@${lastOccurredAtUtc}`
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

function scrollToId(id: string) {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

const Topbar = observer(function Topbar() {
    const { authStore, uiStore } = useStore()
    const location = useLocation()
    const isAdminUser = isAdministrator(authStore.user?.roles)
    const isManagerUser = authStore.user?.roles?.includes('Manager') ?? false
    // Three bells. A Manager's and an HR Administrator's list what they can decide —
    // for HR that is the requests awaiting HR approval (canDecide with no type reads
    // a Pending row as the manager's), not the employee's status feed it used to get;
    // an Employee's lists what happened to their own leave and timesheets; a System
    // Administrator's lists the errors the system hit — the role neither files nor
    // decides leave, so the status feed was everyone else's news, and the one thing
    // the role is emailed about (SystemErrorNotifier) never reached it.
    const isSystemAdminUser = isSystemAdministrator(authStore.user?.roles)
    const isHrAdminUser = isHrAdministrator(authStore.user?.roles)
    const shouldUseManagerNotifications = (isManagerUser && !isAdminUser) || isHrAdminUser
    const shouldUseSystemNotifications = isSystemAdminUser
    const shouldUseEmployeeNotifications = !shouldUseManagerNotifications && !shouldUseSystemNotifications

    const managerKey = `${managerReadPrefix}${authStore.user?.id ?? ''}`
    const managerTsKey = `${managerTsReadPrefix}${authStore.user?.id ?? ''}`
    const employeeKey = `${employeeReadPrefix}${authStore.user?.id ?? ''}`
    const employeeTsKey = `${employeeTsReadPrefix}${authStore.user?.id ?? ''}`
    const systemKey = `${systemReadPrefix}${authStore.user?.id ?? ''}`

    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null)
    const [readManagerIds, setReadManagerIds] = useState<string[]>(() => getStoredIds(managerKey))
    const [readManagerTsIds, setReadManagerTsIds] = useState<string[]>(() => getStoredIds(managerTsKey))
    const [readEmployeeIds, setReadEmployeeIds] = useState<string[]>(() => getStoredIds(employeeKey))
    const [readEmployeeTsIds, setReadEmployeeTsIds] = useState<string[]>(() => getStoredIds(employeeTsKey))
    const [readSystemKeys, setReadSystemKeys] = useState<string[]>(() => getStoredIds(systemKey))

    const { data: statusHistories, isLoading: isLoadingStatus } = useQuery({
        queryKey: ['leaveStatusHistories'],
        queryFn: getLeaveStatusHistories,
        enabled: authStore.isAuthenticated && shouldUseEmployeeNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseEmployeeNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const { data: tsStatusHistories, isLoading: isLoadingTsStatus } = useQuery({
        queryKey: ['timesheetStatusHistories'],
        queryFn: getTimesheetStatusHistories,
        enabled: authStore.isAuthenticated && shouldUseEmployeeNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseEmployeeNotifications ? notificationRefreshMs : false,
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
        queryFn: () => getTimesheets(),
        enabled: authStore.isAuthenticated && shouldUseManagerNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseManagerNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const { data: systemErrors, isLoading: isLoadingSystemErrors } = useQuery({
        queryKey: ['systemErrors'],
        queryFn: getSystemErrors,
        enabled: authStore.isAuthenticated && shouldUseSystemNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseSystemNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })

    const tsTime = (s: string) => parseServerDate(s)?.getTime() ?? 0

    const employeeLeaveItems = (statusHistories ?? [])
        .filter((item) => item.changedByUserId !== authStore.user?.id)
        .map((item) => ({ kind: 'leave' as const, item, ts: tsTime(item.changedAt) }))

    const employeeTsItems = (tsStatusHistories ?? [])
        .filter((item) => item.changedByUserId !== authStore.user?.id)
        .map((item) => ({ kind: 'timesheet' as const, item, ts: tsTime(item.changedAt) }))

    const employeeMerged = [...employeeLeaveItems, ...employeeTsItems]
        .sort((a, b) => b.ts - a.ts)
        .slice(0, 8)

    const employeeNotifications = employeeLeaveItems.map(e => e.item)
    const employeeTsNotifications = employeeTsItems.map(e => e.item)

    const managerPendingRequests = (annualLeaves ?? [])
        .filter((l) => canDecide(l, { isHrAdministrator: isHrAdminUser }, undefined) && l.employeeId !== authStore.user?.id)
        .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())

    const managerPendingTimesheets = (timesheets ?? [])
        .filter((t) => (t.status === 'Submitted' || t.status === 'Resubmitted')
            && !isTimesheetWithManager(t, { isHrAdministrator: isHrAdminUser }))
        .sort((a, b) => {
            const aDate = a.submittedAt ?? a.createdAt
            const bDate = b.submittedAt ?? b.createdAt
            return new Date(bDate).getTime() - new Date(aDate).getTime()
        })

    const readManagerSet = useMemo(() => new Set(readManagerIds), [readManagerIds])
    const readManagerTsSet = useMemo(() => new Set(readManagerTsIds), [readManagerTsIds])
    const readEmployeeSet = useMemo(() => new Set(readEmployeeIds), [readEmployeeIds])
    const readEmployeeTsSet = useMemo(() => new Set(readEmployeeTsIds), [readEmployeeTsIds])
    const readSystemSet = useMemo(() => new Set(readSystemKeys), [readSystemKeys])

    const unreadManagerRequests = managerPendingRequests.filter((item) => !readManagerSet.has(item.id))
    const unreadManagerTimesheets = managerPendingTimesheets.filter((item) => !readManagerTsSet.has(item.id))
    const unreadEmployeeNotifs = employeeNotifications.filter((item) => !readEmployeeSet.has(item.id))
    const unreadEmployeeTsNotifs = employeeTsNotifications.filter((item) => !readEmployeeTsSet.has(item.id))
    const recentThreshold = Date.now() - recentWindowDays * 24 * 60 * 60 * 1000
    const systemNotifications = (systemErrors ?? []).slice(0, 8)
    const isSystemErrorUnread = (id: number, lastOccurredAtUtc: string) => !readSystemSet.has(systemErrorReadKey(id, lastOccurredAtUtc))
    const unreadSystemErrors = systemNotifications.filter((e) => isSystemErrorUnread(e.id, e.lastOccurredAtUtc))
    const unreadCount = shouldUseManagerNotifications
        ? unreadManagerRequests.length + unreadManagerTimesheets.length
        : shouldUseSystemNotifications
            ? unreadSystemErrors.filter((e) => tsTime(e.lastOccurredAtUtc) >= recentThreshold).length
            : unreadEmployeeNotifs.filter((item) => new Date(item.changedAt).getTime() >= recentThreshold).length
              + unreadEmployeeTsNotifs.filter((item) => new Date(item.changedAt).getTime() >= recentThreshold).length

    const managerNotifications = unreadManagerRequests.slice(0, 6)
    const managerTsNotifications = unreadManagerTimesheets.slice(0, 6)
    const isLoading = shouldUseManagerNotifications
        ? (isLoadingLeaves || isLoadingTimesheets)
        : shouldUseSystemNotifications
            ? isLoadingSystemErrors
            : (isLoadingStatus || isLoadingTsStatus)

    useEffect(() => { setReadManagerIds(getStoredIds(managerKey)) }, [managerKey])
    useEffect(() => { setReadManagerTsIds(getStoredIds(managerTsKey)) }, [managerTsKey])
    useEffect(() => { setReadEmployeeIds(getStoredIds(employeeKey)) }, [employeeKey])
    useEffect(() => { setReadEmployeeTsIds(getStoredIds(employeeTsKey)) }, [employeeTsKey])
    useEffect(() => { setReadSystemKeys(getStoredIds(systemKey)) }, [systemKey])

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

    useEffect(() => {
        if (!tsStatusHistories) return
        const historyIds = new Set(tsStatusHistories.map((item) => item.id))
        const pruned = readEmployeeTsIds.filter((id) => historyIds.has(id))
        if (pruned.length !== readEmployeeTsIds.length) {
            setReadEmployeeTsIds(pruned)
            window.localStorage.setItem(employeeTsKey, JSON.stringify(pruned))
        }
    }, [tsStatusHistories, employeeTsKey, readEmployeeTsIds])

    useEffect(() => {
        if (!systemErrors) return
        const liveKeys = new Set(systemErrors.map((e) => systemErrorReadKey(e.id, e.lastOccurredAtUtc)))
        const pruned = readSystemKeys.filter((key) => liveKeys.has(key))
        if (pruned.length !== readSystemKeys.length) {
            setReadSystemKeys(pruned)
            window.localStorage.setItem(systemKey, JSON.stringify(pruned))
        }
    }, [systemErrors, systemKey, readSystemKeys])

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

    const handleEmployeeTsClick = (notifId: string) => {
        const updated = Array.from(new Set([...readEmployeeTsIds, notifId]))
        setReadEmployeeTsIds(updated)
        window.localStorage.setItem(employeeTsKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToTimesheets()
    }

    const handleSystemErrorClick = (id: number, lastOccurredAtUtc: string) => {
        const updated = Array.from(new Set([...readSystemKeys, systemErrorReadKey(id, lastOccurredAtUtc)]))
        setReadSystemKeys(updated)
        window.localStorage.setItem(systemKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToAdminSection('system-log')
        window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#${systemErrorRowId(id)}`)
        window.dispatchEvent(new HashChangeEvent('hashchange'))
        window.setTimeout(() => scrollToId(systemErrorRowId(id)), 100)
    }

    let pageTitle = 'Dashboard'
    const path = location.pathname
    if (path.startsWith('/my-leave')) pageTitle = 'My Leave'
    else if (path.startsWith('/apply-leave')) pageTitle = 'Apply for Leave'
    else if (path.startsWith('/leave-management')) pageTitle = isAdminUser ? 'Leave Management' : 'Team Leave'
    else if (path.startsWith('/timesheets-management')) pageTitle = isAdminUser ? 'Timesheets' : 'Approvals'
    else if (path.startsWith('/timesheets')) pageTitle = isAdminUser ? 'All Timesheets' : 'My Timesheets'
    else if (path.startsWith('/new-timesheet')) pageTitle = 'Submit Timesheet'
    else if (path.startsWith('/team-attendance')) pageTitle = 'Team Attendance'
    else if (path.startsWith('/attendance-management')) pageTitle = 'Attendance'
    else if (path.startsWith('/attendance')) pageTitle = 'My Attendance'
    else if (path.startsWith('/admin/')) {
        const s = path.split('/')[2]
        if (s === 'users') pageTitle = 'Users'
        else if (s === 'departments') pageTitle = 'Departments'
        else if (s === 'leave-types' || s === 'leave') pageTitle = 'Leave Types'
        else if (s === 'projects') pageTitle = 'Projects'
        else if (s === 'project-activities') pageTitle = 'Activities'
        else if (s === 'components') pageTitle = 'Components'
        else if (s === 'project-types') pageTitle = 'Project Types'
        else if (s === 'organization') pageTitle = 'Organization'
        else if (s === 'reminders-notifications') pageTitle = 'Notification Settings'
        else if (s === 'maintenance') pageTitle = 'Data Maintenance'
        else if (s === 'system-log') pageTitle = 'System Log'
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

            {/* Personal controls — grouped as one cluster and set off from the page
                title (which reflects navigation) with a divider and a pill container. */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Divider orientation="vertical" flexItem sx={{ my: 1 }} />
                <Box
                    sx={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 1,
                        px: 1,
                        py: 0.5,
                        borderRadius: 999,
                        bgcolor: 'action.hover',
                    }}
                >
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
                {!isLoading && shouldUseEmployeeNotifications && employeeMerged.length === 0 && (
                    <MenuItem disabled><ListItemText primary="No notifications yet" /></MenuItem>
                )}
                {!isLoading && shouldUseSystemNotifications && systemNotifications.length === 0 && (
                    <MenuItem disabled><ListItemText primary="No system errors" secondary="Errors the system hits are listed here and emailed to you" /></MenuItem>
                )}
                {!isLoading && shouldUseSystemNotifications && systemNotifications.map((error) => {
                    const isUnread = isSystemErrorUnread(error.id, error.lastOccurredAtUtc)
                    const isRecent = tsTime(error.lastOccurredAtUtc) >= recentThreshold
                    return (
                        <MenuItem key={`err-${error.id}`} onClick={() => handleSystemErrorClick(error.id, error.lastOccurredAtUtc)}>
                            <ListItemIcon>
                                <CircleRoundedIcon sx={{ fontSize: 10, color: isUnread && isRecent ? 'error.main' : 'divider' }} />
                            </ListItemIcon>
                            <ListItemText
                                primary={`System error in ${error.source}${error.occurrences > 1 ? ` (×${error.occurrences})` : ''}`}
                                secondary={`${shortExceptionType(error.exceptionType)} · ${formatChangedAt(error.lastOccurredAtUtc)}`}
                                slotProps={{ primary: { sx: { whiteSpace: 'normal', wordBreak: 'break-word' } } }}
                            />
                        </MenuItem>
                    )
                })}
                {!isLoading && shouldUseManagerNotifications && managerNotifications.map((item) => (
                    <MenuItem key={item.id} onClick={() => handleManagerClick(item.id)}>
                        <ListItemIcon>
                            <CircleRoundedIcon sx={{ fontSize: 10, color: 'error.main' }} />
                        </ListItemIcon>
                        <ListItemText
                            primary={isHrAdminUser
                                ? `Leave request from ${item.employeeName} awaiting HR approval`
                                : `New leave request from ${item.employeeName}`}
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
                {!isLoading && shouldUseEmployeeNotifications && employeeMerged.map((entry) => {
                    const isRecent = entry.ts >= recentThreshold
                    if (entry.kind === 'leave') {
                        const item = entry.item
                        const isUnread = !readEmployeeSet.has(item.id)
                        return (
                            <MenuItem key={`leave-${item.id}`} onClick={() => handleEmployeeClick(item.id, item.annualLeaveId)}>
                                <ListItemIcon>
                                    <CircleRoundedIcon sx={{ fontSize: 10, color: isUnread && isRecent ? 'error.main' : 'divider' }} />
                                </ListItemIcon>
                                <ListItemText
                                    primary={`Leave ${statusPhrase(item.newStatus)}`}
                                    secondary={`Updated ${formatChangedAt(item.changedAt)}`}
                                />
                            </MenuItem>
                        )
                    }
                    const item = entry.item
                    const isUnread = !readEmployeeTsSet.has(item.id)
                    return (
                        <MenuItem key={`ts-${item.id}`} onClick={() => handleEmployeeTsClick(item.id)}>
                            <ListItemIcon>
                                <CircleRoundedIcon sx={{ fontSize: 10, color: isUnread && isRecent ? 'error.main' : 'divider' }} />
                            </ListItemIcon>
                            <ListItemText
                                primary={`Timesheet ${item.newStatus.toLowerCase()}${item.comment ? ` — "${item.comment}"` : ''}`}
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
