import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { SEND_BACK_REASON_MAX } from '../../lib/work-tasks'

/** Why a reviewer is returning a waiting task. The reason is required and reaches the assignees by email. */
export default function SendBackDialog({ open, taskTitle, pending, error, onCancel, onSubmit }: {
    open: boolean
    taskTitle: string
    pending: boolean
    error: string | null
    onCancel: () => void
    onSubmit: (reason: string) => void
}) {
    const [reason, setReason] = useState('')
    const trimmed = reason.trim()

    return (
        // Cleared once it has closed, so the next task opens on an empty reason.
        <Dialog open={open} onClose={pending ? undefined : onCancel} fullWidth maxWidth="sm" slotProps={{ transition: { onExited: () => setReason('') } }}>
            <DialogTitle>Send back "{taskTitle}"</DialogTitle>
            <DialogContent>
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
                <TextField
                    label="Reason"
                    required
                    fullWidth
                    multiline
                    minRows={3}
                    autoFocus
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    slotProps={{ htmlInput: { maxLength: SEND_BACK_REASON_MAX } }}
                    helperText={`${reason.length}/${SEND_BACK_REASON_MAX} · the assignees are emailed this`}
                    sx={{ mt: 1 }}
                />
            </DialogContent>
            <DialogActions>
                <Button onClick={onCancel} disabled={pending}>Cancel</Button>
                <Button variant="contained" color="warning" disabled={pending || trimmed.length === 0} onClick={() => onSubmit(trimmed)}>
                    Send back
                </Button>
            </DialogActions>
        </Dialog>
    )
}
