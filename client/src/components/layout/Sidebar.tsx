import { useEffect, useMemo, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import AccessTimeRoundedIcon from '@mui/icons-material/AccessTimeRounded'
import AccessAlarmRoundedIcon from '@mui/icons-material/AccessAlarmRounded'
import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded'
import ApartmentRoundedIcon from '@mui/icons-material/ApartmentRounded'
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded'
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import GroupRoundedIcon from '@mui/icons-material/GroupRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import LabelRoundedIcon from '@mui/icons-material/LabelRounded'
import EventRoundedIcon from '@mui/icons-material/EventRounded'
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded'
import NoteAddRoundedIcon from '@mui/icons-material/NoteAddRounded'
import PeopleRoundedIcon from '@mui/icons-material/PeopleRounded'
import UploadRoundedIcon from '@mui/icons-material/UploadRounded'
import Alert from '@mui/material/Alert'
import Avatar from '@mui/material/Avatar'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Divider from '@mui/material/Divider'
import Drawer from '@mui/material/Drawer'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { ThemeProvider } from '@mui/material/styles'
import { buildTheme } from '../../lib/theme'
import { AppDialog, AppDialogTitle, AppDialogContent, AppDialogActions, cancelBtnSx, saveBtnSx } from '../ui'
import { getDepartments, updateProfile, uploadProfileImage } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { Department } from '../../lib/types'
import { useStore } from '../../lib/mobx'

function buildProfileSchema(requireDepartment: boolean) {
    return z.object({
        displayName: z.string().trim().min(1, 'Display name is required.'),
        email: z.string().trim().min(1, 'Email is required.').email('Enter a valid email address.'),
        departmentId: requireDepartment
            ? z.number().int().positive('Department is required.')
            : z.number().int().nonnegative(),
    })
}
type ProfileFormValues = z.infer<ReturnType<typeof buildProfileSchema>>

type NavSection = { kind: 'section'; label: string }
type NavItem = {
    kind: 'item'
    label: string
    icon: React.ReactNode
    onClick: () => void
    active: boolean
}
type NavEntry = NavSection | NavItem

const Sidebar = observer(function Sidebar() {
    const { uiStore, authStore } = useStore()
    const queryClient = useQueryClient()
    const location = useLocation()

    // The sidebar can render dark while the body + topbar stay light (System
    // mode). Memoize so the nested theme only rebuilds when sidebarMode flips.
    const sidebarTheme = useMemo(() => buildTheme(uiStore.sidebarMode), [uiStore.sidebarMode])

    const isAdminUser = authStore.user?.roles?.includes('Admin') ?? false
    const isManagerUser = authStore.user?.roles?.includes('Manager') ?? false
    const shouldShowDepartmentField = !isAdminUser

    const displayName = authStore.user?.displayName ?? authStore.user?.userName ?? 'User'
    const initials = displayName
        .split(' ')
        .filter(Boolean)
        .slice(0, 2)
        .map((p) => p[0]?.toUpperCase() ?? '')
        .join('')
    const roleLabel = isAdminUser
        ? 'Administrator'
        : isManagerUser
            ? `Manager · ${authStore.user?.departmentName ?? ''}`
            : `Employee · ${authStore.user?.departmentName ?? ''}`

    // Profile menu
    const [profileAnchorEl, setProfileAnchorEl] = useState<null | HTMLElement>(null)

    // Edit profile dialog
    const [isEditOpen, setIsEditOpen] = useState(false)
    const profileSchema = useMemo(
        () => buildProfileSchema(shouldShowDepartmentField),
        [shouldShowDepartmentField],
    )
    const { control, register, handleSubmit, reset, formState: { errors } } = useForm<ProfileFormValues>({
        resolver: zodResolver(profileSchema),
        defaultValues: { displayName: '', email: '', departmentId: 0 },
    })

    const { data: departments = [], isLoading: isLoadingDepartments, isError: isDepartmentsError } = useQuery({
        queryKey: ['departments'],
        queryFn: getDepartments,
        enabled: authStore.isAuthenticated,
    })
    const activeDepartments = departments.filter((d: Department) => d.isActive)

    const uploadImageMutation = useMutation({
        mutationFn: (file: File) => uploadProfileImage(file),
        onSuccess: (result) => authStore.setUserImageUrl(result.imageUrl),
    })

    const updateProfileMutation = useMutation({
        mutationFn: updateProfile,
        onSuccess: async (result) => {
            authStore.setUserProfile({
                displayName: result.displayName,
                email: result.email,
                departmentId: result.departmentId,
                departmentName: result.departmentName,
            })
            await queryClient.invalidateQueries({ queryKey: ['employeeProfiles'] })
            setIsEditOpen(false)
        },
    })

    const handleEditClick = () => {
        reset({
            displayName: authStore.user?.displayName ?? '',
            email: authStore.user?.email ?? '',
            departmentId: authStore.user?.departmentId ?? 0,
        })
        updateProfileMutation.reset()
        setIsEditOpen(true)
        setProfileAnchorEl(null)
    }

    // Re-sync defaults when the user object updates while the dialog is open.
    useEffect(() => {
        if (!isEditOpen) return
        reset({
            displayName: authStore.user?.displayName ?? '',
            email: authStore.user?.email ?? '',
            departmentId: authStore.user?.departmentId ?? 0,
        })
        // We intentionally re-init only when the dialog opens — reset is stable.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isEditOpen])

    const onEditSubmit = handleSubmit(async (values) => {
        await updateProfileMutation.mutateAsync({
            displayName: values.displayName.trim(),
            email: values.email.trim(),
            departmentId: values.departmentId,
        })
    })

    const handleImageSelected = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0]
        if (!file) return
        await uploadImageMutation.mutateAsync(file)
        e.target.value = ''
    }

    const handleSignOut = async () => {
        setProfileAnchorEl(null)
        await authStore.signOut()
        uiStore.resetAfterSignOut()
    }

    // Role-based nav entries. Active-state is derived from the current URL.
    const path = location.pathname
    const onPage = (...routes: string[]) => routes.some((r) => path === r || path.startsWith(`${r}/`))
    const onAdminSection = (...sections: string[]) =>
        sections.some((s) => path === `/admin/${s}` || path.startsWith(`/admin/${s}/`))

    let navEntries: NavEntry[]
    if (isAdminUser) {
        navEntries = [
            { kind: 'section', label: 'Overview' },
            { kind: 'item', label: 'Dashboard', icon: <DashboardRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToDashboard(), active: onPage('/dashboard') },
            { kind: 'section', label: 'Annual Leave' },
            { kind: 'item', label: 'All Leave', icon: <CalendarMonthRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTeamLeave(), active: onPage('/team-leave') },
            { kind: 'section', label: 'Time' },
            { kind: 'item', label: 'Company Attendance', icon: <ApartmentRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToCompanyAttendance(), active: onPage('/company-attendance') },
            { kind: 'section', label: 'Timesheets' },
            { kind: 'item', label: 'All Timesheets', icon: <AccessTimeRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTeamTimesheets(), active: onPage('/team-timesheets') },
            { kind: 'section', label: 'Administration' },
            { kind: 'item', label: 'Users', icon: <PeopleRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('users'), active: onAdminSection('users') },
            { kind: 'item', label: 'Departments', icon: <ApartmentRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('departments'), active: onAdminSection('departments') },
            { kind: 'item', label: 'Leave Types', icon: <LabelRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('leave-types'), active: onAdminSection('leave-types', 'leave') },
            { kind: 'item', label: 'Projects', icon: <FolderRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('projects'), active: onAdminSection('projects') },
            { kind: 'item', label: 'Leave Settings', icon: <EventRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('settings'), active: onAdminSection('settings') },
        ]
    } else if (isManagerUser) {
        navEntries = [
            { kind: 'section', label: 'Overview' },
            { kind: 'item', label: 'Dashboard', icon: <DashboardRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToDashboard(), active: onPage('/dashboard') },
            { kind: 'section', label: 'Annual Leave' },
            { kind: 'item', label: 'My Leave', icon: <CalendarMonthRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToMyLeave('requests'), active: onPage('/my-leave') },
            { kind: 'item', label: 'Team Leave', icon: <GroupRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTeamLeave(), active: onPage('/team-leave') },
            { kind: 'item', label: 'Apply Leave', icon: <AddCircleOutlineRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToApplyLeave(), active: onPage('/apply-leave') },
            { kind: 'section', label: 'Time' },
            { kind: 'item', label: 'My Attendance', icon: <AccessAlarmRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAttendance(), active: onPage('/attendance') },
            { kind: 'item', label: 'Team Attendance', icon: <VisibilityRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTeamAttendance(), active: onPage('/team-attendance') },
            { kind: 'item', label: 'Apply Timesheet', icon: <NoteAddRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToNewTimesheet(), active: onPage('/new-timesheet') },
            { kind: 'item', label: 'My Timesheets', icon: <AccessTimeRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTimesheets(), active: onPage('/timesheets') },
            { kind: 'item', label: 'Team Timesheets', icon: <GroupRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTeamTimesheets(), active: onPage('/team-timesheets') },
        ]
    } else {
        navEntries = [
            { kind: 'section', label: 'Overview' },
            { kind: 'item', label: 'Dashboard', icon: <DashboardRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToDashboard(), active: onPage('/dashboard') },
            { kind: 'section', label: 'Annual Leave' },
            { kind: 'item', label: 'My Leave', icon: <CalendarMonthRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToMyLeave('requests'), active: onPage('/my-leave') },
            { kind: 'item', label: 'Apply Leave', icon: <AddCircleOutlineRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToApplyLeave(), active: onPage('/apply-leave') },
            { kind: 'section', label: 'Time' },
            { kind: 'item', label: 'My Attendance', icon: <AccessAlarmRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAttendance(), active: onPage('/attendance') },
            { kind: 'item', label: 'Apply Timesheet', icon: <NoteAddRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToNewTimesheet(), active: onPage('/new-timesheet') },
            { kind: 'item', label: 'My Timesheets', icon: <AccessTimeRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTimesheets(), active: onPage('/timesheets') },
        ]
    }

    return (
        <>
            <ThemeProvider theme={sidebarTheme}>
            <Drawer
                variant="permanent"
                sx={{
                    width: 220,
                    flexShrink: 0,
                    [`& .MuiDrawer-paper`]: {
                        width: 220,
                        boxSizing: 'border-box',
                        bgcolor: 'background.paper',
                        borderRight: 1,
                        borderColor: 'divider',
                        display: 'flex',
                        flexDirection: 'column',
                    },
                }}
            >
                {/* Logo */}
                <Box sx={{ px: 2.5, py: 2.5, borderBottom: 1, borderColor: 'divider' }}>
                    <Typography sx={{ fontSize: 16, fontWeight: 600, color: 'text.primary', letterSpacing: '0.02em' }}>
                        WorkTrack
                    </Typography>
                    <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.25 }}>
                        Leave &amp; Timesheet
                    </Typography>
                </Box>

                {/* Nav */}
                <Box sx={{ flex: 1, py: 1.5, overflowY: 'auto' }}>
                    {navEntries.map((entry, i) => {
                        if (entry.kind === 'section') {
                            return (
                                <Typography
                                    key={`section-${i}`}
                                    sx={{
                                        px: 1.75,
                                        pt: i === 0 ? 1 : 2,
                                        pb: 0.5,
                                        fontSize: 10,
                                        fontWeight: 600,
                                        color: 'text.disabled',
                                        textTransform: 'uppercase',
                                        letterSpacing: '0.06em',
                                    }}
                                >
                                    {entry.label}
                                </Typography>
                            )
                        }

                        return (
                            <Box
                                key={entry.label}
                                onClick={entry.onClick}
                                sx={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 1.25,
                                    px: 1.75,
                                    py: 1.1,
                                    cursor: 'pointer',
                                    color: entry.active ? 'text.primary' : 'text.secondary',
                                    bgcolor: entry.active ? 'action.selected' : 'transparent',
                                    borderLeft: '3px solid',
                                    borderLeftColor: entry.active ? 'primary.main' : 'transparent',
                                    fontSize: 13,
                                    transition: 'all 0.15s',
                                    '&:hover': {
                                        bgcolor: 'action.hover',
                                        color: 'text.primary',
                                    },
                                }}
                            >
                                <Box sx={{ width: 18, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'inherit' }}>
                                    {entry.icon}
                                </Box>
                                <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'inherit' }}>
                                    {entry.label}
                                </Typography>
                            </Box>
                        )
                    })}
                </Box>

                {/* Footer */}
                <Box sx={{ px: 1.75, py: 1.75, borderTop: 1, borderColor: 'divider' }}>
                    <Tooltip title="Account options" placement="right">
                        <Stack
                            direction="row"
                            spacing={1.25}
                            alignItems="center"
                            onClick={(e) => setProfileAnchorEl(e.currentTarget)}
                            sx={{
                                cursor: 'pointer',
                                borderRadius: 1,
                                p: 0.75,
                                '&:hover': { bgcolor: 'action.hover' },
                            }}
                        >
                            <Avatar
                                src={authStore.user?.imageUrl || undefined}
                                sx={{ width: 32, height: 32, fontSize: 12, fontWeight: 600, bgcolor: 'primary.main', color: 'primary.contrastText', flexShrink: 0 }}
                            >
                                {initials}
                            </Avatar>
                            <Box sx={{ minWidth: 0 }}>
                                <Typography sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', lineHeight: 1.3, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                    {displayName}
                                </Typography>
                                <Typography sx={{ fontSize: 10, color: 'text.secondary', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                    {roleLabel}
                                </Typography>
                            </Box>
                        </Stack>
                    </Tooltip>

                    <Menu
                        anchorEl={profileAnchorEl}
                        open={Boolean(profileAnchorEl)}
                        onClose={() => setProfileAnchorEl(null)}
                        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
                        transformOrigin={{ vertical: 'bottom', horizontal: 'left' }}
                    >
                        <MenuItem disabled>
                            <ListItemText
                                primary={displayName}
                                secondary={authStore.user?.email}
                                slotProps={{ primary: { fontWeight: 600 } }}
                            />
                        </MenuItem>
                        <Divider />
                        <MenuItem onClick={handleEditClick}>
                            <ListItemIcon><EditRoundedIcon fontSize="small" /></ListItemIcon>
                            Edit profile
                        </MenuItem>
                        <MenuItem onClick={() => void handleSignOut()}>
                            <ListItemIcon><LogoutRoundedIcon fontSize="small" /></ListItemIcon>
                            Sign out
                        </MenuItem>
                    </Menu>
                </Box>
            </Drawer>
            </ThemeProvider>

            {/* Edit profile dialog */}
            <AppDialog open={isEditOpen} onClose={() => setIsEditOpen(false)} maxWidth="xs">
                <AppDialogTitle>Edit profile</AppDialogTitle>
                <AppDialogContent>
                    <Stack spacing={2}>
                        <Stack direction="row" spacing={1.5} alignItems="center">
                            <Avatar src={authStore.user?.imageUrl || undefined} sx={{ width: 44, height: 44 }}>
                                {initials}
                            </Avatar>
                            <Button
                                component="label"
                                variant="outlined"
                                startIcon={uploadImageMutation.isPending ? <CircularProgress size={16} color="inherit" /> : <UploadRoundedIcon />}
                                disabled={uploadImageMutation.isPending}
                                sx={{ textTransform: 'none' }}
                            >
                                {uploadImageMutation.isPending ? 'Uploading...' : 'Upload photo'}
                                <input type="file" accept="image/*" hidden onChange={(e) => void handleImageSelected(e)} />
                            </Button>
                        </Stack>

                        <TextField
                            label="Display name"
                            {...register('displayName')}
                            error={!!errors.displayName}
                            helperText={errors.displayName?.message}
                            required
                            fullWidth
                        />

                        <TextField
                            label="Email"
                            type="email"
                            {...register('email')}
                            error={!!errors.email}
                            helperText={errors.email?.message}
                            required
                            fullWidth
                        />

                        {shouldShowDepartmentField && (
                            <>
                                <Controller
                                    name="departmentId"
                                    control={control}
                                    render={({ field }) => (
                                        <TextField
                                            select
                                            label="Department"
                                            {...field}
                                            value={field.value ? String(field.value) : ''}
                                            onChange={(e) => field.onChange(Number(e.target.value))}
                                            error={!!errors.departmentId}
                                            helperText={errors.departmentId?.message ?? (isLoadingDepartments ? 'Loading departments...' : 'Select your department.')}
                                            disabled={isLoadingDepartments || activeDepartments.length === 0}
                                            required
                                            fullWidth
                                        >
                                            {activeDepartments.map((d: Department) => (
                                                <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>
                                            ))}
                                        </TextField>
                                    )}
                                />
                                {isDepartmentsError && (
                                    <Alert severity="error">Unable to load departments. Please refresh and try again.</Alert>
                                )}
                            </>
                        )}

                        {updateProfileMutation.isError && (
                            <Alert severity="error">
                                {getApiErrorMessage(updateProfileMutation.error, 'Unable to update profile.')}
                            </Alert>
                        )}
                    </Stack>
                </AppDialogContent>
                <AppDialogActions>
                    <Button variant="outlined" onClick={() => setIsEditOpen(false)} disabled={updateProfileMutation.isPending} sx={cancelBtnSx}>
                        Cancel
                    </Button>
                    <Button
                        variant="contained"
                        onClick={() => void onEditSubmit()}
                        sx={saveBtnSx}
                        disabled={updateProfileMutation.isPending || (shouldShowDepartmentField && (isLoadingDepartments || activeDepartments.length === 0))}
                        startIcon={updateProfileMutation.isPending ? <CircularProgress size={16} color="inherit" /> : null}
                    >
                        Save
                    </Button>
                </AppDialogActions>
            </AppDialog>
        </>
    )
})

export default Sidebar
