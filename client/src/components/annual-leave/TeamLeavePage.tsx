import React, { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Divider from '@mui/material/Divider'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Tab from '@mui/material/Tab'
import Table from '@mui/material/Table'
import TextField from '@mui/material/TextField'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Tabs from '@mui/material/Tabs'
import Typography from '@mui/material/Typography'
import { getAnnualLeaves, getLeaveStatusHistories, getLeaveTypes, updateLeaveStatus } from '../../lib/api'
import type { AnnualLeave, AnnualLeaveStatus, LeaveStatusHistory, UserInfo } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'


const STATUS_COLORS: Record<string, { bg: SxColor; color: string }> = {
    Pending:   { bg: softBg('warning'), color: 'warning.dark' },
    Approved:  { bg: softBg('success'), color: 'success.dark' },
    Rejected:  { bg: softBg('error'), color: 'error.dark' },
    Cancelled: { bg: 'divider', color: 'text.secondary' },
}

function StatusBadge({ status }: { status: string }) {
    const s = STATUS_COLORS[status] ?? { bg: 'divider', color: 'text.secondary' }
    return (
        <Box
            component="span"
            sx={{
                display: 'inline-flex',
                alignItems: 'center',
                px: 1.25,
                py: 0.35,
                borderRadius: '20px',
                fontSize: 11,
                fontWeight: 500,
                bgcolor: s.bg,
                color: s.color,
                whiteSpace: 'nowrap',
            }}
        >
            {status}
        </Box>
    )
}

function DeptBadge({ dept }: { dept: string }) {
    return (
        <Box
            component="span"
            sx={{
                display: 'inline-block',
                bgcolor: softBg('info'),
                color: 'info.dark',
                borderRadius: '4px',
                px: 1,
                py: 0.25,
                fontSize: 11,
                fontWeight: 500,
            }}
        >
            {dept}
        </Box>
    )
}

function formatDate(d: string) {
    return new Date(d).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

function DetailRow({ label, value }: { label: string; value: React.ReactNode }) {
    return (
        <Stack direction="row" spacing={1} alignItems="flex-start">
            <Typography sx={{ fontSize: 12, color: 'text.secondary', fontWeight: 500, minWidth: 110, pt: 0.15 }}>{label}</Typography>
            <Box sx={{ fontSize: 13, color: 'text.primary', flex: 1 }}>{value}</Box>
        </Stack>
    )
}

function evidenceFileName(url: string) {
    return url.split('/').pop()?.split('?')[0] || 'evidence-file'
}

type StatusTab = 'all' | 'pending' | 'approved' | 'rejected'

const TH = {
    py: '10px',
    px: '14px',
    fontSize: 11,
    fontWeight: 600,
    color: 'text.secondary',
    textTransform: 'uppercase' as const,
    letterSpacing: '0.05em',
    bgcolor: 'action.hover',
    borderBottom: '1px solid', borderColor: 'divider',
}

const TD = {
    py: '11px',
    px: '14px',
    fontSize: 13,
    color: 'text.primary',
    borderBottom: `1px solid #F3F4F6`,
}

const TeamLeavePage = observer(function TeamLeavePage({ user }: { user: UserInfo }) {
    const isAdmin = user.roles.includes('Admin')
    const isManager = user.roles.includes('Manager')
    const queryClient = useQueryClient()

    const defaultTab: StatusTab = isManager && !isAdmin ? 'pending' : 'all'
    const [statusTab, setStatusTab] = useState<StatusTab>(defaultTab)
    const [deptFilter, setDeptFilter] = useState('all')
    const [actionTarget, setActionTarget] = useState<string | null>(null)
    const [viewLeave, setViewLeave] = useState<AnnualLeave | null>(null)
    const [rejectTarget, setRejectTarget] = useState<AnnualLeave | null>(null)
    const [rejectReason, setRejectReason] = useState('')
    const [rejectError, setRejectError] = useState('')

    useEffect(() => {
        const sync = () => {
            if (window.location.hash === '#team-leave-approvals') {
                setStatusTab('pending')
            } else if (window.location.hash === '#team-leave') {
                setStatusTab(defaultTab)
            }
        }
        sync()
        window.addEventListener('hashchange', sync)
        return () => window.removeEventListener('hashchange', sync)
    }, [defaultTab])

    const { data: allLeaves = [], isLoading } = useQuery({
        queryKey: ['annualLeaves'],
        queryFn: getAnnualLeaves,
    })

    const { data: leaveTypes = [] } = useQuery({
        queryKey: ['leaveTypes'],
        queryFn: getLeaveTypes,
    })

    const { data: histories = [] } = useQuery({
        queryKey: ['leaveStatusHistories'],
        queryFn: getLeaveStatusHistories,
    })

    const leaveTypeById = useMemo(
        () => new Map(leaveTypes.map((lt) => [lt.id, lt.name])),
        [leaveTypes]
    )

    const departments = useMemo(
        () => Array.from(new Set(allLeaves.map((l) => l.departmentName).filter(Boolean))).sort(),
        [allLeaves]
    )

    const latestHistoryByLeave = useMemo(() => {
        const map = new Map<string, LeaveStatusHistory>()
        for (const h of histories) {
            const prev = map.get(h.annualLeaveId)
            if (!prev || new Date(h.changedAt) > new Date(prev.changedAt)) {
                map.set(h.annualLeaveId, h)
            }
        }
        return map
    }, [histories])

    const overlappingForView = useMemo(() => {
        if (!viewLeave) return [] as AnnualLeave[]
        const viewStart = new Date(viewLeave.startDate).getTime()
        const viewEnd = new Date(viewLeave.endDate).getTime()
        return allLeaves.filter((l) =>
            l.id !== viewLeave.id
            && l.departmentName === viewLeave.departmentName
            && (l.status === 'Pending' || l.status === 'Approved')
            && new Date(l.startDate).getTime() <= viewEnd
            && new Date(l.endDate).getTime() >= viewStart
        )
    }, [viewLeave, allLeaves])

    const approveMutation = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Approved'),
        onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
        onSettled: () => setActionTarget(null),
    })

    const rejectMutation = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => updateLeaveStatus(id, 'Rejected', comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            void queryClient.invalidateQueries({ queryKey: ['leaveStatusHistories'] })
        },
        onSettled: () => setActionTarget(null),
    })

    function openRejectDialog(leave: AnnualLeave) {
        setRejectTarget(leave)
        setRejectReason('')
        setRejectError('')
    }

    function closeRejectDialog() {
        if (rejectMutation.isPending) return
        setRejectTarget(null)
        setRejectReason('')
        setRejectError('')
    }

    function confirmReject() {
        const target = rejectTarget
        if (!target) return
        const trimmed = rejectReason.trim()
        if (trimmed.length === 0) {
            setRejectError('Please provide a reason for rejecting this request.')
            return
        }
        setActionTarget(target.id)
        rejectMutation.mutate(
            { id: target.id, comment: trimmed },
            { onSuccess: () => { setRejectTarget(null); setRejectReason(''); setRejectError('') } }
        )
    }

    const filtered = useMemo(() => {
        let leaves = allLeaves
        if (deptFilter !== 'all') leaves = leaves.filter((l) => l.departmentName === deptFilter)
        if (statusTab !== 'all') {
            const s = (statusTab.charAt(0).toUpperCase() + statusTab.slice(1)) as AnnualLeaveStatus
            leaves = leaves.filter((l) => l.status === s)
        }
        return leaves.slice().sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    }, [allLeaves, deptFilter, statusTab])

    const pendingCount = useMemo(() => allLeaves.filter((l) => l.status === 'Pending').length, [allLeaves])

    const managerTabs: { value: StatusTab; label: string }[] = [
        { value: 'pending', label: pendingCount > 0 ? `Pending (${pendingCount})` : 'Pending' },
        { value: 'approved', label: 'Approved' },
        { value: 'all', label: 'All' },
    ]
    const adminTabs: { value: StatusTab; label: string }[] = [
        { value: 'all', label: 'All' },
        { value: 'pending', label: pendingCount > 0 ? `Pending (${pendingCount})` : 'Pending' },
        { value: 'approved', label: 'Approved' },
        { value: 'rejected', label: 'Rejected' },
    ]
    const tabs = isAdmin ? adminTabs : managerTabs

    return (
        <Stack spacing={2.5}>
            {/* Status tabs */}
            <Box sx={{ borderBottom: '2px solid', borderColor: 'divider', mb: -1 }}>
                <Tabs
                    value={statusTab}
                    onChange={(_e, v: StatusTab) => setStatusTab(v)}
                    TabIndicatorProps={{ style: { backgroundColor: 'primary.main', height: 2 } }}
                    sx={{
                        minHeight: 44,
                        '& .MuiTab-root': {
                            textTransform: 'none',
                            fontWeight: 500,
                            fontSize: 13,
                            color: 'text.secondary',
                            minHeight: 44,
                            py: 0,
                            px: 2.25,
                        },
                        '& .Mui-selected': { color: '#4F8EF7 !important' },
                    }}
                >
                    {tabs.map((t) => <Tab key={t.value} value={t.value} label={t.label} />)}
                </Tabs>
            </Box>

            {/* Table card */}
            <Paper
                elevation={0}
                sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}
            >
                {/* Card header */}
                <Box
                    sx={{
                        px: '18px',
                        py: '14px',
                        borderBottom: '1px solid', borderColor: 'divider',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        gap: 2,
                    }}
                >
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                        {isAdmin ? 'All Leave Requests' : 'Team Leave Requests'}
                    </Typography>

                    {isAdmin && departments.length > 0 && (
                        <Select
                            size="small"
                            value={deptFilter}
                            onChange={(e) => setDeptFilter(e.target.value)}
                            sx={{
                                fontSize: 12,
                                '& .MuiSelect-select': { py: '5px', px: '10px' },
                                '& fieldset': { borderColor: '#D1D5DB', borderRadius: '6px' },
                            }}
                        >
                            <MenuItem value="all">All Departments</MenuItem>
                            {departments.map((d) => (
                                <MenuItem key={d} value={d}>{d}</MenuItem>
                            ))}
                        </Select>
                    )}
                </Box>

                {/* Content */}
                {isLoading ? (
                    <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                        <CircularProgress size={24} />
                    </Box>
                ) : filtered.length === 0 ? (
                    <Box sx={{ textAlign: 'center', py: 6 }}>
                        <Typography sx={{ fontSize: 13, color: 'text.disabled' }}>No leave requests found.</Typography>
                    </Box>
                ) : (
                    <Box sx={{ overflowX: 'auto' }}>
                        <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell sx={TH}>Employee</TableCell>
                                    {isAdmin && <TableCell sx={TH}>Dept</TableCell>}
                                    <TableCell sx={TH}>Leave Type</TableCell>
                                    <TableCell sx={TH}>Start</TableCell>
                                    <TableCell sx={TH}>End</TableCell>
                                    <TableCell sx={TH}>Days</TableCell>
                                    <TableCell sx={TH}>Status</TableCell>
                                    <TableCell sx={TH}>Actions</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {filtered.map((leave) => {
                                    const isPending = leave.status === 'Pending'
                                    const isWorking = actionTarget === leave.id
                                    const leaveTypeName = leave.leaveTypeId != null
                                        ? (leaveTypeById.get(leave.leaveTypeId) ?? 'Annual Leave')
                                        : 'Annual Leave'

                                    return (
                                        <TableRow key={leave.id} sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}>
                                            <TableCell sx={TD}><strong>{leave.employeeName}</strong></TableCell>
                                            {isAdmin && (
                                                <TableCell sx={TD}>
                                                    <DeptBadge dept={leave.departmentName} />
                                                </TableCell>
                                            )}
                                            <TableCell sx={TD}>{leaveTypeName}</TableCell>
                                            <TableCell sx={TD}>{formatDate(leave.startDate)}</TableCell>
                                            <TableCell sx={TD}>{formatDate(leave.endDate)}</TableCell>
                                            <TableCell sx={TD}>{leave.totalDays}</TableCell>
                                            <TableCell sx={TD}>
                                                <StatusBadge status={leave.status} />
                                            </TableCell>
                                            <TableCell sx={TD}>
                                                {isPending ? (
                                                    <Stack direction="row" spacing={0.75}>
                                                        <Button
                                                            size="small"
                                                            variant="outlined"
                                                            onClick={() => setViewLeave(leave)}
                                                            sx={{
                                                                fontSize: 12,
                                                                py: '5px',
                                                                px: 1.5,
                                                                minWidth: 'unset',
                                                                color: 'text.secondary',
                                                                borderColor: 'divider',
                                                                textTransform: 'none',
                                                                '&:hover': { bgcolor: 'action.hover', borderColor: 'divider' },
                                                            }}
                                                        >
                                                            View
                                                        </Button>
                                                        <Button
                                                            size="small"
                                                            variant="contained"
                                                            disabled={isWorking}
                                                            onClick={() => {
                                                                setActionTarget(leave.id)
                                                                approveMutation.mutate(leave.id)
                                                            }}
                                                            sx={{
                                                                fontSize: 12,
                                                                py: '5px',
                                                                px: 1.5,
                                                                minWidth: 'unset',
                                                                bgcolor: 'success.main',
                                                                '&:hover': { bgcolor: 'success.dark' },
                                                                textTransform: 'none',
                                                                boxShadow: 'none',
                                                            }}
                                                        >
                                                            {isWorking && approveMutation.isPending ? '…' : 'Approve'}
                                                        </Button>
                                                        <Button
                                                            size="small"
                                                            variant="contained"
                                                            disabled={isWorking}
                                                            onClick={() => openRejectDialog(leave)}
                                                            sx={{
                                                                fontSize: 12,
                                                                py: '5px',
                                                                px: 1.5,
                                                                minWidth: 'unset',
                                                                bgcolor: 'error.main',
                                                                '&:hover': { bgcolor: 'error.dark' },
                                                                textTransform: 'none',
                                                                boxShadow: 'none',
                                                            }}
                                                        >
                                                            {isWorking && rejectMutation.isPending ? '…' : 'Reject'}
                                                        </Button>
                                                    </Stack>
                                                ) : (
                                                    <Button
                                                        size="small"
                                                        variant="outlined"
                                                        onClick={() => setViewLeave(leave)}
                                                        sx={{
                                                            fontSize: 12,
                                                            py: '5px',
                                                            px: 1.5,
                                                            minWidth: 'unset',
                                                            color: 'text.secondary',
                                                            borderColor: 'divider',
                                                            textTransform: 'none',
                                                            '&:hover': { bgcolor: 'action.hover', borderColor: 'divider' },
                                                        }}
                                                    >
                                                        View
                                                    </Button>
                                                )}
                                            </TableCell>
                                        </TableRow>
                                    )
                                })}
                            </TableBody>
                        </Table>
                    </Box>
                )}
            </Paper>
            {/* Leave detail dialog */}
            <Dialog open={viewLeave !== null} onClose={() => setViewLeave(null)} maxWidth="sm" fullWidth>
                <DialogTitle sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary', pb: 1 }}>
                    Leave Request Details
                </DialogTitle>
                <DialogContent sx={{ px: 3, py: 2 }}>
                    {viewLeave && (
                        <Stack spacing={1.5}>
                            <DetailRow label="Employee" value={viewLeave.employeeName} />
                            {isAdmin && <DetailRow label="Department" value={viewLeave.departmentName} />}
                            <DetailRow
                                label="Leave Type"
                                value={viewLeave.leaveTypeId != null ? (leaveTypeById.get(viewLeave.leaveTypeId) ?? 'Annual Leave') : 'Annual Leave'}
                            />
                            <Divider sx={{ my: 0.5 }} />
                            <DetailRow label="Start Date" value={formatDate(viewLeave.startDate)} />
                            <DetailRow label="End Date" value={formatDate(viewLeave.endDate)} />
                            <DetailRow label="Total Days" value={`${viewLeave.totalDays} working day${viewLeave.totalDays !== 1 ? 's' : ''}`} />
                            <Divider sx={{ my: 0.5 }} />
                            <DetailRow label="Status" value={<StatusBadge status={viewLeave.status} />} />
                            {viewLeave.reason && <DetailRow label="Reason" value={viewLeave.reason} />}
                            {viewLeave.evidenceUrl && viewLeave.evidenceUrl.trim() !== '' && (
                                <DetailRow
                                    label="Evidence"
                                    value={
                                        <Button
                                            size="small"
                                            variant="outlined"
                                            component="a"
                                            href={viewLeave.evidenceUrl}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            download={evidenceFileName(viewLeave.evidenceUrl)}
                                            sx={{
                                                fontSize: 12, textTransform: 'none',
                                                borderColor: 'primary.main', color: 'primary.main',
                                                py: '3px', px: 1.25,
                                                '&:hover': { bgcolor: softBg('info'), borderColor: 'primary.main' },
                                            }}
                                        >
                                            Open File
                                        </Button>
                                    }
                                />
                            )}
                            <DetailRow label="Submitted" value={formatDate(viewLeave.createdAt)} />
                            {viewLeave.approvedAt && (
                                <DetailRow label="Actioned" value={formatDate(viewLeave.approvedAt)} />
                            )}
                            {(() => {
                                const today = new Date(); today.setHours(0, 0, 0, 0)
                                const start = new Date(viewLeave.startDate); start.setHours(0, 0, 0, 0)
                                const daysUntil = Math.round((start.getTime() - today.getTime()) / 86_400_000)
                                const noticeText = daysUntil < 0
                                    ? `Started ${Math.abs(daysUntil)} day${Math.abs(daysUntil) === 1 ? '' : 's'} ago`
                                    : daysUntil === 0 ? 'Starts today'
                                    : daysUntil === 1 ? 'Starts tomorrow'
                                    : `${daysUntil} days notice`
                                return <DetailRow label="Notice" value={noticeText} />
                            })()}
                            <Divider sx={{ my: 0.5 }} />
                            <DetailRow
                                label="Team coverage"
                                value={
                                    overlappingForView.length === 0 ? (
                                        <Typography sx={{ fontSize: 13, color: 'success.dark' }}>
                                            ● No other team members on leave these days.
                                        </Typography>
                                    ) : (
                                        <Stack spacing={0.5}>
                                            <Typography sx={{ fontSize: 12, color: 'warning.dark', fontWeight: 600 }}>
                                                ⚠ {overlappingForView.length} overlap{overlappingForView.length === 1 ? '' : 's'} in {viewLeave.departmentName || 'this department'}
                                            </Typography>
                                            {overlappingForView.slice(0, 6).map((c) => (
                                                <Box key={c.id} sx={{ fontSize: 12, color: 'text.primary' }}>
                                                    <strong>{c.employeeName}</strong>{' · '}
                                                    {formatDate(c.startDate)} – {formatDate(c.endDate)}
                                                    {' '}
                                                    <Box component="span" sx={{
                                                        display: 'inline-block', px: '6px', py: '1px',
                                                        bgcolor: c.status === 'Approved' ? softBg('success') : softBg('warning'),
                                                        color: c.status === 'Approved' ? 'success.dark' : 'warning.dark',
                                                        borderRadius: '8px', fontSize: 10, fontWeight: 500,
                                                    }}>{c.status}</Box>
                                                </Box>
                                            ))}
                                            {overlappingForView.length > 6 && (
                                                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                                                    …and {overlappingForView.length - 6} more
                                                </Typography>
                                            )}
                                        </Stack>
                                    )
                                }
                            />
                            {(() => {
                                const fb = latestHistoryByLeave.get(viewLeave.id)
                                if (!fb?.comment) return null
                                const isRejected = viewLeave.status === 'Rejected'
                                const bg = isRejected ? softBg('error') : softBg('success')
                                const fg = isRejected ? 'error.dark' : 'success.dark'
                                const accent = isRejected ? 'error.main' : 'success.main'
                                const label = isRejected ? 'Rejection reason' : 'Manager note'
                                return (
                                    <Box sx={{
                                        mt: 0.5, p: '10px 14px', bgcolor: bg, color: fg,
                                        borderLeft: '3px solid', borderLeftColor: accent, borderRadius: '6px',
                                    }}>
                                        <Box sx={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '4px' }}>
                                            {label}
                                        </Box>
                                        <Box sx={{ fontSize: 13, lineHeight: 1.5 }}>
                                            <Box component="strong">{fb.changedByUserName}:</Box> "{fb.comment}"
                                        </Box>
                                    </Box>
                                )
                            })()}
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions sx={{ px: 3, py: 1.75, gap: 1 }}>
                    <Button
                        size="small"
                        onClick={() => setViewLeave(null)}
                        sx={{ textTransform: 'none', color: 'text.secondary' }}
                    >
                        Close
                    </Button>
                    {viewLeave && viewLeave.status !== 'Cancelled' && viewLeave.status !== 'Approved' && (
                        <Button
                            size="small"
                            variant="contained"
                            disabled={approveMutation.isPending}
                            onClick={() => {
                                setActionTarget(viewLeave.id)
                                approveMutation.mutate(viewLeave.id)
                                setViewLeave(null)
                            }}
                            sx={{ textTransform: 'none', bgcolor: 'success.main', '&:hover': { bgcolor: 'success.dark' }, boxShadow: 'none' }}
                        >
                            Approve
                        </Button>
                    )}
                    {viewLeave && viewLeave.status !== 'Cancelled' && viewLeave.status !== 'Rejected' && (
                        <Button
                            size="small"
                            variant="contained"
                            disabled={rejectMutation.isPending}
                            onClick={() => {
                                const target = viewLeave
                                setViewLeave(null)
                                openRejectDialog(target)
                            }}
                            sx={{ textTransform: 'none', bgcolor: 'error.main', '&:hover': { bgcolor: 'error.dark' }, boxShadow: 'none' }}
                        >
                            Reject
                        </Button>
                    )}
                </DialogActions>
            </Dialog>

            {/* Reject reason dialog */}
            <Dialog open={rejectTarget !== null} onClose={closeRejectDialog} maxWidth="xs" fullWidth>
                <DialogTitle sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary', pb: 1 }}>
                    Reject leave request
                </DialogTitle>
                <DialogContent sx={{ px: 3, py: 2 }}>
                    {rejectTarget && (
                        <Stack spacing={1.5}>
                            <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>
                                Please provide a reason. The employee will see this message.
                            </Typography>
                            <Box sx={{ fontSize: 12, color: 'text.secondary' }}>
                                <strong style={{ color: 'inherit' }}>{rejectTarget.employeeName}</strong>
                                {' · '}
                                {formatDate(rejectTarget.startDate)} – {formatDate(rejectTarget.endDate)}
                                {' · '}
                                {rejectTarget.totalDays} day{rejectTarget.totalDays === 1 ? '' : 's'}
                            </Box>
                            <TextField
                                autoFocus
                                multiline
                                minRows={3}
                                maxRows={6}
                                fullWidth
                                placeholder="Reason for rejection (required)"
                                value={rejectReason}
                                onChange={(e) => {
                                    setRejectReason(e.target.value)
                                    if (rejectError) setRejectError('')
                                }}
                                error={!!rejectError}
                                helperText={rejectError || `${rejectReason.trim().length}/500`}
                                inputProps={{ maxLength: 500 }}
                                sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                            />
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions sx={{ px: 3, py: 1.75, gap: 1 }}>
                    <Button
                        size="small"
                        onClick={closeRejectDialog}
                        disabled={rejectMutation.isPending}
                        sx={{ textTransform: 'none', color: 'text.secondary' }}
                    >
                        Cancel
                    </Button>
                    <Button
                        size="small"
                        variant="contained"
                        disabled={rejectMutation.isPending || rejectReason.trim().length === 0}
                        onClick={confirmReject}
                        sx={{ textTransform: 'none', bgcolor: 'error.main', '&:hover': { bgcolor: 'error.dark' }, boxShadow: 'none' }}
                    >
                        {rejectMutation.isPending ? 'Rejecting…' : 'Confirm Reject'}
                    </Button>
                </DialogActions>
            </Dialog>
        </Stack>
    )
})

export default TeamLeavePage
