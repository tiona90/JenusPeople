import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { SweetAlert, AppDialog, AppDialogTitle, AppDialogContent, AppDialogActions, cancelBtnSx, saveBtnSx } from '../ui'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import CircularProgress from '@mui/material/CircularProgress'
import Divider from '@mui/material/Divider'
import FormControlLabel from '@mui/material/FormControlLabel'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import {
    confirmAdminUserEmail,
    createAdminUser,
    deleteAdminUser,
    getAdminUsers,
    getAnnualLeaves,
    getAppSettings,
    getDepartments,
    getEmployeeProfiles,
    getLeaveStatusHistories,
    getLeaveTypes,
    getTimesheetStatusHistories,
    getUserPresence,
    setAdminUserRoles,
    updateAdminUser,
    updateEmployeeProfile,
} from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { annualLeaveAllowance } from '../../lib/leave-allowance'
import { softBg, type SxColor } from '../../lib/theme-tokens'
import type {
    AdminUser, Department, EmployeeProfile, LeaveStatusHistory, PresenceStatus, TimesheetStatusHistory, UserRole,
} from '../../lib/types'

const PROTECTED_ADMIN_EMAIL = 'admin@annualleave.com'
const ALL_ROLES: UserRole[] = ['Admin', 'Manager', 'Employee']

type StatusTab = 'all' | 'active' | 'admins' | 'managers' | 'employees' | 'online'

type Presence = PresenceStatus

interface DerivedUser {
    user: AdminUser
    profile?: EmployeeProfile
    departmentName: string | null
    primaryRole: 'Admin' | 'Manager' | 'Employee'
    presence: Presence
    lastSeenLabel: string
    leaveBalance: number
    leaveTotal: number
    leavePct: number
    isProtected: boolean
}

interface ActivityItem {
    iconEl: string
    color: 'green' | 'amber' | 'blue' | 'red' | 'gray'
    text: string
    age: string
    timestamp: number
}

function initials(name: string) {
    const parts = (name ?? '').trim().split(/\s+/)
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

function avatarBg(seed: string) {
    const palette = ['primary.main', 'success.main', 'warning.main', 'secondary.main', '#EC4899', '#06B6D4', '#84CC16', 'error.main']
    let hash = 0
    for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0
    return palette[Math.abs(hash) % palette.length]
}

function primaryRoleOf(roles: UserRole[]): 'Admin' | 'Manager' | 'Employee' {
    if (roles.includes('Admin')) return 'Admin'
    if (roles.includes('Manager')) return 'Manager'
    return 'Employee'
}

function fmtRelative(ts: number) {
    const diff = Date.now() - ts
    const m = Math.floor(diff / 60_000)
    if (m < 1) return 'Just now'
    if (m < 60) return `${m}m ago`
    const h = Math.floor(m / 60)
    if (h < 24) return `${h}h ago`
    const d = Math.floor(h / 24)
    if (d < 7) return `${d}d ago`
    return new Date(ts).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
}

function fmtJoined(iso?: string | null) {
    if (!iso) return '—'
    return new Date(iso).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })
}

/* ════════════════════════════════════════════════════════════════════════ */

function AdminUsersPanel() {
    const queryClient = useQueryClient()

    const [statusTab, setStatusTab] = useState<StatusTab>('all')
    const [searchText, setSearchText] = useState('')
    const [deptFilter, setDeptFilter] = useState<string>('all')
    const [roleFilter, setRoleFilter] = useState<string>('all')
    const [selected, setSelected] = useState<Set<string>>(new Set())
    const [expanded, setExpanded] = useState<Set<string>>(new Set())
    const [createOpen, setCreateOpen] = useState(false)
    const [editData, setEditData] = useState<{ user: AdminUser; profile?: EmployeeProfile } | null>(null)
    const [apiError, setApiError] = useState('')
    const [inviteNotice, setInviteNotice] = useState<{ severity: 'success' | 'warning'; message: string } | null>(null)

    const { data: users = [], isLoading, isError, error } = useQuery({
        queryKey: ['adminUsers'],
        queryFn: getAdminUsers,
    })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: departments = [] } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: presences = [] } = useQuery({ queryKey: ['attendance', 'presence'], queryFn: getUserPresence })
    const { data: leaveHistories = [] } = useQuery({ queryKey: ['leaveStatusHistories'], queryFn: getLeaveStatusHistories })
    const { data: timesheetHistories = [] } = useQuery({ queryKey: ['timesheetStatusHistories'], queryFn: getTimesheetStatusHistories })
    const { data: leaves = [] } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: appSettings } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })

    /* What a row's leave bar is measured against is the employee's own entitlement, which
       overrides the annual-leave allowance from Leave Types. The dialogs below say so, and
       a new employee starts from that allowance rather than a number hard-coded here.
       See lib/leave-allowance.ts. */
    const annualAllowance = useMemo(
        () => annualLeaveAllowance(leaveTypes, appSettings?.defaultAnnualEntitlement ?? 0),
        [leaveTypes, appSettings?.defaultAnnualEntitlement])

    const profilesByUserId = useMemo(() => new Map(profiles.map((p) => [p.userId, p])), [profiles])
    const deptById = useMemo(() => new Map(departments.map((d) => [d.id, d])), [departments])
    const userByName = useMemo(() => new Map(users.map((u) => [u.displayName, u])), [users])

    // Presence comes from /attendance/presence, keyed by user id: a user is
    // online only while checked in (away while on break), and offline once they
    // check out or if they never checked in. It is deliberately NOT inferred
    // from the company "recent activity" feed — that feed is capped at 20
    // events, keyed by display name, and includes synthetic "Not checked in"
    // rows, so string-matching it reported exactly the wrong users as online.
    const presenceByUserId = useMemo(
        () => new Map(presences.map((p) => [p.userId, p.status])),
        [presences],
    )

    // Last-seen label per user, from that same authoritative feed.
    const lastSeenByUserId = useMemo(() => {
        const map = new Map<string, string>()
        for (const p of presences) {
            if (!p.lastActivityAt) continue
            map.set(p.userId, fmtRelative(new Date(p.lastActivityAt).getTime()))
        }
        return map
    }, [presences])

    /* Derive a unified user list */
    const derivedAll: DerivedUser[] = useMemo(() => {
        const currentYear = new Date().getFullYear()
        return users.map((u) => {
            const profile = profilesByUserId.get(u.id)
            const departmentName = profile?.departmentId ? deptById.get(profile.departmentId)?.name ?? null : null
            const presence = presenceByUserId.get(u.id) ?? 'offline'
            const lastSeenLabel = presence === 'online' ? 'Online now'
                : presence === 'away' ? 'On break'
                : (lastSeenByUserId.get(u.id) ?? 'No activity')
            const used = leaves
                .filter((l) => l.employeeId === u.id && l.status === 'Approved' && new Date(l.startDate).getFullYear() === currentYear)
                .reduce((s, l) => s + l.totalDays, 0)
            const entitled = profile?.annualLeaveEntitlement ?? 0
            const leaveBalance = Math.max(0, entitled - used)
            const leavePct = entitled > 0 ? Math.min(100, (used / entitled) * 100) : 0
            return {
                user: u,
                profile,
                departmentName,
                primaryRole: primaryRoleOf(u.roles),
                presence,
                lastSeenLabel,
                leaveBalance,
                leaveTotal: entitled,
                leavePct,
                isProtected: u.email.trim().toLowerCase() === PROTECTED_ADMIN_EMAIL,
            }
        })
    }, [users, profilesByUserId, deptById, presenceByUserId, lastSeenByUserId, leaves])

    /* Stats */
    const counts = useMemo(() => {
        const c = {
            all: derivedAll.length,
            admins: derivedAll.filter((d) => d.primaryRole === 'Admin').length,
            managers: derivedAll.filter((d) => d.primaryRole === 'Manager').length,
            employees: derivedAll.filter((d) => d.primaryRole === 'Employee').length,
            online: derivedAll.filter((d) => d.presence === 'online').length,
            withProfile: derivedAll.filter((d) => d.profile).length,
        }
        return c
    }, [derivedAll])

    /* Filtering */
    const filtered = useMemo(() => {
        let out = derivedAll
        if (statusTab === 'admins') out = out.filter((d) => d.primaryRole === 'Admin')
        else if (statusTab === 'managers') out = out.filter((d) => d.primaryRole === 'Manager')
        else if (statusTab === 'employees') out = out.filter((d) => d.primaryRole === 'Employee')
        else if (statusTab === 'online') out = out.filter((d) => d.presence === 'online')

        if (roleFilter !== 'all') out = out.filter((d) => d.primaryRole === roleFilter)
        if (deptFilter !== 'all') out = out.filter((d) => d.departmentName === deptFilter)

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((d) =>
                d.user.email.toLowerCase().includes(q) ||
                (d.user.displayName ?? '').toLowerCase().includes(q)
            )
        }
        return out.sort((a, b) => (a.user.displayName ?? a.user.email).localeCompare(b.user.displayName ?? b.user.email))
    }, [derivedAll, statusTab, roleFilter, deptFilter, searchText])

    /* Mutations */
    const createMutation = useMutation({
        mutationFn: createAdminUser,
        onSuccess: (created) => {
            void queryClient.invalidateQueries({ queryKey: ['adminUsers'] })
            void queryClient.invalidateQueries({ queryKey: ['employeeProfiles'] })
            setCreateOpen(false)
            // The account is created without a password, so whether the invite
            // email actually left matters: if it didn't, the new user has no way
            // in until someone tells them to use "Forgot password?".
            setInviteNotice(created.inviteEmailSent === false
                ? {
                    severity: 'warning',
                    message: `${created.email} was created, but the welcome email could not be sent. Ask them to use “Forgot password?” on the sign-in page to set their password.`,
                }
                : {
                    severity: 'success',
                    message: `${created.email} was created. A welcome email with a link to set their password is on its way.`,
                })
        },
        onError: (err) => setApiError(getApiErrorMessage(err, 'Could not create user.')),
    })

    const editMutation = useMutation({
        mutationFn: async (payload: {
            userId: string
            email: string
            displayName: string
            roles: UserRole[]
            profile: EmployeeProfile | undefined
            departmentId: number
            jobTitle: string
            annualLeaveEntitlement: number
            managerId: string | null
            phoneNumber: string | null
            dateOfBirth: string | null
        }) => {
            await updateAdminUser(payload.userId, { email: payload.email, displayName: payload.displayName, phoneNumber: payload.phoneNumber, dateOfBirth: payload.dateOfBirth })
            await setAdminUserRoles(payload.userId, { roles: payload.roles })
            if (payload.profile) {
                await updateEmployeeProfile({
                    id: payload.profile.id,
                    departmentId: payload.departmentId,
                    managerId: payload.managerId,
                    annualLeaveEntitlement: payload.annualLeaveEntitlement,
                    leaveBalance: payload.profile.leaveBalance,
                    jobTitle: payload.jobTitle || null,
                })
            }
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['adminUsers'] })
            void queryClient.invalidateQueries({ queryKey: ['employeeProfiles'] })
            setEditData(null)
        },
        onError: (err) => setApiError(getApiErrorMessage(err, 'Could not update user.')),
    })

    const deleteMutation = useMutation({
        mutationFn: deleteAdminUser,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['adminUsers'] })
            void queryClient.invalidateQueries({ queryKey: ['employeeProfiles'] })
        },
        onError: (err) => setApiError(getApiErrorMessage(err, 'Could not delete user.')),
    })

    const confirmEmailMutation = useMutation({
        mutationFn: confirmAdminUserEmail,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['adminUsers'] })
        },
        onError: (err) => setApiError(getApiErrorMessage(err, 'Could not mark email as verified.')),
    })

    function toggleSelected(id: string) {
        setSelected((prev) => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id); else next.add(id)
            return next
        })
    }
    function toggleExpanded(id: string) {
        setExpanded((prev) => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id); else next.add(id)
            return next
        })
    }
    async function bulkDelete() {
        const ids = Array.from(selected).filter((id) => {
            const u = users.find((u) => u.id === id)
            return u && u.email.trim().toLowerCase() !== PROTECTED_ADMIN_EMAIL
        })
        if (ids.length === 0) return
        const result = await SweetAlert.fire({
            title: `Delete ${ids.length} user${ids.length === 1 ? '' : 's'}?`,
            text: 'This cannot be undone.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, delete',
            cancelButtonText: 'Cancel',
            confirmButtonColor: '#EF4444',
            reverseButtons: true,
        })
        if (!result.isConfirmed) return
        for (const id of ids) await deleteMutation.mutateAsync(id).catch(() => {})
        setSelected(new Set())
    }

    if (isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }
    if (isError) {
        return <Box sx={{ p: 2 }}><Alert severity="error">{getApiErrorMessage(error, 'Failed to load users.')}</Alert></Box>
    }

    const onlinePct = counts.all > 0 ? Math.round((counts.online / counts.all) * 100) : 0
    const managerRatio = counts.managers > 0 ? `1 : ${Math.round(counts.employees / counts.managers)}` : '—'

    return (
        <Box>
            {apiError && (
                <Alert severity="error" onClose={() => setApiError('')} sx={{ mb: 2 }}>{apiError}</Alert>
            )}

            {inviteNotice && (
                <Alert severity={inviteNotice.severity} onClose={() => setInviteNotice(null)} sx={{ mb: 2 }}>
                    {inviteNotice.message}
                </Alert>
            )}

            {/* Stats row */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <Box sx={statCardSx}>
                    <Box sx={statLabelSx}>👥 Total Users</Box>
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>{counts.all}</Box>
                    <Box sx={{ display: 'flex', gap: '12px', mt: '8px', fontSize: 11, color: 'text.secondary', flexWrap: 'wrap' }}>
                        <RoleDot color="#FEE2E2" label={`${counts.admins} admin${counts.admins === 1 ? '' : 's'}`} />
                        <RoleDot color="#FEF3C7" label={`${counts.managers} manager${counts.managers === 1 ? '' : 's'}`} />
                        <RoleDot color="#DBEAFE" label={`${counts.employees} employee${counts.employees === 1 ? '' : 's'}`} />
                    </Box>
                </Box>
                <Box sx={statCardSx}>
                    <Box sx={statLabelSx}>🟢 Online Now</Box>
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: 'success.main', lineHeight: 1 }}>{counts.online}</Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>of {counts.all} users · {onlinePct}%</Box>
                </Box>
                <Box sx={statCardSx}>
                    <Box sx={statLabelSx}>📋 With Profile</Box>
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: 'primary.main', lineHeight: 1 }}>{counts.withProfile}</Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>
                        of {counts.all} users have an employee profile
                    </Box>
                </Box>
                <Box sx={statCardSx}>
                    <Box sx={statLabelSx}>📊 Manager Ratio</Box>
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: 'primary.main', lineHeight: 1 }}>{managerRatio}</Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>
                        {counts.managers} manager{counts.managers === 1 ? '' : 's'} for {counts.employees} employee{counts.employees === 1 ? '' : 's'}
                    </Box>
                </Box>
            </Box>

            {/* Toolbar */}
            <Box sx={{
                bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                p: '10px 12px', display: 'flex', gap: '10px', flexWrap: 'wrap',
                alignItems: 'center', mb: '14px',
            }}>
                <Box sx={{ flex: 1, minWidth: 220 }}>
                    <Box
                        component="input"
                        type="search"
                        placeholder="Search by name or email…"
                        value={searchText}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchText(e.target.value)}
                        sx={{
                            width: '100%', p: '7px 10px', fontSize: 13, fontFamily: 'inherit',
                            border: '1px solid', borderColor: 'divider', borderRadius: '6px', outline: 'none',
                            bgcolor: 'background.paper', color: 'text.primary',
                            '&::placeholder': { color: 'text.disabled' },
                            '&:focus': { borderColor: 'primary.main' },
                        }}
                    />
                </Box>
                <SelectFilter value={roleFilter} onChange={setRoleFilter} options={[
                    { value: 'all', label: 'All roles' },
                    { value: 'Admin', label: `👑 Admin (${counts.admins})` },
                    { value: 'Manager', label: `👥 Manager (${counts.managers})` },
                    { value: 'Employee', label: `👤 Employee (${counts.employees})` },
                ]} />
                <SelectFilter value={deptFilter} onChange={setDeptFilter} options={[
                    { value: 'all', label: 'All departments' },
                    ...departments.map((d) => ({ value: d.name, label: d.name })),
                ]} />
                <Box
                    component="button"
                    onClick={() => setCreateOpen(true)}
                    sx={{
                        bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '14px', py: '7px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', whiteSpace: 'nowrap',
                        '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    + Add user
                </Box>
            </Box>

            {/* Bulk action bar */}
            {selected.size > 0 && (
                <Box sx={{
                    position: 'sticky', top: 0, zIndex: 5,
                    bgcolor: 'background.paper', color: '#fff', borderRadius: '10px',
                    p: '10px 14px', display: 'flex', alignItems: 'center', gap: '14px',
                    mb: '14px', flexWrap: 'wrap',
                    boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
                }}>
                    <Box sx={{ fontSize: 13 }}>
                        <Box component="strong">{selected.size}</Box> user{selected.size === 1 ? '' : 's'} selected
                    </Box>
                    <Box sx={{ ml: 'auto', display: 'flex', gap: '8px' }}>
                        <Box
                            component="button"
                            onClick={() => setSelected(new Set())}
                            disabled={deleteMutation.isPending}
                            sx={{
                                bgcolor: 'transparent', color: '#fff', border: '1px solid #fff',
                                px: '12px', py: '5px', borderRadius: '6px', fontSize: 12, fontWeight: 500,
                                cursor: 'pointer', fontFamily: 'inherit',
                                '&:hover:not(:disabled)': { bgcolor: 'rgba(255,255,255,0.1)' },
                                '&:disabled': { opacity: 0.5 },
                            }}
                        >Clear</Box>
                        <Box
                            component="button"
                            onClick={() => void bulkDelete()}
                            disabled={deleteMutation.isPending}
                            sx={{
                                bgcolor: 'error.main', color: '#fff', border: 'none',
                                px: '14px', py: '6px', borderRadius: '6px', fontSize: 12, fontWeight: 600,
                                cursor: 'pointer', fontFamily: 'inherit',
                                '&:hover:not(:disabled)': { bgcolor: 'error.dark' },
                                '&:disabled': { opacity: 0.5 },
                            }}
                        >🚫 Delete</Box>
                    </Box>
                </Box>
            )}

            {/* Status tabs */}
            <Box sx={{ display: 'flex', gap: '2px', mb: '14px', borderBottom: '1px solid', borderColor: 'divider', px: '2px', flexWrap: 'wrap' }}>
                {([
                    { value: 'all',       label: 'All',       count: counts.all },
                    { value: 'admins',    label: 'Admins',    count: counts.admins },
                    { value: 'managers',  label: 'Managers',  count: counts.managers },
                    { value: 'employees', label: 'Employees', count: counts.employees },
                    { value: 'online',    label: '🟢 Online',  count: counts.online },
                ] as { value: StatusTab; label: string; count: number }[]).map((tab) => {
                    const active = statusTab === tab.value
                    return (
                        <Box
                            key={tab.value}
                            component="button"
                            onClick={() => setStatusTab(tab.value)}
                            sx={{
                                p: '9px 16px', fontSize: 13,
                                color: active ? 'primary.main' : 'text.secondary',
                                cursor: 'pointer',
                                borderBottom: active ? `2px solid ${'primary.main'}` : '2px solid transparent',
                                mb: '-1px', display: 'flex', alignItems: 'center', gap: '6px',
                                background: 'none', border: 'none', fontFamily: 'inherit',
                                fontWeight: active ? 600 : 500,
                                '&:hover': { color: active ? 'primary.main' : 'text.primary' },
                            }}
                        >
                            {tab.label}
                            <Box component="span" sx={{
                                bgcolor: active ? softBg('primary') : 'action.hover',
                                color: active ? 'primary.main' : 'text.secondary',
                                fontSize: 10, fontWeight: 600,
                                px: '7px', borderRadius: '10px',
                            }}>{tab.count}</Box>
                        </Box>
                    )
                })}
            </Box>

            {/* User rows */}
            {filtered.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No users match the current filters.
                </Box>
            ) : (
                filtered.map((d) => (
                    <UserRow
                        key={d.user.id}
                        derived={d}
                        isSelected={selected.has(d.user.id)}
                        isExpanded={expanded.has(d.user.id)}
                        leaveHistories={leaveHistories}
                        timesheetHistories={timesheetHistories}
                        usersByName={userByName}
                        onToggleSelect={() => toggleSelected(d.user.id)}
                        onToggleExpand={() => toggleExpanded(d.user.id)}
                        onEdit={() => setEditData({ user: d.user, profile: d.profile })}
                        onConfirmEmail={() => confirmEmailMutation.mutate(d.user.id)}
                        confirmingEmail={confirmEmailMutation.isPending}
                        onDelete={async () => {
                            const result = await SweetAlert.fire({
                                title: `Delete ${d.user.displayName || d.user.email}?`,
                                text: 'This cannot be undone.',
                                icon: 'warning',
                                showCancelButton: true,
                                confirmButtonText: 'Yes, delete',
                                cancelButtonText: 'Cancel',
                                confirmButtonColor: '#EF4444',
                                reverseButtons: true,
                            })
                            if (result.isConfirmed) deleteMutation.mutate(d.user.id)
                        }}
                        disabled={deleteMutation.isPending}
                    />
                ))
            )}

            {/* Dialogs */}
            <CreateUserDialog
                open={createOpen}
                isPending={createMutation.isPending}
                error={createMutation.error}
                onClose={() => setCreateOpen(false)}
                onSubmit={(payload) => createMutation.mutate(payload)}
                departments={departments}
                profiles={profiles}
                users={users}
                annualAllowance={annualAllowance}
            />
            <EditUserDialog
                data={editData}
                departments={departments}
                profiles={profiles}
                users={users}
                annualAllowance={annualAllowance}
                isPending={editMutation.isPending}
                error={editMutation.error}
                onClose={() => setEditData(null)}
                onSubmit={(payload) => editMutation.mutate(payload)}
            />
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* User row                                                                  */
/* ════════════════════════════════════════════════════════════════════════ */

function UserRow({
    derived, isSelected, isExpanded, leaveHistories, timesheetHistories, usersByName,
    onToggleSelect, onToggleExpand, onEdit, onConfirmEmail, confirmingEmail, onDelete, disabled,
}: {
    derived: DerivedUser
    isSelected: boolean
    isExpanded: boolean
    leaveHistories: LeaveStatusHistory[]
    timesheetHistories: TimesheetStatusHistory[]
    usersByName: Map<string, AdminUser>
    onToggleSelect: () => void
    onToggleExpand: () => void
    onEdit: () => void
    onConfirmEmail: () => void
    confirmingEmail: boolean
    onDelete: () => void
    disabled: boolean
}) {
    const u = derived.user
    const role = derived.primaryRole
    const presence = derived.presence

    const activity = useMemo<ActivityItem[]>(() => {
        const items: ActivityItem[] = []
        for (const h of leaveHistories) {
            if (h.employeeId !== u.id && h.changedByUserId !== u.id) continue
            const isOwn = h.employeeId === u.id
            const ts = new Date(h.changedAt).getTime()
            const action = isOwn
                ? `${h.newStatus === 'Pending' ? 'Submitted leave request' : `Leave ${h.newStatus.toLowerCase()}`}`
                : `${h.newStatus === 'Approved' ? 'Approved' : h.newStatus === 'Rejected' ? 'Rejected' : 'Changed'} ${h.employeeName}'s leave`
            const color = h.newStatus === 'Approved' ? 'green'
                : h.newStatus === 'Rejected' ? 'red'
                : h.newStatus === 'Pending' ? 'amber' : 'blue'
            items.push({
                iconEl: '🌴',
                color,
                text: action,
                age: fmtRelative(ts),
                timestamp: ts,
            })
        }
        for (const h of timesheetHistories) {
            if (h.employeeId !== u.id && h.changedByUserId !== u.id) continue
            const isOwn = h.employeeId === u.id
            const ts = new Date(h.changedAt).getTime()
            const action = isOwn
                ? `Timesheet ${h.newStatus.toLowerCase()}`
                : `${h.newStatus === 'Approved' ? 'Approved' : h.newStatus === 'Rejected' ? 'Rejected' : 'Changed'} ${h.employeeName}'s timesheet`
            const color = h.newStatus === 'Approved' ? 'green'
                : h.newStatus === 'Rejected' ? 'red' : 'blue'
            items.push({
                iconEl: '📋',
                color,
                text: action,
                age: fmtRelative(ts),
                timestamp: ts,
            })
        }
        items.sort((a, b) => b.timestamp - a.timestamp)
        return items.slice(0, 5)
    }, [leaveHistories, timesheetHistories, u.id])

    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const managerName = useMemo(() => {
        if (!derived.profile?.managerId) return null
        const managerProfile = profiles.find((p) => p.id === derived.profile!.managerId)
        if (!managerProfile) return null
        const managerUser = usersByName.size > 0
            ? Array.from(usersByName.values()).find((u) => u.id === managerProfile.userId)
            : undefined
        return managerUser?.displayName || managerUser?.email || null
    }, [derived.profile, profiles, usersByName])

    const roleStyle = roleStyles[role]
    const accentColor = role === 'Admin' ? 'secondary.main'
        : role === 'Manager' ? 'warning.main' : 'primary.main'

    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
            borderLeft: `3px solid ${accentColor}`,
            borderRadius: '10px', mb: '8px',
            ...(isSelected && { boxShadow: (theme) => `inset 0 0 0 2px ${theme.palette.primary.main}`, bgcolor: softBg('primary') }),
        }}>
            <Box
                onClick={onToggleExpand}
                sx={{
                    display: 'grid',
                    gridTemplateColumns: {
                        xs: '24px 1fr auto',
                        md: '24px minmax(0, 240px) minmax(0, 110px) minmax(0, 140px) minmax(0, 130px) minmax(0, 150px) minmax(80px, 1fr) auto',
                    },
                    gap: '12px', alignItems: 'center',
                    p: '14px 16px', cursor: 'pointer',
                    '&:hover': { bgcolor: isSelected ? softBg('primary') : 'action.hover' },
                }}
            >
                <Box
                    component="input"
                    type="checkbox"
                    checked={isSelected}
                    disabled={derived.isProtected}
                    onChange={onToggleSelect}
                    onClick={(e: React.MouseEvent) => e.stopPropagation()}
                    sx={{
                        cursor: derived.isProtected ? 'not-allowed' : 'pointer',
                        width: 16, height: 16,
                        accentColor: 'primary.main',
                        opacity: derived.isProtected ? 0.3 : 1,
                    }}
                />

                {/* User */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0 }}>
                    <Box sx={{ position: 'relative' }}>
                        <Box sx={{
                            width: 36, height: 36, borderRadius: '50%',
                            bgcolor: avatarBg(u.displayName || u.email), color: '#fff',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            fontSize: 12, fontWeight: 600, flexShrink: 0,
                        }}>{initials(u.displayName || u.email)}</Box>
                        <Box sx={{
                            position: 'absolute', bottom: 0, right: 0,
                            width: 10, height: 10, borderRadius: '50%',
                            border: '2px solid #fff',
                            bgcolor: presence === 'online' ? 'success.main'
                                : presence === 'away' ? 'warning.main' : 'text.disabled',
                        }} />
                    </Box>
                    <Box sx={{ minWidth: 0 }}>
                        <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {u.displayName || u.email}
                            {derived.isProtected && (
                                <Box component="span" sx={{ ml: '6px', fontSize: 10, color: 'text.disabled', fontStyle: 'italic' }}>· protected</Box>
                            )}
                        </Box>
                        <Box sx={{ fontSize: 11, color: 'text.secondary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {u.email}
                        </Box>
                    </Box>
                </Box>

                {/* Role pill */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    <Box component="span" sx={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        bgcolor: roleStyle.bg, color: roleStyle.fg,
                        fontSize: 11, fontWeight: 500, px: '8px', py: '3px',
                        borderRadius: '12px',
                    }}>
                        {role === 'Admin' ? '👑' : role === 'Manager' ? '👥' : '👤'} {role}
                    </Box>
                </Box>

                {/* Department */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    {derived.departmentName ? (
                        <Box component="span" sx={{
                            display: 'inline-block', bgcolor: softBg('info'), color: 'info.dark',
                            borderRadius: '4px', px: '8px', py: '2px',
                            fontSize: 11, fontWeight: 500,
                        }}>{derived.departmentName}</Box>
                    ) : <Box sx={{ fontSize: 11, color: 'text.disabled' }}>—</Box>}
                </Box>

                {/* Status */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    <Box component="span" sx={{
                        display: 'inline-flex', alignItems: 'center', gap: '5px',
                        fontSize: 11, fontWeight: 500,
                        color: presence === 'online' ? 'success.dark'
                            : presence === 'away' ? 'warning.dark' : 'text.secondary',
                        bgcolor: presence === 'online' ? softBg('success')
                            : presence === 'away' ? softBg('warning') : 'action.hover',
                        px: '8px', py: '3px', borderRadius: '12px',
                    }}>
                        {presence === 'online' ? 'Online' : presence === 'away' ? 'Away' : 'Offline'}
                    </Box>
                </Box>

                {/* Leave */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    {derived.leaveTotal > 0 ? (
                        <>
                            <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                                <Box component="strong" sx={{
                                    color: derived.leavePct >= 80 ? 'error.main' : derived.leavePct >= 60 ? 'warning.main' : 'text.primary',
                                    fontSize: 13, fontWeight: 700,
                                }}>{derived.leaveBalance}</Box> / {derived.leaveTotal} days
                            </Box>
                            <Box sx={{ height: 4, bgcolor: 'action.hover', borderRadius: '2px', overflow: 'hidden', mt: '4px' }}>
                                <Box sx={{
                                    height: '100%',
                                    bgcolor: derived.leavePct >= 80 ? 'error.main' : derived.leavePct >= 60 ? 'warning.main' : 'success.main',
                                    width: `${100 - derived.leavePct}%`,
                                }} />
                            </Box>
                        </>
                    ) : (
                        <Box sx={{ fontSize: 11, color: 'text.disabled' }}>—</Box>
                    )}
                </Box>

                {/* Last active */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    <Box sx={{ fontSize: 12, fontWeight: 600, color: presence === 'online' ? 'success.main' : 'text.primary' }}>
                        {derived.lastSeenLabel}
                    </Box>
                    {derived.lastSeenLabel !== 'No activity' && (
                        <Box sx={{ fontSize: 10, color: 'text.secondary', mt: '2px' }}>
                            {presence === 'online' ? 'right now' : 'last active'}
                        </Box>
                    )}
                </Box>

                {/* Actions */}
                <Box
                    onClick={(e: React.MouseEvent) => e.stopPropagation()}
                    sx={{ display: 'flex', gap: '6px', justifyContent: 'flex-end', flexShrink: 0 }}
                >
                    {!derived.isProtected && (
                        <>
                            <IconBtn title="Edit" onClick={onEdit}>✏️</IconBtn>
                            <IconBtn title="Delete" onClick={onDelete} disabled={disabled} danger>🗑</IconBtn>
                        </>
                    )}
                </Box>
            </Box>

            {isExpanded && (
                <Box sx={{
                    px: '16px', py: '14px', borderTop: '1px solid #F3F4F6',
                    bgcolor: 'action.hover',
                    display: 'grid',
                    gridTemplateColumns: { xs: '1fr', md: '1fr 1fr 1fr' },
                    gap: '14px',
                }}>
                    <ExpandBlock title="Account details">
                        <ExpandRow label="Joined" value={fmtJoined(derived.profile?.createdAt)} />
                        <ExpandRow label="Phone" value={u.phoneNumber || '—'} />
                        <ExpandRow label="Date of birth" value={u.dateOfBirth ? new Date(u.dateOfBirth).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) : '—'} />
                        <ExpandRow label="Department" value={derived.departmentName ?? '—'} />
                        <ExpandRow label="Job title" value={derived.profile?.jobTitle || '—'} />
                        {role === 'Employee' && (
                            <ExpandRow label="Manager" value={managerName ?? '—'} />
                        )}
                        <ExpandRow label="Annual entitlement"
                                   value={derived.leaveTotal > 0 ? `${derived.leaveTotal} days` : '—'} />
                        <ExpandRow label="Balance"
                                   value={derived.leaveTotal > 0 ? `${derived.leaveBalance} days` : '—'} />
                    </ExpandBlock>

                    <ExpandBlock title="Recent activity">
                        {activity.length === 0 ? (
                            <Box sx={{ fontSize: 11, color: 'text.disabled', fontStyle: 'italic' }}>No recent activity</Box>
                        ) : (
                            <Box sx={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                {activity.map((a, i) => (
                                    <Box key={i} sx={{ display: 'grid', gridTemplateColumns: '22px 1fr auto', gap: '8px', alignItems: 'center' }}>
                                        <Box sx={{
                                            width: 22, height: 22, borderRadius: '50%',
                                            bgcolor: activityIconBg[a.color],
                                            color: activityIconFg[a.color],
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            fontSize: 11,
                                        }}>{a.iconEl}</Box>
                                        <Box sx={{ fontSize: 11, color: 'text.primary', minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {a.text}
                                        </Box>
                                        <Box sx={{ fontSize: 10, color: 'text.disabled', whiteSpace: 'nowrap' }}>{a.age}</Box>
                                    </Box>
                                ))}
                            </Box>
                        )}
                    </ExpandBlock>

                    <ExpandBlock title={role === 'Manager' || role === 'Admin' ? 'Reach' : 'Quick info'}>
                        {role === 'Manager' || role === 'Admin' ? (
                            <DirectReports user={derived.user} role={role} />
                        ) : (
                            <>
                                <ExpandRow
                                    label="Email verified"
                                    value={u.emailConfirmed
                                        ? <Box component="span" sx={{ color: 'success.main' }}>✓ Yes</Box>
                                        : (
                                            <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                                                <Box component="span" sx={{ color: 'warning.main' }}>⚠ No</Box>
                                                <Box
                                                    component="button"
                                                    onClick={onConfirmEmail}
                                                    disabled={confirmingEmail}
                                                    sx={{
                                                        bgcolor: 'transparent', border: '1px solid', borderColor: 'primary.main',
                                                        color: 'primary.main', borderRadius: '4px',
                                                        px: '8px', py: '2px', fontSize: 10, fontWeight: 600,
                                                        cursor: confirmingEmail ? 'not-allowed' : 'pointer', fontFamily: 'inherit',
                                                        '&:hover:not(:disabled)': { bgcolor: softBg('primary') },
                                                        '&:disabled': { opacity: 0.5 },
                                                    }}
                                                >
                                                    {confirmingEmail ? 'Verifying…' : 'Mark verified'}
                                                </Box>
                                            </Box>
                                        )}
                                />
                                <ExpandRow label="Roles" value={u.roles.join(', ')} />
                                <ExpandRow label="User ID" value={<Box component="code" sx={{ fontSize: 10 }}>{u.id.slice(0, 8)}…</Box>} />
                            </>
                        )}
                    </ExpandBlock>
                </Box>
            )}
        </Box>
    )
}

function DirectReports({ user, role }: { user: AdminUser; role: 'Admin' | 'Manager' }) {
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: users = [] } = useQuery({ queryKey: ['adminUsers'], queryFn: getAdminUsers })

    const myProfile = profiles.find((p) => p.userId === user.id)
    if (!myProfile && role !== 'Admin') {
        return <Box sx={{ fontSize: 11, color: 'text.disabled', fontStyle: 'italic' }}>No profile linked</Box>
    }

    const reports = role === 'Admin'
        ? users.filter((u) => u.id !== user.id)
        : profiles
            .filter((p) => p.managerId && myProfile && p.managerId === myProfile.id)
            .map((p) => users.find((u) => u.id === p.userId))
            .filter((u): u is AdminUser => !!u)

    if (reports.length === 0) {
        return <Box sx={{ fontSize: 11, color: 'text.disabled', fontStyle: 'italic' }}>No direct reports</Box>
    }

    const visible = reports.slice(0, 4)
    const remaining = reports.length - visible.length

    return (
        <>
            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '8px' }}>
                {reports.length} {role === 'Admin' ? 'people in scope' : `report${reports.length === 1 ? '' : 's'}`}
            </Box>
            <Box sx={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                {visible.map((r) => (
                    <Box key={r.id} title={r.displayName || r.email} sx={{
                        width: 28, height: 28, borderRadius: '50%',
                        bgcolor: avatarBg(r.displayName || r.email), color: '#fff',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 10, fontWeight: 600,
                        border: '2px solid #fff', marginLeft: '-2px',
                    }}>{initials(r.displayName || r.email)}</Box>
                ))}
                {remaining > 0 && (
                    <Box sx={{
                        width: 28, height: 28, borderRadius: '50%',
                        bgcolor: 'divider', color: 'text.secondary',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 10, fontWeight: 600, border: '2px solid #fff', marginLeft: '-2px',
                    }}>+{remaining}</Box>
                )}
            </Box>
        </>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Small UI bits                                                            */
/* ════════════════════════════════════════════════════════════════════════ */

const statCardSx = {
    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', p: '14px 16px',
} as const

const statLabelSx = {
    fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em',
    mb: '6px', display: 'flex', alignItems: 'center', gap: '6px',
} as const

function RoleDot({ color, label }: { color: string; label: string }) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: color }} />
            {label}
        </Box>
    )
}

function SelectFilter({ value, onChange, options }: {
    value: string
    onChange: (v: string) => void
    options: { value: string; label: string }[]
}) {
    return (
        <Box
            component="select"
            value={value}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => onChange(e.target.value)}
            sx={{
                fontSize: 12, fontFamily: 'inherit', p: '7px 10px',
                border: '1px solid', borderColor: 'divider', borderRadius: '6px',
                color: 'text.primary', bgcolor: 'background.paper', outline: 'none', cursor: 'pointer',
                '&:focus': { borderColor: 'primary.main' },
            }}
        >
            {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </Box>
    )
}

function ExpandBlock({ title, children }: { title: string; children: React.ReactNode }) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '8px', p: '12px 14px' }}>
            <Box sx={{ fontSize: 11, fontWeight: 600, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '8px' }}>
                {title}
            </Box>
            {children}
        </Box>
    )
}

function ExpandRow({ label, value }: { label: string; value: React.ReactNode }) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: '8px', py: '4px', fontSize: 11 }}>
            <Box sx={{ color: 'text.secondary' }}>{label}</Box>
            <Box sx={{ color: 'text.primary', fontWeight: 500, textAlign: 'right' }}>{value}</Box>
        </Box>
    )
}

function IconBtn({ title, onClick, disabled, danger, children }: {
    title: string
    onClick: () => void
    disabled?: boolean
    danger?: boolean
    children: React.ReactNode
}) {
    return (
        <Box
            component="button"
            title={title}
            onClick={onClick}
            disabled={disabled}
            sx={{
                width: 30, height: 30, borderRadius: '6px',
                bgcolor: 'transparent', border: '1px solid', borderColor: 'divider',
                color: danger ? 'error.dark' : 'text.secondary',
                cursor: 'pointer', fontFamily: 'inherit',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 13,
                '&:hover:not(:disabled)': {
                    bgcolor: danger ? softBg('error') : 'action.hover',
                    borderColor: danger ? 'error.main' : 'divider',
                },
                '&:disabled': { opacity: 0.4, cursor: 'not-allowed' },
            }}
        >
            {children}
        </Box>
    )
}

const roleStyles: Record<'Admin' | 'Manager' | 'Employee', { bg: SxColor; fg: string }> = {
    Admin:    { bg: softBg('secondary'), fg: 'secondary.dark' },
    Manager:  { bg: softBg('warning'), fg: 'warning.dark' },
    Employee: { bg: softBg('info'), fg: 'info.dark' },
}

const activityIconBg: Record<ActivityItem['color'], SxColor> = {
    green: softBg('success'), amber: softBg('warning'), blue: softBg('info'), red: softBg('error'), gray: 'action.hover',
}
const activityIconFg: Record<ActivityItem['color'], string> = {
    green: 'success.dark', amber: 'warning.dark', blue: 'info.dark', red: 'error.dark', gray: 'text.secondary',
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Dialogs                                                                  */
/* ════════════════════════════════════════════════════════════════════════ */

// A user's manager is whoever manages their department — not a free pick.
// Mirrors DepartmentsPanel's "Department manager" derivation (the department's
// Manager-role profile) so the two views agree. Shared by Create and Edit so
// both dialogs land on the same person. excludeUserId keeps a department
// manager from being shown as their own manager while editing themselves.
function deriveDepartmentManager(
    departmentId: number,
    profiles: EmployeeProfile[],
    users: AdminUser[],
    excludeUserId?: string,
): { profileId: string; name: string } | null {
    if (!departmentId) return null
    const candidate = profiles.find((p) => {
        if (p.departmentId !== departmentId) return false
        if (p.userId === excludeUserId) return false
        const u = users.find((u) => u.id === p.userId)
        return !!u?.roles.includes('Manager')
    })
    if (!candidate) return null
    const u = users.find((u) => u.id === candidate.userId)
    return { profileId: candidate.id, name: u?.displayName || u?.email || 'Unknown' }
}

function EditUserDialog(props: {
    data: { user: AdminUser; profile?: EmployeeProfile } | null
    departments: Department[]
    profiles: EmployeeProfile[]
    users: AdminUser[]
    annualAllowance: number
    onClose: () => void
    isPending: boolean
    error: unknown
    onSubmit: (payload: {
        userId: string
        email: string
        displayName: string
        roles: UserRole[]
        profile: EmployeeProfile | undefined
        departmentId: number
        jobTitle: string
        annualLeaveEntitlement: number
        managerId: string | null
        phoneNumber: string | null
        dateOfBirth: string | null
    }) => void
}) {
    const open = !!props.data
    const { user, profile } = props.data ?? {}

    const [email, setEmail] = useState('')
    const [displayName, setDisplayName] = useState('')
    // Exactly one role per user, so a single value rather than a set.
    const [role, setRole] = useState<UserRole>('Employee')
    const [departmentId, setDepartmentId] = useState(0)
    const [jobTitle, setJobTitle] = useState('')
    const [annualLeaveEntitlement, setAnnualLeaveEntitlement] = useState(0)
    const [phoneNumber, setPhoneNumber] = useState('')
    const [dateOfBirth, setDateOfBirth] = useState('')

    useEffect(() => {
        if (props.data) {
            Promise.resolve().then(() => {
                setEmail(props.data!.user.email)
                setDisplayName(props.data!.user.displayName ?? '')
                // Collapses any legacy multi-role account to its highest role.
                setRole(primaryRoleOf(props.data!.user.roles))
                setDepartmentId(props.data!.profile?.departmentId ?? 0)
                setJobTitle(props.data!.profile?.jobTitle ?? '')
                setAnnualLeaveEntitlement(props.data!.profile?.annualLeaveEntitlement ?? 0)
                setPhoneNumber(props.data!.user.phoneNumber ?? '')
                setDateOfBirth(props.data!.user.dateOfBirth ?? '')
            })
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.data])

    // If the person being edited *is* their department's manager, they have no
    // manager of their own here — excludeUserId keeps them from matching themselves.
    const departmentManager = useMemo(
        () => deriveDepartmentManager(departmentId, props.profiles, props.users, props.data?.user.id),
        [departmentId, props.profiles, props.users, props.data],
    )

    return (
        <AppDialog open={open} onClose={props.onClose} maxWidth="sm">
            <AppDialogTitle>Edit User</AppDialogTitle>
            <AppDialogContent>
                <Stack spacing={2}>
                    <TextField label="Email" value={email} onChange={(e) => setEmail(e.target.value)} fullWidth required />
                    <TextField label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} fullWidth />
                    <TextField label="Phone number" type="tel" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} fullWidth />
                    <TextField label="Date of birth" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} fullWidth slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: new Date().toISOString().slice(0, 10) } }} helperText="Used for birthday reminders." />

                    <Divider />
                    <Typography variant="subtitle2" color="text.secondary">Role</Typography>
                    <RadioGroup row name="edit-user-role" value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
                        {ALL_ROLES.map((option) => (
                            <FormControlLabel key={option} value={option} control={<Radio />} label={option} />
                        ))}
                    </RadioGroup>

                    {profile && (
                        <>
                            <Divider />
                            <Typography variant="subtitle2" color="text.secondary">Profile</Typography>
                            <TextField
                                select
                                label="Department"
                                value={departmentId}
                                onChange={(e) => setDepartmentId(Number(e.target.value))}
                                fullWidth
                                required
                                error={departmentId === 0}
                                helperText={departmentId === 0 ? 'Department is required' : ''}
                            >
                                <MenuItem value={0} disabled>Select department</MenuItem>
                                {props.departments.map((dept) => (
                                    <MenuItem key={dept.id} value={dept.id}>{dept.name} ({dept.code})</MenuItem>
                                ))}
                            </TextField>
                            <TextField
                                label="Manager"
                                value={departmentManager?.name ?? 'No manager assigned to this department'}
                                fullWidth
                                disabled
                                helperText="Set by the department's manager — change it by reassigning who manages this department."
                            />
                            <TextField
                                label="Job title"
                                value={jobTitle}
                                onChange={(e) => setJobTitle(e.target.value)}
                                fullWidth
                            />
                            <TextField
                                label="Annual leave entitlement"
                                type="number"
                                value={annualLeaveEntitlement}
                                onChange={(e) => setAnnualLeaveEntitlement(Number(e.target.value))}
                                inputProps={{ min: 0, step: 0.5 }}
                                fullWidth
                                helperText={`This employee's own allowance, overriding the ${props.annualAllowance} days/year Leave Types sets for annual leave.`}
                            />
                        </>
                    )}

                    {props.error ? <Alert severity="error">{getApiErrorMessage(props.error, 'Failed.')}</Alert> : null}
                </Stack>
            </AppDialogContent>
            <AppDialogActions>
                <Button variant="outlined" onClick={props.onClose} disabled={props.isPending} sx={cancelBtnSx}>Cancel</Button>
                <Button
                    variant="contained"
                    disabled={props.isPending || !user}
                    onClick={() =>
                        user && props.onSubmit({ userId: user.id, email, displayName, roles: [role], profile, departmentId, jobTitle, annualLeaveEntitlement, managerId: departmentManager?.profileId ?? null, phoneNumber: phoneNumber.trim() || null, dateOfBirth: dateOfBirth || null })
                    }
                    sx={saveBtnSx}
                >
                    Save
                </Button>
            </AppDialogActions>
        </AppDialog>
    )
}

function CreateUserDialog(props: {
    open: boolean
    annualAllowance: number
    onClose: () => void
    isPending: boolean
    error: unknown
    onSubmit: (payload: {
        email: string
        displayName: string
        roles: UserRole[]
        departmentId: number
        managerId: string | null
        jobTitle: string | null
        annualLeaveEntitlement: number
        phoneNumber: string | null
        dateOfBirth: string | null
    }) => void
    departments: Department[]
    profiles: EmployeeProfile[]
    users: AdminUser[]
}) {
    const [email, setEmail] = useState('')
    const [displayName, setDisplayName] = useState('')
    // Exactly one role per user, so a single value rather than a set.
    const [role, setRole] = useState<UserRole>('Employee')
    const [departmentId, setDepartmentId] = useState<number>(0)
    const [jobTitle, setJobTitle] = useState('')
    const [annualLeaveEntitlement, setAnnualLeaveEntitlement] = useState(props.annualAllowance)
    const [phoneNumber, setPhoneNumber] = useState('')
    const [dateOfBirth, setDateOfBirth] = useState('')

    /* The allowance arrives with the leave-types query, which can settle after this
       dialog has mounted, so the field is seeded when it opens rather than at mount. */
    useEffect(() => {
        if (props.open) {
            Promise.resolve().then(() => setAnnualLeaveEntitlement(props.annualAllowance))
        }
    }, [props.open, props.annualAllowance])

    // Same rule as EditUserDialog: a new hire reports to whoever manages the
    // department they're placed in — not a free pick.
    const departmentManager = useMemo(
        () => deriveDepartmentManager(departmentId, props.profiles, props.users),
        [departmentId, props.profiles, props.users],
    )

    const close = () => {
        setEmail('')
        setDisplayName('')
        setRole('Employee')
        setDepartmentId(0)
        setJobTitle('')
        setAnnualLeaveEntitlement(props.annualAllowance)
        setPhoneNumber('')
        setDateOfBirth('')
        props.onClose()
    }

    return (
        <AppDialog open={props.open} onClose={close} maxWidth="sm">
            <AppDialogTitle>Create User</AppDialogTitle>
            <AppDialogContent>
                <Stack spacing={2}>
                    {/* No password field: the new user picks their own from the
                        emailed link, so an admin never sets or relays one. */}
                    <Alert severity="info" sx={{ fontSize: 12 }}>
                        No password needed — we'll email this person a secure link to set their own.
                    </Alert>
                    <TextField label="Email" value={email} onChange={(e) => setEmail(e.target.value)} fullWidth required />
                    <TextField
                        label="Display name"
                        value={displayName}
                        onChange={(e) => setDisplayName(e.target.value)}
                        fullWidth
                        required
                        error={!displayName.trim()}
                        helperText={!displayName.trim() ? 'Display name is required' : 'Shown throughout the app and used to greet them in emails.'}
                    />
                    <TextField label="Phone number" type="tel" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} fullWidth />
                    <TextField label="Date of birth" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} fullWidth slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: new Date().toISOString().slice(0, 10) } }} helperText="Used for birthday reminders." />

                    <Divider />
                    <Typography variant="subtitle2" color="text.secondary">Role</Typography>
                    <RadioGroup row name="create-user-role" value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
                        {ALL_ROLES.map((option) => (
                            <FormControlLabel key={option} value={option} control={<Radio />} label={option} />
                        ))}
                    </RadioGroup>

                    <Divider />
                    <Typography variant="subtitle2" color="text.secondary">Profile</Typography>
                    <TextField
                        select
                        label="Department"
                        value={departmentId}
                        onChange={(e) => setDepartmentId(Number(e.target.value))}
                        fullWidth
                        required
                        error={departmentId === 0}
                        helperText={departmentId === 0 ? 'Department is required' : ''}
                    >
                        <MenuItem value={0} disabled>Select department</MenuItem>
                        {props.departments.map((dept) => (
                            <MenuItem key={dept.id} value={dept.id}>{dept.name} ({dept.code})</MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        label="Manager"
                        value={departmentManager?.name ?? 'No manager assigned to this department'}
                        fullWidth
                        disabled
                        helperText="Set by the department's manager — change it by reassigning who manages this department."
                    />
                    <TextField
                        label="Job title"
                        value={jobTitle}
                        onChange={(e) => setJobTitle(e.target.value)}
                        fullWidth
                    />
                    <TextField
                        label="Annual leave entitlement"
                        type="number"
                        value={annualLeaveEntitlement}
                        onChange={(e) => setAnnualLeaveEntitlement(Number(e.target.value))}
                        inputProps={{ min: 0, step: 0.5 }}
                        fullWidth
                        helperText={`Starts from the ${props.annualAllowance}-day annual leave allowance on Leave Types; change it to give this employee a different one.`}
                    />

                    {props.error ? <Alert severity="error">{getApiErrorMessage(props.error, 'Failed.')}</Alert> : null}
                </Stack>
            </AppDialogContent>
            <AppDialogActions>
                <Button variant="outlined" onClick={close} disabled={props.isPending} sx={cancelBtnSx}>Cancel</Button>
                <Button
                    variant="contained"
                    disabled={props.isPending || !email.trim() || !displayName.trim() || departmentId === 0}
                    onClick={() => props.onSubmit({
                        email: email.trim(),
                        displayName: displayName.trim(),
                        roles: [role],
                        departmentId,
                        managerId: departmentManager?.profileId ?? null,
                        jobTitle: jobTitle.trim() || null,
                        annualLeaveEntitlement,
                        phoneNumber: phoneNumber.trim() || null,
                        dateOfBirth: dateOfBirth || null,
                    })}
                    sx={saveBtnSx}
                >
                    Create
                </Button>
            </AppDialogActions>
        </AppDialog>
    )
}

export default AdminUsersPanel
