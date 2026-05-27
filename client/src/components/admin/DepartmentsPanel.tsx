import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { SweetAlert, AppDialog, AppDialogTitle, AppDialogContent, AppDialogActions, cancelBtnSx, saveBtnSx } from '../ui'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Switch from '@mui/material/Switch'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import {
    createDepartment,
    deleteDepartment,
    getAdminUsers,
    getAnnualLeaves,
    getCompanyAttendance,
    getDepartments,
    getEmployeeProfiles,
    getTimesheets,
    updateDepartment,
    type UpsertDepartmentRequest,
} from '../../lib/api'
import { useStore } from '../../lib/mobx'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { AdminUser, Department, DepartmentAttendance, EmployeeProfile } from '../../lib/types'
import { softBg } from '../../lib/theme-tokens'


const DEPT_GRADIENTS = [
    'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',  // blue
    'linear-gradient(135deg, #22C47A 0%, #16A05E 100%)',  // green
    'linear-gradient(135deg, #F59E0B 0%, #D97706 100%)',  // amber
    'linear-gradient(135deg, #8B5CF6 0%, #7C3AED 100%)',  // purple
    'linear-gradient(135deg, #EC4899 0%, #DB2777 100%)',  // pink
    'linear-gradient(135deg, #06B6D4 0%, #0891B2 100%)',  // cyan
    'linear-gradient(135deg, #FF4D4F 0%, #DC2626 100%)',  // red
    'linear-gradient(135deg, #84CC16 0%, #65A30D 100%)',  // lime
]

type StatusFilter = 'all' | 'active' | 'attention' | 'inactive'
type ViewMode = 'cards' | 'table'

/* ─── helpers ───────────────────────────────────────────────────────────── */

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

function gradientForDept(id: number) {
    return DEPT_GRADIENTS[id % DEPT_GRADIENTS.length]
}

function fmtJoined(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

interface DerivedDept {
    dept: Department
    headcount: number
    managerName: string | null
    managerInitials: string | null
    managerBg: string
    members: { id: string; name: string }[]
    counts: { in: number; break: number; leave: number; out: number; total: number }
    pendingLeave: number
    pendingTs: number
    leaveUsedYTD: number
    leaveAllowance: number
    needsAttention: boolean
    alert: string | null
    gradient: string
}

function getErrorMessage(error: unknown) {
    return getApiErrorMessage(error, 'Something went wrong. Please try again.')
}

/* ════════════════════════════════════════════════════════════════════════ */

function DepartmentsPanel() {
    const { uiStore } = useStore()
    const queryClient = useQueryClient()

    const [createOpen, setCreateOpen] = useState(false)
    const [editDept, setEditDept] = useState<Department | null>(null)
    const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
    const [searchText, setSearchText] = useState('')
    const [viewMode, setViewMode] = useState<ViewMode>('cards')

    const { data: departments = [], isLoading, isError, error } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: adminUsers = [] } = useQuery({ queryKey: ['adminUsers'], queryFn: getAdminUsers })
    const { data: company } = useQuery({ queryKey: ['attendance', 'company'], queryFn: getCompanyAttendance })
    const { data: leaves = [] } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: timesheets = [] } = useQuery({ queryKey: ['timesheets'], queryFn: getTimesheets })

    const managerUserIds = useMemo(
        () => new Set(adminUsers.filter((u) => u.roles.includes('Manager')).map((u) => u.id)),
        [adminUsers]
    )
    const userById = useMemo(() => new Map(adminUsers.map((u) => [u.id, u])), [adminUsers])
    const profilesByDept = useMemo(() => {
        const map = new Map<number, EmployeeProfile[]>()
        for (const p of profiles) {
            const arr = map.get(p.departmentId) ?? []
            arr.push(p); map.set(p.departmentId, arr)
        }
        return map
    }, [profiles])
    const attendanceByName = useMemo(() => {
        const map = new Map<string, DepartmentAttendance>()
        if (company) for (const d of company.departments) map.set(d.name, d)
        return map
    }, [company])

    const currentYear = new Date().getFullYear()

    const derivedAll: DerivedDept[] = useMemo(() => {
        return departments.map((dept) => {
            const deptProfiles = profilesByDept.get(dept.id) ?? []
            const headcount = deptProfiles.length
            const managers = deptProfiles
                .filter((p) => managerUserIds.has(p.userId))
                .map((p) => userById.get(p.userId))
                .filter((u): u is AdminUser => !!u)
            const manager = managers[0] ?? null
            const managerName = manager?.displayName || manager?.email || null

            const att = attendanceByName.get(dept.name)
            const counts = att
                ? { in: att.in, break: att.break, leave: att.leave, out: att.out, total: att.total }
                : { in: 0, break: 0, leave: 0, out: 0, total: headcount }

            const deptUserIds = new Set(deptProfiles.map((p) => p.userId))
            const pendingLeave = leaves.filter((l) => l.status === 'Pending' && deptUserIds.has(l.employeeId)).length
            const pendingTs = timesheets.filter(
                (t) => (t.status === 'Submitted' || t.status === 'Resubmitted') && t.departmentId === dept.id
            ).length

            const leaveUsedYTD = leaves
                .filter((l) =>
                    l.status === 'Approved'
                    && deptUserIds.has(l.employeeId)
                    && new Date(l.startDate).getFullYear() === currentYear
                )
                .reduce((s, l) => s + l.totalDays, 0)

            const leaveAllowance = deptProfiles.reduce(
                (s, p) => s + (p.annualLeaveEntitlement > 0 ? p.annualLeaveEntitlement : 20),
                0
            )

            // Needs attention: no manager OR > 25% not checked in
            const outPct = counts.total > 0 ? counts.out / counts.total : 0
            const needsAttention = !managerName || outPct >= 0.25
            const alertParts: string[] = []
            if (!managerName) alertParts.push('Manager position vacant')
            if (counts.out > 0 && outPct >= 0.25) alertParts.push(`${counts.out} employees not checked in`)
            const alert = alertParts.length > 0 ? alertParts.join(' · ') : null

            return {
                dept,
                headcount,
                managerName,
                managerInitials: manager ? initials(manager.displayName || manager.email) : null,
                managerBg: manager ? avatarBg(manager.displayName || manager.email) : 'text.disabled',
                members: deptProfiles.map((p) => ({ id: p.userId, name: p.displayName ?? '?' })),
                counts,
                pendingLeave,
                pendingTs,
                leaveUsedYTD,
                leaveAllowance,
                needsAttention,
                alert,
                gradient: gradientForDept(dept.id),
            }
        })
    }, [departments, profilesByDept, managerUserIds, userById, attendanceByName, leaves, timesheets, currentYear])

    const filtered = useMemo(() => {
        let out = derivedAll
        if (statusFilter === 'active') out = out.filter((d) => d.dept.isActive && !d.needsAttention)
        else if (statusFilter === 'attention') out = out.filter((d) => d.dept.isActive && d.needsAttention)
        else if (statusFilter === 'inactive') out = out.filter((d) => !d.dept.isActive)

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((d) =>
                d.dept.name.toLowerCase().includes(q) || d.dept.code.toLowerCase().includes(q)
            )
        }
        return out.sort((a, b) => a.dept.name.localeCompare(b.dept.name))
    }, [derivedAll, statusFilter, searchText])

    /* Aggregate stats */
    const totalHeadcount = derivedAll.reduce((s, d) => s + d.headcount, 0)
    const totalOnline = derivedAll.reduce((s, d) => s + d.counts.in, 0)
    const totalPending = derivedAll.reduce((s, d) => s + d.pendingLeave + d.pendingTs, 0)
    const needAttentionCount = derivedAll.filter((d) => d.needsAttention).length

    /* Mutations */
    const createMutation = useMutation({
        mutationFn: createDepartment,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['departments'] })
            setCreateOpen(false)
        },
    })
    const updateMutation = useMutation({
        mutationFn: ({ id, payload }: { id: number; payload: UpsertDepartmentRequest }) =>
            updateDepartment(id, payload),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['departments'] })
            setEditDept(null)
        },
    })
    const deleteMutation = useMutation({
        mutationFn: deleteDepartment,
        onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['departments'] }),
    })

    if (isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }
    if (isError) {
        return <Box sx={{ p: 2 }}><Alert severity="error">{getErrorMessage(error)}</Alert></Box>
    }

    return (
        <Box>
            {deleteMutation.isError && (
                <Alert severity="error" sx={{ mb: 2 }}>{getErrorMessage(deleteMutation.error)}</Alert>
            )}

            {/* Stats */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <StatCard label="🏢 Departments" value={String(departments.length)}
                          sub={`${derivedAll.filter((d) => !d.needsAttention).length} healthy · ${derivedAll.filter((d) => !d.dept.isActive).length} inactive`} />
                <StatCard label="👥 Total Headcount" value={String(totalHeadcount)} valueColor={'primary.main'}
                          sub={`${departments.length > 0 ? Math.round(totalHeadcount / departments.length) : 0} per dept · ${totalOnline} working now`} />
                <StatCard label="⏳ Pending Approvals" value={String(totalPending)} valueColor="#F59E0B"
                          sub="across all departments" />
                <StatCard label="⚠️ Need Attention" value={String(needAttentionCount)}
                          valueColor={needAttentionCount > 0 ? 'error.main' : 'success.main'}
                          sub={needAttentionCount === 0 ? 'all departments OK'
                                : `department${needAttentionCount === 1 ? '' : 's'} flagged`} />
            </Box>

            {/* Toolbar */}
            <Box sx={{
                bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                p: '10px 12px', display: 'flex', gap: '10px', flexWrap: 'wrap',
                alignItems: 'center', mb: '14px',
            }}>
                <Box sx={{ flex: 1, minWidth: 200 }}>
                    <Box
                        component="input"
                        type="search"
                        placeholder="Search departments…"
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
                <SelectFilter value={statusFilter} onChange={(v) => setStatusFilter(v as StatusFilter)} options={[
                    { value: 'all', label: `All statuses (${derivedAll.length})` },
                    { value: 'active', label: `Active (${derivedAll.filter((d) => d.dept.isActive && !d.needsAttention).length})` },
                    { value: 'attention', label: `Needs attention (${derivedAll.filter((d) => d.dept.isActive && d.needsAttention).length})` },
                    { value: 'inactive', label: `Inactive (${derivedAll.filter((d) => !d.dept.isActive).length})` },
                ]} />
                <ViewToggle mode={viewMode} onChange={setViewMode} />
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
                    + New department
                </Box>
            </Box>

            {/* Content */}
            {filtered.length === 0 && viewMode === 'cards' ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No departments match the current filters.
                </Box>
            ) : viewMode === 'cards' ? (
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' },
                    gap: '14px',
                }}>
                    {filtered.map((d) => (
                        <DepartmentCard
                            key={d.dept.id}
                            derived={d}
                            onEdit={() => setEditDept(d.dept)}
                            onDelete={async () => {
                                const result = await SweetAlert.fire({
                                    title: `Delete "${d.dept.name}"?`,
                                    text: d.headcount > 0 ? 'Cannot delete — users are assigned to this department.' : 'This cannot be undone.',
                                    icon: 'warning',
                                    showCancelButton: true,
                                    confirmButtonText: 'Yes, delete',
                                    cancelButtonText: 'Cancel',
                                    confirmButtonColor: '#EF4444',
                                    reverseButtons: true,
                                })
                                if (result.isConfirmed) deleteMutation.mutate(d.dept.id)
                            }}
                            onViewTeam={() => uiStore.navigateToAdminSection('users')}
                            onReport={() => uiStore.navigateToCompanyAttendance()}
                        />
                    ))}
                    <AddCard onClick={() => setCreateOpen(true)} />
                </Box>
            ) : (
                <TableView
                    rows={filtered}
                    onEdit={(d) => setEditDept(d)}
                    onDelete={async (d) => {
                        const result = await SweetAlert.fire({
                            title: `Delete "${d.name}"?`,
                            text: 'This will fail if users are assigned to it.',
                            icon: 'warning',
                            showCancelButton: true,
                            confirmButtonText: 'Yes, delete',
                            cancelButtonText: 'Cancel',
                            confirmButtonColor: '#EF4444',
                            reverseButtons: true,
                        })
                        if (result.isConfirmed) deleteMutation.mutate(d.id)
                    }}
                />
            )}

            <DepartmentFormDialog
                open={createOpen}
                title="New Department"
                isPending={createMutation.isPending}
                error={createMutation.error}
                onClose={() => setCreateOpen(false)}
                onSubmit={(payload) => createMutation.mutate(payload)}
            />
            <DepartmentFormDialog
                open={!!editDept}
                title="Edit Department"
                initial={editDept ?? undefined}
                isPending={updateMutation.isPending}
                error={updateMutation.error}
                onClose={() => setEditDept(null)}
                onSubmit={(payload) => editDept && updateMutation.mutate({ id: editDept.id, payload })}
            />
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Card                                                                     */
/* ════════════════════════════════════════════════════════════════════════ */

function DepartmentCard({ derived, onEdit, onDelete, onViewTeam, onReport }: {
    derived: DerivedDept
    onEdit: () => void
    onDelete: () => void
    onViewTeam: () => void
    onReport: () => void
}) {
    const { dept, counts, headcount, managerName, managerInitials, managerBg } = derived
    const totalPending = derived.pendingLeave + derived.pendingTs
    const inPct = counts.total > 0 ? (counts.in / counts.total) * 100 : 0
    const brkPct = counts.total > 0 ? (counts.break / counts.total) * 100 : 0
    const lvPct = counts.total > 0 ? (counts.leave / counts.total) * 100 : 0
    const outPct = counts.total > 0 ? (counts.out / counts.total) * 100 : 0
    const leaveUsedPct = derived.leaveAllowance > 0 ? (derived.leaveUsedYTD / derived.leaveAllowance) * 100 : 0
    const inPercent = Math.round(inPct)

    const visibleMembers = derived.members.slice(0, 7)
    const remaining = derived.headcount - visibleMembers.length

    const showStatusPill = !dept.isActive ? 'Inactive' : derived.needsAttention ? 'Needs attention' : 'Active'
    const statusPillBg = !dept.isActive ? 'rgba(255,255,255,0.2)' : derived.needsAttention ? softBg('warning') : 'rgba(255,255,255,0.2)'
    const statusPillFg = !dept.isActive ? '#fff' : derived.needsAttention ? 'warning.dark' : '#fff'

    return (
        <Box sx={{
            bgcolor: 'background.paper', border: `1px solid ${derived.needsAttention ? 'error.main' : 'divider'}`,
            borderRadius: '12px', overflow: 'hidden',
            display: 'flex', flexDirection: 'column',
            ...(derived.needsAttention && { boxShadow: '0 0 0 1px #FECACA' }),
        }}>
            {/* Header */}
            <Box sx={{
                background: dept.isActive ? derived.gradient : 'text.disabled',
                color: '#fff', p: '16px 18px',
                position: 'relative', overflow: 'hidden',
                '&::before': {
                    content: '""', position: 'absolute', right: -20, top: -20,
                    width: 100, height: 100, borderRadius: '50%',
                    bgcolor: 'rgba(255,255,255,0.1)',
                },
            }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '8px', position: 'relative', zIndex: 1 }}>
                    <Box sx={{ minWidth: 0 }}>
                        <Box component="span" sx={{
                            display: 'inline-block', bgcolor: 'rgba(255,255,255,0.25)',
                            px: '8px', py: '3px', borderRadius: '4px',
                            fontSize: 10, fontWeight: 700, letterSpacing: '0.05em',
                            mb: '6px',
                        }}>{dept.code}</Box>
                        <Box sx={{ fontSize: 17, fontWeight: 700, lineHeight: 1.2 }}>{dept.name}</Box>
                        <Box component="span" sx={{
                            display: 'inline-block', mt: '8px',
                            bgcolor: statusPillBg, color: statusPillFg,
                            px: '8px', py: '2px', borderRadius: '10px',
                            fontSize: 10, fontWeight: 500,
                        }}>{showStatusPill}</Box>
                    </Box>
                    <Box sx={{ display: 'flex', gap: '4px' }}>
                        <IconBtn title="Edit" onClick={onEdit}>✏️</IconBtn>
                        <IconBtn title="Delete" onClick={onDelete} danger>🗑</IconBtn>
                    </Box>
                </Box>
            </Box>

            {/* Body */}
            <Box sx={{ p: '14px 16px', display: 'flex', flexDirection: 'column', gap: '14px', flex: 1 }}>
                {/* Alert */}
                {derived.alert && (
                    <Box sx={{
                        p: '8px 12px', borderRadius: '6px',
                        bgcolor: softBg('error'), color: 'error.dark',
                        fontSize: 11, fontWeight: 500,
                        display: 'flex', alignItems: 'center', gap: '6px',
                        borderLeft: '3px solid #FF4D4F',
                    }}>
                        <Box component="span">⚠️</Box>
                        <Box component="span">{derived.alert}</Box>
                    </Box>
                )}

                {/* Manager */}
                {managerName ? (
                    <Box sx={{
                        display: 'flex', alignItems: 'center', gap: '10px',
                        p: '10px 12px', bgcolor: 'action.hover',
                        border: '1px solid', borderColor: 'divider', borderRadius: '8px',
                    }}>
                        <Box sx={{
                            width: 36, height: 36, borderRadius: '50%',
                            bgcolor: managerBg, color: '#fff',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            fontSize: 12, fontWeight: 600,
                        }}>{managerInitials}</Box>
                        <Box sx={{ flex: 1, minWidth: 0 }}>
                            <Box sx={{ fontSize: 10, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Department manager
                            </Box>
                            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {managerName}
                            </Box>
                        </Box>
                    </Box>
                ) : (
                    <Box sx={{
                        display: 'flex', alignItems: 'center', gap: '10px',
                        p: '10px 12px', bgcolor: '#FFFBEB',
                        border: '1px dashed #FDE68A', borderRadius: '8px',
                    }}>
                        <Box sx={{
                            width: 36, height: 36, borderRadius: '50%',
                            bgcolor: softBg('warning'), color: 'warning.dark',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            fontSize: 14,
                        }}>⚠</Box>
                        <Box sx={{ flex: 1 }}>
                            <Box sx={{ fontSize: 12, fontWeight: 600, color: 'warning.dark' }}>No manager assigned</Box>
                            <Box sx={{ fontSize: 11, color: '#78350F' }}>Approvals are routed to Admin</Box>
                        </Box>
                    </Box>
                )}

                {/* Team members */}
                <Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '8px' }}>
                        <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600 }}>
                            Team members
                        </Box>
                        <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                            {headcount}
                            <Box component="span" sx={{ fontSize: 11, color: 'text.secondary', fontWeight: 500, ml: '4px' }}>
                                {headcount === 1 ? 'person' : 'people'}
                            </Box>
                        </Box>
                    </Box>
                    {visibleMembers.length === 0 ? (
                        <Box sx={{ fontSize: 11, color: 'text.disabled', fontStyle: 'italic' }}>No members yet</Box>
                    ) : (
                        <Box sx={{ display: 'flex' }}>
                            {visibleMembers.map((m) => (
                                <Box
                                    key={m.id}
                                    title={m.name}
                                    sx={{
                                        width: 32, height: 32, borderRadius: '50%',
                                        bgcolor: avatarBg(m.name), color: '#fff',
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        fontSize: 11, fontWeight: 600,
                                        border: '2px solid #fff',
                                        marginLeft: '-6px', '&:first-of-type': { marginLeft: 0 },
                                    }}
                                >{initials(m.name)}</Box>
                            ))}
                            {remaining > 0 && (
                                <Box sx={{
                                    width: 32, height: 32, borderRadius: '50%',
                                    bgcolor: 'divider', color: 'text.secondary',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                    fontSize: 10, fontWeight: 600,
                                    border: '2px solid #fff', marginLeft: '-6px',
                                }}>+{remaining}</Box>
                            )}
                        </Box>
                    )}
                </Box>

                {/* Attendance bar */}
                {counts.total > 0 && (
                    <Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '6px' }}>
                            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600 }}>
                                Right now
                            </Box>
                            <Box sx={{ fontSize: 11, color: 'success.main', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
                                Live
                            </Box>
                        </Box>
                        <Box sx={{ display: 'flex', height: 10, bgcolor: 'action.hover', borderRadius: '4px', overflow: 'hidden', mb: '8px' }}>
                            {inPct > 0 && <Box title={`${counts.in} working`} sx={{ width: `${inPct}%`, bgcolor: 'success.main' }} />}
                            {brkPct > 0 && <Box title={`${counts.break} on break`} sx={{ width: `${brkPct}%`, bgcolor: 'warning.main' }} />}
                            {lvPct > 0 && <Box title={`${counts.leave} on leave`} sx={{ width: `${lvPct}%`, bgcolor: 'primary.main' }} />}
                            {outPct > 0 && <Box title={`${counts.out} not in`} sx={{ width: `${outPct}%`, bgcolor: 'divider' }} />}
                        </Box>
                        <Box sx={{ display: 'flex', gap: '10px', fontSize: 10, color: 'text.secondary', flexWrap: 'wrap' }}>
                            <LegendPill color="#22C47A" count={counts.in} label="working" />
                            {counts.break > 0 && <LegendPill color="#F59E0B" count={counts.break} label="break" />}
                            {counts.leave > 0 && <LegendPill color={'primary.main'} count={counts.leave} label="leave" />}
                            {counts.out > 0 && <LegendPill color="#9CA3AF" count={counts.out} label="not in" />}
                        </Box>
                    </Box>
                )}

                {/* Stats triplet */}
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '8px' }}>
                    <MiniStat
                        label="Leave used"
                        value={`${derived.leaveUsedYTD}`}
                        suffix={`/${derived.leaveAllowance}`}
                        sub={`${Math.round(leaveUsedPct)}% YTD`}
                    />
                    <MiniStat
                        label="Pending"
                        value={String(totalPending)}
                        valueColor={totalPending > 0 ? 'warning.main' : 'success.main'}
                        sub={`${derived.pendingLeave}L · ${derived.pendingTs}TS`}
                    />
                    <MiniStat
                        label="In today"
                        value={counts.total > 0 ? `${inPercent}%` : '—'}
                        valueColor={inPercent >= 80 ? 'success.main' : inPercent >= 60 ? 'warning.main' : 'error.main'}
                        sub={counts.total > 0 ? `${counts.in} of ${counts.total}` : 'no attendance'}
                    />
                </Box>
            </Box>

            {/* Footer */}
            <Box sx={{
                display: 'flex', gap: '6px', p: '12px 16px',
                borderTop: '1px solid #F3F4F6', bgcolor: 'action.hover',
            }}>
                <OutlineBtn onClick={onViewTeam} flex>👥 View team</OutlineBtn>
                <OutlineBtn onClick={onReport} flex>📊 Report</OutlineBtn>
            </Box>
        </Box>
    )
}

function AddCard({ onClick }: { onClick: () => void }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: 'transparent', border: `2px dashed ${'divider'}`,
                borderRadius: '12px', p: '24px',
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                cursor: 'pointer', fontFamily: 'inherit', textAlign: 'center',
                minHeight: 300, color: 'text.secondary',
                transition: 'all 0.15s',
                '&:hover': { borderColor: 'primary.main', color: 'primary.main', bgcolor: softBg('primary') },
            }}
        >
            <Box sx={{ fontSize: 36, fontWeight: 300, mb: '12px', lineHeight: 1 }}>+</Box>
            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Create a new department</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', lineHeight: 1.5 }}>
                Set up structure, assign a manager,<br/>and onboard team members
            </Box>
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Table view                                                                */
/* ════════════════════════════════════════════════════════════════════════ */

const TH = {
    py: '10px', px: '14px', fontSize: 11, fontWeight: 600, color: 'text.secondary',
    textTransform: 'uppercase' as const, letterSpacing: '0.05em',
    bgcolor: 'action.hover', borderBottom: '1px solid', borderColor: 'divider',
}
const TD = { py: '11px', px: '14px', fontSize: 13, color: 'text.primary', borderBottom: '1px solid #F3F4F6' }

function TableView({ rows, onEdit, onDelete }: {
    rows: DerivedDept[]
    onEdit: (d: Department) => void
    onDelete: (d: Department) => void
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
            <Box sx={{ overflowX: 'auto' }}>
                <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                    <TableHead>
                        <TableRow>
                            <TableCell sx={TH}>Name</TableCell>
                            <TableCell sx={TH}>Code</TableCell>
                            <TableCell sx={TH}>Manager</TableCell>
                            <TableCell sx={TH}>Headcount</TableCell>
                            <TableCell sx={TH}>In today</TableCell>
                            <TableCell sx={TH}>Pending</TableCell>
                            <TableCell sx={TH}>Status</TableCell>
                            <TableCell sx={TH}>Created</TableCell>
                            <TableCell sx={TH}>Actions</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {rows.length === 0 ? (
                            <TableRow>
                                <TableCell colSpan={9} sx={{ ...TD, textAlign: 'center', color: 'text.secondary', py: 4 }}>
                                    No departments match the current filters.
                                </TableCell>
                            </TableRow>
                        ) : rows.map((d) => {
                            const inPct = d.counts.total > 0 ? Math.round((d.counts.in / d.counts.total) * 100) : 0
                            const totalPending = d.pendingLeave + d.pendingTs
                            return (
                                <TableRow key={d.dept.id} sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}>
                                    <TableCell sx={TD}><strong>{d.dept.name}</strong></TableCell>
                                    <TableCell sx={TD}>
                                        <Box component="span" sx={{
                                            display: 'inline-block', bgcolor: softBg('info'), color: 'info.dark',
                                            borderRadius: '4px', px: 1, py: 0.25, fontSize: 11, fontWeight: 500,
                                        }}>{d.dept.code}</Box>
                                    </TableCell>
                                    <TableCell sx={{ ...TD, color: d.managerName ? 'text.primary' : 'warning.dark' }}>
                                        {d.managerName ?? '⚠ Vacant'}
                                    </TableCell>
                                    <TableCell sx={TD}>{d.headcount}</TableCell>
                                    <TableCell sx={TD}>{d.counts.total > 0 ? `${inPct}%` : '—'}</TableCell>
                                    <TableCell sx={TD}>{totalPending > 0 ? `${totalPending} (${d.pendingLeave}L · ${d.pendingTs}TS)` : '—'}</TableCell>
                                    <TableCell sx={TD}>
                                        <Box component="span" sx={{
                                            display: 'inline-flex', px: 1.25, py: 0.35, borderRadius: '20px',
                                            fontSize: 11, fontWeight: 500, whiteSpace: 'nowrap',
                                            bgcolor: !d.dept.isActive ? 'divider' : d.needsAttention ? softBg('error') : softBg('success'),
                                            color: !d.dept.isActive ? 'text.secondary' : d.needsAttention ? 'error.dark' : 'success.dark',
                                        }}>
                                            {!d.dept.isActive ? 'Inactive' : d.needsAttention ? 'Attention' : 'Active'}
                                        </Box>
                                    </TableCell>
                                    <TableCell sx={{ ...TD, color: 'text.secondary' }}>{fmtJoined(d.dept.createdAt)}</TableCell>
                                    <TableCell sx={TD}>
                                        <Stack direction="row" spacing={0.75}>
                                            <Button size="small" variant="outlined"
                                                    onClick={() => onEdit(d.dept)}
                                                    sx={{
                                                        fontSize: 12, py: '5px', px: 1.5, minWidth: 'unset',
                                                        color: 'text.secondary', borderColor: 'divider', textTransform: 'none',
                                                        '&:hover': { bgcolor: 'action.hover', borderColor: 'divider' },
                                                    }}>Edit</Button>
                                            <Button size="small" variant="outlined"
                                                    onClick={() => onDelete(d.dept)}
                                                    sx={{
                                                        fontSize: 12, py: '5px', px: 1.5, minWidth: 'unset',
                                                        color: 'error.main', borderColor: 'error.main', textTransform: 'none',
                                                        '&:hover': { bgcolor: '#FFF5F5', borderColor: 'error.main' },
                                                    }}>Delete</Button>
                                        </Stack>
                                    </TableCell>
                                </TableRow>
                            )
                        })}
                    </TableBody>
                </Table>
            </Box>
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* Small UI bits                                                             */
/* ════════════════════════════════════════════════════════════════════════ */

function StatCard({ label, value, sub, valueColor }: { label: string; value: string; sub: string; valueColor?: string }) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 22, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>{sub}</Box>
        </Box>
    )
}

function MiniStat({ label, value, suffix, sub, valueColor }: {
    label: string; value: string; suffix?: string; sub: string; valueColor?: string
}) {
    return (
        <Box sx={{ p: '8px', bgcolor: 'action.hover', borderRadius: '6px', textAlign: 'center' }}>
            <Box sx={{ fontSize: 9, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '4px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 14, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>
                {value}
                {suffix && <Box component="span" sx={{ fontSize: 10, color: 'text.disabled', fontWeight: 500 }}>{suffix}</Box>}
            </Box>
            <Box sx={{ fontSize: 9, color: 'text.secondary', mt: '3px' }}>{sub}</Box>
        </Box>
    )
}

function LegendPill({ color, count, label }: { color: string; count: number; label: string }) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: color }} />
            <Box component="strong" sx={{ color: 'text.primary', fontWeight: 600 }}>{count}</Box>
            {label}
        </Box>
    )
}

function IconBtn({ title, onClick, danger, children }: {
    title: string; onClick: () => void; danger?: boolean; children: React.ReactNode
}) {
    return (
        <Box
            component="button"
            title={title}
            onClick={(e: React.MouseEvent) => { e.stopPropagation(); onClick() }}
            sx={{
                width: 28, height: 28, borderRadius: '6px',
                bgcolor: 'rgba(255,255,255,0.2)', border: 'none', color: '#fff',
                cursor: 'pointer', fontFamily: 'inherit',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, lineHeight: 1,
                '&:hover': { bgcolor: danger ? 'rgba(255, 77, 79, 0.3)' : 'rgba(255,255,255,0.3)' },
            }}
        >
            {children}
        </Box>
    )
}

function OutlineBtn({ onClick, flex, children }: { onClick: () => void; flex?: boolean; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: 'background.paper', color: 'text.primary', border: '1px solid', borderColor: 'divider',
                borderRadius: '6px', px: '12px', py: '6px',
                fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
                flex: flex ? 1 : 'initial',
                '&:hover': { bgcolor: 'action.hover', borderColor: 'primary.main', color: 'primary.main' },
            }}
        >
            {children}
        </Box>
    )
}

function ViewToggle({ mode, onChange }: { mode: ViewMode; onChange: (m: ViewMode) => void }) {
    return (
        <Box sx={{
            display: 'flex', bgcolor: 'action.hover', borderRadius: '6px', p: '2px',
        }}>
            {(['cards', 'table'] as ViewMode[]).map((m) => (
                <Box
                    key={m}
                    component="button"
                    onClick={() => onChange(m)}
                    sx={{
                        bgcolor: mode === m ? 'background.paper' : 'transparent',
                        border: 'none', color: mode === m ? 'text.primary' : 'text.secondary',
                        px: '12px', py: '5px', borderRadius: '5px',
                        fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
                        transition: 'all 0.15s',
                    }}
                >
                    {m === 'cards' ? '▦ Cards' : '☰ Table'}
                </Box>
            ))}
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

/* ════════════════════════════════════════════════════════════════════════ */
/* Dialog                                                                    */
/* ════════════════════════════════════════════════════════════════════════ */

function DepartmentFormDialog(props: {
    open: boolean
    title: string
    initial?: Department
    isPending: boolean
    error: Error | null
    onClose: () => void
    onSubmit: (payload: UpsertDepartmentRequest) => void
}) {
    const [name, setName] = useState('')
    const [code, setCode] = useState('')
    const [isActive, setIsActive] = useState(true)

    useEffect(() => {
        if (props.open) {
            setName(props.initial?.name ?? '')
            setCode(props.initial?.code ?? '')
            setIsActive(props.initial?.isActive ?? true)
        }
    }, [props.open, props.initial])

    return (
        <AppDialog open={props.open} onClose={props.onClose} maxWidth="xs">
            <AppDialogTitle>{props.title}</AppDialogTitle>
            <AppDialogContent>
                <Stack spacing={2}>
                    <TextField
                        label="Name"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        fullWidth
                        required
                    />
                    <TextField
                        label="Code"
                        value={code}
                        onChange={(e) => setCode(e.target.value.toUpperCase())}
                        fullWidth
                        required
                        inputProps={{ maxLength: 10 }}
                        helperText="Short uppercase code e.g. ENG, HR"
                    />
                    <FormControlLabel
                        control={<Switch checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
                        label="Active"
                    />
                    {props.error != null && (
                        <Alert severity="error">{getErrorMessage(props.error)}</Alert>
                    )}
                </Stack>
            </AppDialogContent>
            <AppDialogActions>
                <Button variant="outlined" sx={cancelBtnSx} onClick={props.onClose} disabled={props.isPending}>Cancel</Button>
                <Button
                    variant="contained"
                    sx={saveBtnSx}
                    disabled={props.isPending || !name.trim() || !code.trim()}
                    onClick={() => props.onSubmit({ name: name.trim(), code: code.trim(), isActive })}
                >
                    Save
                </Button>
            </AppDialogActions>
        </AppDialog>
    )
}

export default DepartmentsPanel
