import { useMemo, useState, type MouseEvent } from 'react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import DialogContentText from '@mui/material/DialogContentText'
import IconButton from '@mui/material/IconButton'
import { AppDialog, AppDialogTitle, AppDialogContent, AppDialogActions, cancelBtnSx, dangerBtnSx } from '../ui'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import Link from '@mui/material/Link'
import {
    Check as CheckIcon,
    Close as CloseIcon,
    Delete as DeleteIcon,
    Edit as EditIcon,
    MoreVert as MoreVertIcon,
} from '@mui/icons-material'
import { useDeleteAnnualLeave, useLeaveTypes, useUpdateLeaveStatus } from '../../lib/hooks'
import type { AnnualLeave, AnnualLeaveStatus, UserInfo } from '../../lib/types'
import AnnualLeaveForm from './AnnualLeaveForm'

function statusColor(status: AnnualLeaveStatus): 'warning' | 'success' | 'error' | 'default' {
    switch (status) {
        case 'Pending': return 'warning'
        case 'Approved': return 'success'
        case 'Rejected': return 'error'
        case 'Cancelled': return 'default'
    }
}

function formatDate(dateStr: string) {
    return new Date(dateStr).toLocaleDateString('en-GB', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
    })
}

interface AnnualLeaveCardProps {
    leave: AnnualLeave
    user: UserInfo
}

function AnnualLeaveCard({ leave, user }: AnnualLeaveCardProps) {
    const [editOpen, setEditOpen] = useState(false)
    const [deleteOpen, setDeleteOpen] = useState(false)
    const [rejectOpen, setRejectOpen] = useState(false)
    const [rejectComment, setRejectComment] = useState('')
    const [actionsAnchorEl, setActionsAnchorEl] = useState<null | HTMLElement>(null)

    const isAdmin = user.roles.includes('Admin')
    const isManager = user.roles.includes('Manager')
    const isOwnLeave = leave.employeeId === user.id

    const { data: leaveTypes = [] } = useLeaveTypes()

    const leaveTypeName = useMemo(() => {
        if (leave.leaveTypeId == null) {
            return 'Annual Leave'
        }

        return leaveTypes.find((leaveType) => leaveType.id === leave.leaveTypeId)?.name ?? 'Leave Type'
    }, [leave.leaveTypeId, leaveTypes])

    // Approve / Reject: Admin any, Manager dept-team-only (server enforces), Employee never
    const canApproveReject = (isAdmin || isManager) && leave.status === 'Pending'

    // Edit dates/type/reason: Approved/Rejected requests are admin-only; otherwise Admin any, Manager any, Employee own only
    const isLockedStatus = leave.status === 'Rejected' || leave.status === 'Approved'
    const canEdit = isLockedStatus ? isAdmin : (isAdmin || isManager || isOwnLeave)
    const showLockedStatusNote = isLockedStatus && !isAdmin
    const lockedStatusMessage = leave.status === 'Rejected'
        ? 'This request was rejected and is now read-only. If needed, submit a new request.'
        : leave.status === 'Approved'
            ? 'This request has been approved and is now read-only.'
            : 'This request can no longer be edited.'

    // Cancel: Admin any, Manager own, Employee own-pending-only
    const canCancel =
        isAdmin ||
        (isManager && isOwnLeave) ||
        (isOwnLeave && leave.status === 'Pending')
    const hasOverflowActions = canEdit || canCancel
    const isActionsMenuOpen = Boolean(actionsAnchorEl)

    const handleOpenActionsMenu = (event: MouseEvent<HTMLElement>) => {
        setActionsAnchorEl(event.currentTarget)
    }

    const handleCloseActionsMenu = () => {
        setActionsAnchorEl(null)
    }

    const deleteMutation = useDeleteAnnualLeave()
    const statusMutation = useUpdateLeaveStatus()

    const statusAccentColor =
        leave.status === 'Approved'
            ? 'rgba(5, 150, 105, 0.78)'
            : leave.status === 'Rejected'
                ? 'rgba(239, 68, 68, 0.34)'
                : leave.status === 'Cancelled'
                    ? 'rgba(107, 114, 128, 0.45)'
                    : 'rgba(217, 119, 6, 0.78)'

    return (
        <>
            <Paper
                id={`leave-card-${leave.id}`}
                elevation={0}
                sx={{
                    p: { xs: 1.15, sm: 1.25 },
                    borderRadius: 2.5,
                    border: '1px solid',
                    borderColor: 'rgba(15, 23, 42, 0.08)',
                    borderLeft: '2px solid',
                    borderLeftColor: statusAccentColor,
                    backgroundColor: 'rgba(248,250,252,0.72)',
                    boxShadow: '0 6px 16px rgba(15, 23, 42, 0.03)',
                    transition: 'background-color 0.15s ease, box-shadow 0.15s ease',
                    '&:hover': {
                        backgroundColor: 'rgba(248,250,252,0.9)',
                        boxShadow: '0 8px 18px rgba(15, 23, 42, 0.05)',
                    },
                }}
            >
                <Stack spacing={0.85}>
                    <Stack
                        direction={{ xs: 'column', md: 'row' }}
                        justifyContent="space-between"
                        alignItems={{ xs: 'flex-start', md: 'flex-start' }}
                        spacing={0.75}
                    >
                        <Stack spacing={0.55} sx={{ minWidth: 0 }}>
                            <Stack direction="row" spacing={0.55} flexWrap="wrap" useFlexGap alignItems="center">
                                <Chip
                                    label={leave.status}
                                    color={statusColor(leave.status)}
                                    size="small"
                                    sx={{ fontWeight: 800 }}
                                />
                                <Chip
                                    label={leaveTypeName}
                                    size="small"
                                    variant="outlined"
                                    color="info"
                                    sx={{
                                        fontWeight: 700,
                                        backgroundColor: 'rgba(255,255,255,0.6)',
                                        borderColor: 'rgba(14, 116, 144, 0.18)',
                                    }}
                                />
                                <Chip
                                    label={`${leave.totalDays} day${leave.totalDays !== 1 ? 's' : ''}`}
                                    size="small"
                                    variant="outlined"
                                    sx={{
                                        fontWeight: 700,
                                        borderColor: 'rgba(15, 23, 42, 0.1)',
                                        backgroundColor: 'rgba(255,255,255,0.5)',
                                    }}
                                />
                            </Stack>

                            <Typography
                                variant="h6"
                                fontWeight={800}
                                sx={{
                                    fontSize: { xs: '1.06rem', sm: '1.14rem' },
                                    lineHeight: 1.2,
                                    color: 'text.primary',
                                }}
                            >
                                {formatDate(leave.startDate)} — {formatDate(leave.endDate)}
                            </Typography>

                            {leave.employeeName && (
                                <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 600 }}>
                                    {leave.employeeName}
                                    {leave.departmentName ? ` • ${leave.departmentName}` : ''}
                                </Typography>
                            )}

                            {leave.evidenceUrl ? (
                                <Link
                                    href={leave.evidenceUrl}
                                    target="_blank"
                                    rel="noreferrer"
                                    underline="hover"
                                    sx={{ fontSize: '0.86rem', fontWeight: 700, width: 'fit-content' }}
                                >
                                    View evidence
                                </Link>
                            ) : null}
                        </Stack>

                        <Stack direction="row" spacing={0.35} alignItems="center" sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                            {canApproveReject ? (
                                <>
                                    <Tooltip title="Approve request">
                                        <IconButton
                                            size="small"
                                            color="success"
                                            disabled={statusMutation.isPending}
                                            onClick={() => statusMutation.mutate({ id: leave.id, status: 'Approved' })}
                                            sx={{ border: '1px solid', borderColor: 'rgba(34,197,94,0.25)', backgroundColor: 'rgba(34,197,94,0.06)' }}
                                        >
                                            {statusMutation.isPending
                                                ? <CircularProgress size={16} />
                                                : <CheckIcon fontSize="small" />}
                                        </IconButton>
                                    </Tooltip>
                                    <Tooltip title="Reject request">
                                        <IconButton
                                            size="small"
                                            color="error"
                                            disabled={statusMutation.isPending}
                                            onClick={() => setRejectOpen(true)}
                                            sx={{ border: '1px solid', borderColor: 'rgba(239,68,68,0.2)', backgroundColor: 'rgba(239,68,68,0.05)' }}
                                        >
                                            <CloseIcon fontSize="small" />
                                        </IconButton>
                                    </Tooltip>
                                </>
                            ) : null}

                            {hasOverflowActions ? (
                                <>
                                    <Tooltip title="More actions">
                                        <IconButton
                                            size="small"
                                            onClick={handleOpenActionsMenu}
                                            disabled={deleteMutation.isPending || statusMutation.isPending}
                                            sx={{
                                                border: '1px solid',
                                                borderColor: 'rgba(15,23,42,0.10)',
                                                backgroundColor: 'rgba(248,250,252,0.95)',
                                                color: 'text.secondary',
                                                '&:hover': {
                                                    backgroundColor: 'rgba(255,255,255,0.92)',
                                                },
                                            }}
                                        >
                                            <MoreVertIcon fontSize="small" />
                                        </IconButton>
                                    </Tooltip>

                                    <Menu
                                        anchorEl={actionsAnchorEl}
                                        open={isActionsMenuOpen}
                                        onClose={handleCloseActionsMenu}
                                        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
                                        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
                                        slotProps={{
                                            paper: {
                                                elevation: 0,
                                                sx: {
                                                    mt: 0.5,
                                                    minWidth: 170,
                                                    borderRadius: 2.5,
                                                    border: '1px solid',
                                                    borderColor: 'rgba(15, 23, 42, 0.08)',
                                                    boxShadow: '0 12px 24px rgba(15, 23, 42, 0.08)',
                                                },
                                            },
                                        }}
                                    >
                                        {canEdit ? (
                                            <MenuItem
                                                onClick={() => {
                                                    handleCloseActionsMenu()
                                                    setEditOpen(true)
                                                }}
                                                sx={{ gap: 1, fontSize: '0.95rem' }}
                                            >
                                                <EditIcon fontSize="small" />
                                                Edit request
                                            </MenuItem>
                                        ) : null}

                                        {canCancel ? (
                                            <MenuItem
                                                onClick={() => {
                                                    handleCloseActionsMenu()
                                                    setDeleteOpen(true)
                                                }}
                                                sx={{ gap: 1, fontSize: '0.95rem', color: 'error.main' }}
                                            >
                                                <DeleteIcon fontSize="small" />
                                                Delete request
                                            </MenuItem>
                                        ) : null}
                                    </Menu>
                                </>
                            ) : null}
                        </Stack>
                    </Stack>

                    {showLockedStatusNote ? (
                        <Box
                            sx={{
                                px: 0.9,
                                py: 0.5,
                                borderRadius: 2.5,
                                backgroundColor: 'rgba(15, 23, 42, 0.035)',
                            }}
                        >
                            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', lineHeight: 1.4 }}>
                                {lockedStatusMessage}
                            </Typography>
                        </Box>
                    ) : null}

                    {leave.reason ? (
                        <Box
                            sx={{
                                px: 1.05,
                                py: 0.8,
                                borderRadius: 1.5,
                                backgroundColor: 'rgba(255, 255, 255, 0.56)',
                                border: '1px solid rgba(15, 23, 42, 0.06)',
                            }}
                        >
                            <Typography
                                variant="caption"
                                color="text.secondary"
                                sx={{
                                    display: 'block',
                                    mb: 0.3,
                                    fontWeight: 600,
                                }}
                            >
                                Reason
                            </Typography>
                            <Typography
                                variant="body2"
                                color="text.primary"
                                sx={{
                                    lineHeight: 1.5,
                                    whiteSpace: 'pre-wrap',
                                }}
                            >
                                {leave.reason}
                            </Typography>
                        </Box>
                    ) : null}

                    <Typography variant="caption" color="text.disabled" sx={{ pt: 0 }}>
                        {leave.approvedAt
                            ? `Approved on ${formatDate(leave.approvedAt)}`
                            : `Submitted ${formatDate(leave.createdAt)}`}
                    </Typography>
                </Stack>
            </Paper>

            {/* Edit dialog */}
            <AnnualLeaveForm
                key={leave.id}
                open={editOpen}
                onClose={() => setEditOpen(false)}
                leave={leave}
            />

            {/* Cancel confirmation dialog */}
            <AppDialog open={deleteOpen} onClose={() => setDeleteOpen(false)} maxWidth="xs">
                <AppDialogTitle>Cancel Leave Request?</AppDialogTitle>
                <AppDialogContent>
                    <DialogContentText>
                        Cancel the leave request from{' '}
                        <strong>{formatDate(leave.startDate)}</strong> to{' '}
                        <strong>{formatDate(leave.endDate)}</strong>?
                    </DialogContentText>
                </AppDialogContent>
                <AppDialogActions>
                    <Button variant="outlined" sx={cancelBtnSx} onClick={() => setDeleteOpen(false)} disabled={deleteMutation.isPending}>
                        Keep
                    </Button>
                    <Button
                        variant="contained"
                        sx={dangerBtnSx}
                        disabled={deleteMutation.isPending}
                        startIcon={deleteMutation.isPending ? <CircularProgress size={16} color="inherit" /> : null}
                        onClick={() => deleteMutation.mutate(leave.id, { onSuccess: () => setDeleteOpen(false) })}
                    >
                        {deleteMutation.isPending ? 'Cancelling...' : 'Cancel Request'}
                    </Button>
                </AppDialogActions>
            </AppDialog>

            {/* Reject dialog */}
            <AppDialog open={rejectOpen} onClose={() => setRejectOpen(false)} maxWidth="xs">
                <AppDialogTitle>Reject Leave Request?</AppDialogTitle>
                <AppDialogContent>
                    <Stack spacing={2}>
                        <DialogContentText>
                            Reject the leave request from{' '}
                            <strong>{formatDate(leave.startDate)}</strong> to{' '}
                            <strong>{formatDate(leave.endDate)}</strong>?
                        </DialogContentText>
                        <TextField
                            label="Reason (optional)"
                            value={rejectComment}
                            onChange={(e) => setRejectComment(e.target.value)}
                            multiline
                            rows={2}
                            fullWidth
                        />
                    </Stack>
                </AppDialogContent>
                <AppDialogActions>
                    <Button variant="outlined" sx={cancelBtnSx} onClick={() => setRejectOpen(false)} disabled={statusMutation.isPending}>
                        Cancel
                    </Button>
                    <Button
                        variant="contained"
                        sx={dangerBtnSx}
                        disabled={statusMutation.isPending}
                        startIcon={statusMutation.isPending ? <CircularProgress size={16} color="inherit" /> : null}
                        onClick={() => statusMutation.mutate(
                            { id: leave.id, status: 'Rejected', comment: rejectComment || undefined },
                            { onSuccess: () => { setRejectOpen(false); setRejectComment('') } }
                        )}
                    >
                        {statusMutation.isPending ? 'Rejecting...' : 'Reject'}
                    </Button>
                </AppDialogActions>
            </AppDialog>
        </>
    )
}

export default AnnualLeaveCard

