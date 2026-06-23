import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'

/**
 * Reusable "reject with a required reason" dialog, shared by the annual-leave and
 * timesheet admin pages. The caller owns the reason/error state and the
 * approve/reject mutation; this component is purely presentational.
 */
export default function RejectReasonDialog({
    open,
    title,
    label,
    reason,
    error,
    isPending,
    onReasonChange,
    onClose,
    onConfirm,
}: {
    open: boolean
    title: string
    label: string
    reason: string
    error: string
    isPending: boolean
    onReasonChange: (value: string) => void
    onClose: () => void
    onConfirm: () => void
}) {
    return (
        <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
            <DialogTitle sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary', pb: 1 }}>
                {title}
            </DialogTitle>
            <DialogContent sx={{ px: 3, py: 2 }}>
                {open && (
                    <Stack spacing={1.5}>
                        <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>
                            Please provide a reason. The employee will see this message.
                        </Typography>
                        <Box sx={{ fontSize: 12, color: 'text.secondary' }}>
                            {label}
                        </Box>
                        <TextField
                            autoFocus
                            multiline
                            minRows={3}
                            maxRows={6}
                            fullWidth
                            placeholder="Reason for rejection (required)"
                            value={reason}
                            onChange={(e) => onReasonChange(e.target.value)}
                            error={!!error}
                            helperText={error || `${reason.trim().length}/500`}
                            inputProps={{ maxLength: 500 }}
                            sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                        />
                    </Stack>
                )}
            </DialogContent>
            <DialogActions sx={{ px: 3, py: 1.75, gap: 1 }}>
                <Button
                    size="small"
                    onClick={onClose}
                    disabled={isPending}
                    sx={{ textTransform: 'none', color: 'text.secondary' }}
                >
                    Cancel
                </Button>
                <Button
                    size="small"
                    variant="contained"
                    disabled={isPending || reason.trim().length === 0}
                    onClick={onConfirm}
                    sx={{ textTransform: 'none', bgcolor: 'error.main', '&:hover': { bgcolor: 'error.dark' }, boxShadow: 'none' }}
                >
                    {isPending ? 'Rejecting…' : 'Confirm Reject'}
                </Button>
            </DialogActions>
        </Dialog>
    )
}
