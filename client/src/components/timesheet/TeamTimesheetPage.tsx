import { useMemo, useState } from 'react'
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
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Tabs from '@mui/material/Tabs'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { approveTimesheet, getDepartments, getProjects, getTimesheet, getTimesheets, rejectTimesheet } from '../../lib/api'
import type { TimesheetEntry, TimesheetStatus, UserInfo } from '../../lib/types'
import type { Timesheet } from '../../lib/types/timesheet'
import { softBg, type SxColor } from '../../lib/theme-tokens'


const STATUS_COLORS: Record<string, { bg: SxColor; color: string }> = {
    Draft:       { bg: softBg('info'), color: 'info.dark' },
    Submitted:   { bg: softBg('warning'), color: 'warning.dark' },
    Approved:    { bg: softBg('success'), color: 'success.dark' },
    Rejected:    { bg: softBg('error'), color: 'error.dark' },
    Resubmitted: { bg: '#F3E8FF', color: '#6D28D9' },
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

function formatPeriod(start: string, end: string) {
    const s = new Date(start).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    const e = new Date(end).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
    return `${s} – ${e}`
}

function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
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

const TeamTimesheetPage = observer(function TeamTimesheetPage({ user }: { user: UserInfo }) {
    const isAdmin = user.roles.includes('Admin')
    const isManager = user.roles.includes('Manager')
    const queryClient = useQueryClient()

    const [statusTab, setStatusTab] = useState<StatusTab>('pending')
    const [deptFilter, setDeptFilter] = useState('all')
    const [actionTarget, setActionTarget] = useState<string | null>(null)
    const [viewTs, setViewTs] = useState<Timesheet | null>(null)
    const [rejectTarget, setRejectTarget] = useState<Timesheet | null>(null)
    const [rejectReason, setRejectReason] = useState('')
    const [rejectError, setRejectError] = useState('')

    const { data: timesheets = [], isLoading } = useQuery({
        queryKey: ['timesheets'],
        queryFn: getTimesheets,
    })

    const { data: departments = [] } = useQuery({
        queryKey: ['departments'],
        queryFn: getDepartments,
        enabled: isAdmin,
    })

    const { data: projects = [] } = useQuery({
        queryKey: ['projects'],
        queryFn: getProjects,
    })

    const { data: tsDetail, isLoading: isDetailLoading } = useQuery({
        queryKey: ['timesheet', viewTs?.id],
        queryFn: () => getTimesheet(viewTs!.id),
        enabled: !!viewTs?.id,
    })

    const activeProjects = projects.filter((p) => p.isActive)
    const entries = (tsDetail?.entries as TimesheetEntry[] | undefined) ?? []

    const deptById = useMemo(
        () => new Map(departments.map((d) => [d.id, d.name])),
        [departments]
    )

    const approveMutation = useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            setViewTs(null)
        },
        onSettled: () => setActionTarget(null),
    })

    const rejectMutation = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => rejectTimesheet(id, comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
            setViewTs(null)
        },
        onSettled: () => setActionTarget(null),
    })

    function openRejectDialog(ts: Timesheet) {
        setRejectTarget(ts)
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
            setRejectError('Please provide a reason for rejecting this timesheet.')
            return
        }
        setActionTarget(target.id)
        rejectMutation.mutate(
            { id: target.id, comment: trimmed },
            { onSuccess: () => { setRejectTarget(null); setRejectReason(''); setRejectError('') } }
        )
    }

    const needsAction = (status: TimesheetStatus) => status === 'Submitted' || status === 'Resubmitted'

    const filtered = useMemo(() => {
        let list = timesheets
        if (isAdmin && deptFilter !== 'all') {
            const deptId = departments.find((d) => d.name === deptFilter)?.id
            if (deptId != null) list = list.filter((t) => t.departmentId === deptId)
        }
        if (statusTab === 'pending') list = list.filter((t) => needsAction(t.status))
        else if (statusTab === 'approved') list = list.filter((t) => t.status === 'Approved')
        else if (statusTab === 'rejected') list = list.filter((t) => t.status === 'Rejected')
        return list.slice().sort((a, b) => {
            const aDate = a.submittedAt ?? a.createdAt
            const bDate = b.submittedAt ?? b.createdAt
            return new Date(bDate).getTime() - new Date(aDate).getTime()
        })
    }, [timesheets, statusTab, deptFilter, departments, isAdmin])

    const pendingCount = useMemo(() => timesheets.filter((t) => needsAction(t.status)).length, [timesheets])

    const managerTabs: { value: StatusTab; label: string }[] = [
        { value: 'pending', label: pendingCount > 0 ? `Pending (${pendingCount})` : 'Pending' },
        { value: 'approved', label: 'Approved' },
        { value: 'rejected', label: 'Rejected' },
        { value: 'all', label: 'All' },
    ]
    const adminTabs: { value: StatusTab; label: string }[] = [
        { value: 'all', label: 'All' },
        { value: 'pending', label: pendingCount > 0 ? `Pending (${pendingCount})` : 'Pending' },
        { value: 'approved', label: 'Approved' },
        { value: 'rejected', label: 'Rejected' },
    ]
    const tabs = isAdmin ? adminTabs : managerTabs

    const deptNames = useMemo(
        () => Array.from(new Set(departments.map((d) => d.name))).sort(),
        [departments]
    )

    const isPendingView = viewTs ? needsAction(viewTs.status) : false
    const isActioning = viewTs ? actionTarget === viewTs.id : false

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
                        {isAdmin ? 'All Timesheets' : 'Team Timesheets'}
                    </Typography>

                    {isAdmin && deptNames.length > 0 && (
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
                            {deptNames.map((d) => (
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
                        <Typography sx={{ fontSize: 13, color: 'text.disabled' }}>No timesheets found.</Typography>
                    </Box>
                ) : (
                    <Box sx={{ overflowX: 'auto' }}>
                        <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell sx={TH}>Employee</TableCell>
                                    {isAdmin && <TableCell sx={TH}>Dept</TableCell>}
                                    <TableCell sx={TH}>Period</TableCell>
                                    <TableCell sx={TH}>Hours</TableCell>
                                    <TableCell sx={TH}>Status</TableCell>
                                    <TableCell sx={TH}>Submitted</TableCell>
                                    <TableCell sx={TH}>Actions</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {filtered.map((ts) => {
                                    const isPending = needsAction(ts.status)
                                    const isWorking = actionTarget === ts.id
                                    const deptName = deptById.get(ts.departmentId) ?? '—'

                                    return (
                                        <TableRow
                                            key={ts.id}
                                            sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}
                                        >
                                            <TableCell sx={TD}><strong>{ts.employeeName}</strong></TableCell>
                                            {isAdmin && (
                                                <TableCell sx={TD}>
                                                    {deptName !== '—' ? <DeptBadge dept={deptName} /> : <span style={{ color: 'text.secondary' }}>—</span>}
                                                </TableCell>
                                            )}
                                            <TableCell sx={TD}>{formatPeriod(ts.periodStart, ts.periodEnd)}</TableCell>
                                            <TableCell sx={TD}>{Number(ts.totalHours).toFixed(1)} hrs</TableCell>
                                            <TableCell sx={TD}>
                                                <StatusBadge status={ts.status} />
                                            </TableCell>
                                            <TableCell sx={{ ...TD, color: 'text.secondary' }}>
                                                {ts.submittedAt
                                                    ? new Date(ts.submittedAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
                                                    : '—'}
                                            </TableCell>
                                            <TableCell sx={TD}>
                                                {isPending && (isAdmin || isManager) ? (
                                                    <Stack direction="row" spacing={0.75}>
                                                        <Button
                                                            size="small"
                                                            variant="outlined"
                                                            onClick={() => setViewTs(ts)}
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
                                                                setActionTarget(ts.id)
                                                                approveMutation.mutate(ts.id)
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
                                                            onClick={() => openRejectDialog(ts)}
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
                                                        onClick={() => setViewTs(ts)}
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

            {/* Timesheet detail dialog */}
            <Dialog
                open={!!viewTs}
                onClose={() => setViewTs(null)}
                maxWidth="md"
                fullWidth
                PaperProps={{ sx: { borderRadius: '12px' } }}
            >
                {viewTs && (
                    <>
                        <DialogTitle sx={{ pb: 1 }}>
                            <Stack direction="row" alignItems="center" spacing={1.5}>
                                <Typography sx={{ fontSize: 15, fontWeight: 700, color: 'text.primary' }}>
                                    {viewTs.employeeName} — {formatPeriod(viewTs.periodStart, viewTs.periodEnd)}
                                </Typography>
                                <StatusBadge status={viewTs.status} />
                            </Stack>
                        </DialogTitle>

                        <Divider />

                        <DialogContent sx={{ pt: 2 }}>
                            {isDetailLoading ? (
                                <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
                                    <CircularProgress size={24} />
                                </Box>
                            ) : (
                                <Stack spacing={2}>
                                    <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '8px', overflow: 'hidden' }}>
                                        <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                                            <TableHead>
                                                <TableRow>
                                                    <TableCell sx={TH}>Project</TableCell>
                                                    <TableCell sx={TH}>Date</TableCell>
                                                    <TableCell sx={TH}>Hours</TableCell>
                                                    <TableCell sx={TH}>Notes</TableCell>
                                                </TableRow>
                                            </TableHead>
                                            <TableBody>
                                                {entries.length === 0 ? (
                                                    <TableRow>
                                                        <TableCell colSpan={4} sx={{ ...TD, textAlign: 'center', color: 'text.disabled', py: 3 }}>
                                                            No entries.
                                                        </TableCell>
                                                    </TableRow>
                                                ) : (
                                                    entries.map((entry) => {
                                                        const projectName = activeProjects.find((p) => p.id === entry.projectId)?.name
                                                            ?? `Project #${entry.projectId}`
                                                        return (
                                                            <TableRow
                                                                key={entry.id}
                                                                sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}
                                                            >
                                                                <TableCell sx={TD}>{projectName}</TableCell>
                                                                <TableCell sx={TD}>{formatDate(entry.date)}</TableCell>
                                                                <TableCell sx={TD}>{Number(entry.hoursWorked).toFixed(1)}</TableCell>
                                                                <TableCell sx={{ ...TD, color: 'text.secondary' }}>{entry.notes ?? '—'}</TableCell>
                                                            </TableRow>
                                                        )
                                                    })
                                                )}
                                            </TableBody>
                                        </Table>
                                    </Box>

                                    <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
                                        <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                            Total:{' '}
                                            <span style={{ color: 'primary.main' }}>
                                                {entries.reduce((sum, e) => sum + Number(e.hoursWorked), 0).toFixed(1)} hrs
                                            </span>
                                        </Typography>
                                    </Box>
                                </Stack>
                            )}
                        </DialogContent>

                        <Divider />

                        <DialogActions sx={{ px: 3, py: 1.5, gap: 1 }}>
                            <Button
                                variant="outlined"
                                onClick={() => setViewTs(null)}
                                sx={{ textTransform: 'none', borderColor: 'divider', color: 'text.secondary' }}
                            >
                                Close
                            </Button>
                            {isPendingView && (isAdmin || isManager) && (
                                <>
                                    <Button
                                        variant="contained"
                                        disabled={isActioning}
                                        onClick={() => {
                                            const target = viewTs
                                            setViewTs(null)
                                            openRejectDialog(target)
                                        }}
                                        startIcon={isActioning && rejectMutation.isPending ? <CircularProgress size={14} color="inherit" /> : null}
                                        sx={{
                                            textTransform: 'none',
                                            bgcolor: 'error.main',
                                            '&:hover': { bgcolor: 'error.dark' },
                                            boxShadow: 'none',
                                        }}
                                    >
                                        Reject
                                    </Button>
                                    <Button
                                        variant="contained"
                                        disabled={isActioning}
                                        onClick={() => {
                                            setActionTarget(viewTs.id)
                                            approveMutation.mutate(viewTs.id)
                                        }}
                                        startIcon={isActioning && approveMutation.isPending ? <CircularProgress size={14} color="inherit" /> : null}
                                        sx={{
                                            textTransform: 'none',
                                            bgcolor: 'success.main',
                                            '&:hover': { bgcolor: 'success.dark' },
                                            boxShadow: 'none',
                                        }}
                                    >
                                        Approve
                                    </Button>
                                </>
                            )}
                        </DialogActions>
                    </>
                )}
            </Dialog>

            {/* Reject reason dialog */}
            <Dialog open={rejectTarget !== null} onClose={closeRejectDialog} maxWidth="xs" fullWidth>
                <DialogTitle sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary', pb: 1 }}>
                    Reject timesheet
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
                                {formatPeriod(rejectTarget.periodStart, rejectTarget.periodEnd)}
                                {' · '}
                                {Number(rejectTarget.totalHours).toFixed(1)} hrs
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

export default TeamTimesheetPage
