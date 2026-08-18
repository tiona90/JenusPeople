import { useState, useEffect, useMemo } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import { AppDialog, AppDialogTitle, AppDialogContent, AppDialogActions, cancelBtnSx, saveBtnSx } from '../ui'
import InputAdornment from '@mui/material/InputAdornment'
import Stack from '@mui/material/Stack'
import MenuItem from '@mui/material/MenuItem'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { AttachFile as AttachFileIcon, CalendarMonth as CalendarMonthIcon, OpenInNew as OpenInNewIcon } from '@mui/icons-material'
import Box from '@mui/material/Box'
import { createAnnualLeave, editAnnualLeave, getLeaveTypes, getAdminUsers, uploadLeaveEvidence } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import { softBg } from '../../lib/theme-tokens'
import { buildAnnualLeaveSchema, type AnnualLeaveFormValues } from '../../lib/validation/leave'
import type { AnnualLeave, CreateAnnualLeaveRequest, EditAnnualLeaveRequest, LeaveStatusHistory } from '../../lib/types'

function getErrorMessage(error: unknown) {
    return getApiErrorMessage(error, 'Something went wrong. Please try again.')
}

function toInputDate(dateStr: string) {
    return dateStr ? dateStr.substring(0, 10) : ''
}

interface AnnualLeaveFormProps {
    open: boolean
    onClose: () => void
    /** Pass an existing leave to edit; omit for create */
    leave?: AnnualLeave
    /** When true, an "Assign to Employee" dropdown is shown so admin can create on behalf of a user */
    isAdmin?: boolean
    /** When true, the form is rendered in view-only mode (no edits, no submit) */
    readOnly?: boolean
    /** Optional manager/admin feedback to show in read-only mode (e.g. rejection reason) */
    statusFeedback?: LeaveStatusHistory
}

function AnnualLeaveForm({ open, onClose, leave, isAdmin = false, readOnly = false, statusFeedback }: AnnualLeaveFormProps) {
    const isEdit = !!leave && !readOnly
    const queryClient = useQueryClient()
    const { authStore } = useStore()

    const [evidenceUrl, setEvidenceUrl] = useState(leave?.evidenceUrl ?? '')
    const [evidenceFile, setEvidenceFile] = useState<File | null>(null)

    const requireEmployee = isAdmin && !isEdit
    const schema = useMemo(() => buildAnnualLeaveSchema(requireEmployee), [requireEmployee])

    const buildDefaults = (): AnnualLeaveFormValues => ({
        employeeId: '',
        startDate: leave ? toInputDate(leave.startDate) : '',
        endDate: leave ? toInputDate(leave.endDate) : '',
        leaveTypeId: leave?.leaveTypeId ?? 0,
        reason: leave?.reason ?? '',
    })

    const { control, handleSubmit, reset, watch } = useForm<AnnualLeaveFormValues>({
        resolver: zodResolver(schema),
        defaultValues: buildDefaults(),
    })

    const { data: leaveTypes, isLoading: isLoadingLeaveTypes } = useQuery({
        queryKey: ['leaveTypes'],
        queryFn: getLeaveTypes,
    })

    const { data: adminUsers, isLoading: isLoadingUsers } = useQuery({
        queryKey: ['adminUsers'],
        queryFn: getAdminUsers,
        enabled: isAdmin && !isEdit,
    })

    // Sync form state on open (populate from leave) and on close (reset).
    useEffect(() => {
        reset(buildDefaults())
        setEvidenceUrl(leave?.evidenceUrl ?? '')
        setEvidenceFile(null)
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [open, leave?.id])

    const createMutation = useMutation({
        mutationFn: (req: CreateAnnualLeaveRequest) => createAnnualLeave(req),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            onClose()
        },
    })

    const editMutation = useMutation({
        mutationFn: (req: EditAnnualLeaveRequest) => editAnnualLeave(req),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            onClose()
        },
    })

    const uploadEvidenceMutation = useMutation({
        mutationFn: (file: File) => uploadLeaveEvidence(file),
    })

    const isPending = createMutation.isPending || editMutation.isPending || uploadEvidenceMutation.isPending
    const error = createMutation.error ?? editMutation.error ?? uploadEvidenceMutation.error
    const dialogTitle = readOnly
        ? 'Leave Request Details'
        : isEdit ? 'Edit Leave Request' : isAdmin ? 'Assign Leave to User' : 'New Leave Request'
    const dialogDescription = readOnly
        ? 'View details (read only).'
        : isEdit
            ? 'Update dates, leave type, and notes.'
            : isAdmin
                ? 'Select an employee and create a leave request on their behalf.'
                : 'Fill in details and submit your leave request.'
    const submitLabel = isPending ? 'Saving...' : isEdit ? 'Save Changes' : isAdmin ? 'Assign Leave' : 'Submit Request'

    const dateFieldSx = {
        '& .MuiInputBase-root': {
            borderRadius: 2,
            backgroundColor: 'rgba(15, 23, 42, 0.02)',
        },
        '& input[type="date"]': {
            fontWeight: 600,
        },
        '& input[type="date"]::-webkit-calendar-picker-indicator': {
            cursor: 'pointer',
            opacity: 0.8,
            filter: 'saturate(1.2)',
        },
    }

    const watchedStart = watch('startDate')

    // Validated submit (react-hook-form blocks this when the zod schema fails,
    // so the existing API calls only fire on valid input).
    const onValid = async (values: AnnualLeaveFormValues) => {
        try {
            let nextEvidenceUrl = evidenceUrl.trim() || undefined

            if (evidenceFile) {
                const uploadResult = await uploadEvidenceMutation.mutateAsync(evidenceFile)
                nextEvidenceUrl = uploadResult.evidenceUrl
                setEvidenceUrl(uploadResult.evidenceUrl)
            }

            if (isEdit && leave) {
                await editMutation.mutateAsync({
                    id: leave.id,
                    startDate: values.startDate,
                    endDate: values.endDate,
                    leaveTypeId: values.leaveTypeId,
                    reason: values.reason,
                    evidenceUrl: nextEvidenceUrl,
                    // This form doesn't edit coverage — carry the existing delegate
                    // through so an edit here never silently drops it.
                    delegateId: leave.delegateId ?? undefined,
                })
            } else {
                await createMutation.mutateAsync({
                    startDate: values.startDate,
                    endDate: values.endDate,
                    leaveTypeId: values.leaveTypeId,
                    reason: values.reason,
                    evidenceUrl: nextEvidenceUrl,
                    employeeId: isAdmin ? values.employeeId : (authStore.user?.id ?? ''),
                })
            }
        } catch {
            // Mutation state already exposes the API error to the form.
        }
    }

    return (
        <AppDialog open={open} onClose={onClose} maxWidth="sm">
            <AppDialogTitle>
                {dialogTitle}
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, fontWeight: 400, fontSize: 13 }}>
                    {dialogDescription}
                </Typography>
            </AppDialogTitle>

            <AppDialogContent>
                <Stack spacing={3} component="form" id="leave-form" onSubmit={handleSubmit(onValid)} noValidate sx={{ pt: 1 }}>
                    {readOnly && leave && (() => {
                        const status = leave.status
                        const isRejected = status === 'Rejected'
                        const isApproved = status === 'Approved'
                        const isCancelled = status === 'Cancelled'
                        const bg = isRejected ? softBg('error') : isApproved ? softBg('success') : 'action.hover'
                        const fg = isRejected ? 'error.dark' : isApproved ? 'success.dark' : 'text.secondary'
                        const accent = isRejected ? 'error.main' : isApproved ? 'success.main' : 'text.disabled'
                        const label = isRejected
                            ? (statusFeedback?.comment ? 'Rejection reason' : 'Rejected')
                            : isApproved
                                ? (statusFeedback?.comment ? 'Approval note' : 'Approved')
                                : isCancelled
                                    ? 'Cancelled'
                                    : 'Status'
                        return (
                            <Box sx={{
                                p: '10px 14px', bgcolor: bg, color: fg,
                                borderLeft: '3px solid', borderLeftColor: accent,
                                borderRadius: '6px',
                            }}>
                                <Box sx={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '4px' }}>
                                    {label}
                                </Box>
                                {statusFeedback?.comment ? (
                                    <Box sx={{ fontSize: 13, lineHeight: 1.5 }}>
                                        <Box component="strong">{statusFeedback.changedByUserName}:</Box>{' '}
                                        "{statusFeedback.comment}"
                                    </Box>
                                ) : (
                                    <Box sx={{ fontSize: 13, lineHeight: 1.5 }}>
                                        {isRejected
                                            ? 'No reason was provided.'
                                            : isApproved
                                                ? 'Your request has been approved.'
                                                : isCancelled
                                                    ? 'This request was cancelled.'
                                                    : status}
                                    </Box>
                                )}
                            </Box>
                        )
                    })()}
                    {isAdmin && !isEdit && (
                        <Controller
                            name="employeeId"
                            control={control}
                            render={({ field, fieldState }) => (
                                <TextField
                                    {...field}
                                    label="Assign to Employee"
                                    select
                                    fullWidth
                                    disabled={isLoadingUsers}
                                    error={!!fieldState.error}
                                    helperText={fieldState.error?.message ?? 'Required'}
                                >
                                    <MenuItem value="" disabled>
                                        Select employee
                                    </MenuItem>
                                    {(adminUsers ?? [])
                                        .filter((u) => u.roles.includes('Employee') || u.roles.includes('Manager'))
                                        .map((u) => (
                                            <MenuItem key={u.id} value={u.id}>
                                                {u.displayName} ({u.email})
                                            </MenuItem>
                                        ))}
                                </TextField>
                            )}
                        />
                    )}
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                        {readOnly ? (
                            <TextField
                                label="Start Date"
                                type="date"
                                value={leave ? toInputDate(leave.startDate) : ''}
                                fullWidth
                                disabled
                                InputLabelProps={{ shrink: true }}
                                helperText=" "
                                InputProps={{
                                    readOnly: true,
                                    endAdornment: (
                                        <InputAdornment position="end">
                                            <CalendarMonthIcon fontSize="small" color="action" />
                                        </InputAdornment>
                                    ),
                                }}
                                sx={dateFieldSx}
                            />
                        ) : (
                            <Controller
                                name="startDate"
                                control={control}
                                render={({ field, fieldState }) => (
                                    <TextField
                                        {...field}
                                        label="Start Date"
                                        type="date"
                                        required
                                        fullWidth
                                        InputLabelProps={{ shrink: true }}
                                        error={!!fieldState.error}
                                        helperText={fieldState.error?.message ?? 'Select start of leave'}
                                        InputProps={{
                                            endAdornment: (
                                                <InputAdornment position="end">
                                                    <CalendarMonthIcon fontSize="small" color="action" />
                                                </InputAdornment>
                                            ),
                                        }}
                                        sx={dateFieldSx}
                                    />
                                )}
                            />
                        )}
                        {readOnly ? (
                            <TextField
                                label="End Date"
                                type="date"
                                value={leave ? toInputDate(leave.endDate) : ''}
                                fullWidth
                                disabled
                                InputLabelProps={{ shrink: true }}
                                helperText=" "
                                InputProps={{
                                    readOnly: true,
                                    endAdornment: (
                                        <InputAdornment position="end">
                                            <CalendarMonthIcon fontSize="small" color="action" />
                                        </InputAdornment>
                                    ),
                                }}
                                sx={dateFieldSx}
                            />
                        ) : (
                            <Controller
                                name="endDate"
                                control={control}
                                render={({ field, fieldState }) => (
                                    <TextField
                                        {...field}
                                        label="End Date"
                                        type="date"
                                        required
                                        fullWidth
                                        InputLabelProps={{ shrink: true }}
                                        inputProps={{ min: watchedStart }}
                                        error={!!fieldState.error}
                                        helperText={fieldState.error?.message ?? 'Select end of leave'}
                                        InputProps={{
                                            endAdornment: (
                                                <InputAdornment position="end">
                                                    <CalendarMonthIcon fontSize="small" color="action" />
                                                </InputAdornment>
                                            ),
                                        }}
                                        sx={dateFieldSx}
                                    />
                                )}
                            />
                        )}
                    </Stack>
                    {readOnly ? (
                        <TextField
                            label="Leave Type"
                            value={leaveTypes?.find((lt) => lt.id === (leave?.leaveTypeId ?? 0))?.name ?? ''}
                            fullWidth
                            disabled
                            helperText=" "
                            InputProps={{ readOnly: true }}
                        />
                    ) : (
                        <Controller
                            name="leaveTypeId"
                            control={control}
                            render={({ field, fieldState }) => (
                                <TextField
                                    label="Leave Type"
                                    select
                                    value={field.value}
                                    onChange={(e) => field.onChange(Number(e.target.value))}
                                    onBlur={field.onBlur}
                                    inputRef={field.ref}
                                    required
                                    fullWidth
                                    disabled={isLoadingLeaveTypes}
                                    error={!!fieldState.error}
                                    helperText={fieldState.error?.message ?? 'Required'}
                                >
                                    <MenuItem value={0} disabled>
                                        Select leave type
                                    </MenuItem>
                                    {(leaveTypes ?? []).map((leaveType) => (
                                        <MenuItem key={leaveType.id} value={leaveType.id}>
                                            {leaveType.name}
                                        </MenuItem>
                                    ))}
                                </TextField>
                            )}
                        />
                    )}
                    {(() => {
                        if (readOnly) {
                            const trimmed = (leave?.reason ?? '').trim()
                            const isPlaceholderOnly = trimmed === '' || /^[-_‐-―−.·•]+$/.test(trimmed)
                            if (isPlaceholderOnly) return null
                            return (
                                <TextField
                                    label="Reason"
                                    value={leave?.reason ?? ''}
                                    multiline
                                    rows={3}
                                    fullWidth
                                    disabled
                                    InputProps={{ readOnly: true }}
                                    helperText=" "
                                />
                            )
                        }
                        return (
                            <Controller
                                name="reason"
                                control={control}
                                render={({ field, fieldState }) => (
                                    <TextField
                                        {...field}
                                        label="Reason"
                                        multiline
                                        rows={3}
                                        required
                                        fullWidth
                                        error={!!fieldState.error}
                                        helperText={fieldState.error?.message ?? 'Required'}
                                        placeholder="Add a short reason for this request"
                                    />
                                )}
                            />
                        )
                    })()}

                    <Stack spacing={0.75}>
                        {!readOnly && (
                            <Button component="label" variant="outlined" startIcon={<AttachFileIcon />} disabled={isPending} sx={{ alignSelf: 'flex-start' }}>
                                {evidenceFile ? 'Change evidence file' : evidenceUrl ? 'Replace evidence file' : 'Upload evidence'}
                                <input
                                    hidden
                                    type="file"
                                    accept=".pdf,.jpg,.jpeg,.png,.doc,.docx"
                                    onChange={(event) => {
                                        const selectedFile = event.target.files?.[0] ?? null
                                        setEvidenceFile(selectedFile)
                                    }}
                                />
                            </Button>
                        )}

                        {evidenceFile ? (
                            <Typography variant="body2" color="text.secondary">
                                Selected file: {evidenceFile.name}
                            </Typography>
                        ) : evidenceUrl ? (
                            <Button
                                size="small"
                                href={evidenceUrl}
                                target="_blank"
                                rel="noreferrer"
                                endIcon={<OpenInNewIcon fontSize="inherit" />}
                                sx={{ alignSelf: 'flex-start', px: 0, textTransform: 'none' }}
                            >
                                {readOnly ? 'View evidence' : 'View current evidence'}
                            </Button>
                        ) : readOnly ? (
                            <Typography variant="body2" color="text.disabled">
                                No evidence attached.
                            </Typography>
                        ) : null}

                        {!readOnly && (
                            <Typography variant="caption" color="text.secondary">
                                Optional: upload PDF, image, DOC, or DOCX evidence (max 10 MB).
                            </Typography>
                        )}
                    </Stack>

                    {error ? <Alert severity="error">{getErrorMessage(error)}</Alert> : null}
                </Stack>
            </AppDialogContent>

            <AppDialogActions>
                <Button variant="outlined" sx={cancelBtnSx} onClick={onClose} disabled={isPending}>
                    {readOnly ? 'Close' : 'Cancel'}
                </Button>
                {!readOnly && (
                    <Button
                        type="submit"
                        form="leave-form"
                        variant="contained"
                        sx={saveBtnSx}
                        disabled={isPending || isLoadingLeaveTypes}
                        startIcon={isPending ? <CircularProgress size={16} color="inherit" /> : null}
                    >
                        {submitLabel}
                    </Button>
                )}
            </AppDialogActions>
        </AppDialog>
    )
}

export default AnnualLeaveForm
