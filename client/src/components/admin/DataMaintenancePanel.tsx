import { useMutation, useQueryClient } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { clearApprovalHistory } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'
import { SweetAlert } from '../ui'

/**
 * Destructive maintenance, on a page of its own.
 *
 * "Clear all approval history" deletes thirty days of leave and timesheet approval
 * records and cannot be undone. It used to sit under a "Danger Zone" heading at the
 * bottom of Reminders & Notifications, one card below a row of notification toggles —
 * a page an admin opens to change when an email goes out. Nothing else on this page
 * is a preference, so nobody arrives here by accident.
 */
export default function DataMaintenancePanel() {
    const queryClient = useQueryClient()

    const clearHistoryMutation = useMutation({
        mutationFn: clearApprovalHistory,
        onSuccess: (count) => {
            void queryClient.invalidateQueries()
            void SweetAlert.fire({ icon: 'success', title: 'History cleared', text: `${count} approval record${count === 1 ? '' : 's'} from the past 30 days deleted.`, timer: 2600, showConfirmButton: false })
        },
        onError: (err) => SweetAlert.fire({ icon: 'error', title: 'Failed', text: getApiErrorMessage(err, 'Could not clear approval history.') }),
    })

    const onClearHistory = async () => {
        const res = await SweetAlert.fire({
            title: 'Clear approval history?',
            text: 'This deletes all leave & timesheet approval records from the past 30 days. This action cannot be undone.',
            icon: 'warning', showCancelButton: true, confirmButtonText: 'Yes, clear history',
            cancelButtonText: 'Cancel', confirmButtonColor: '#EF4444', reverseButtons: true,
        })
        if (res.isConfirmed) clearHistoryMutation.mutate()
    }

    return (
        <Stack spacing={2}>
            <Box>
                <Typography sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary' }}>🗄️ Data Maintenance</Typography>
                <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>
                    Irreversible operations on stored records. Nothing here is a preference.
                </Typography>
            </Box>

            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'error.light', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ px: 2.25, py: 1.75, borderBottom: '1px solid', borderColor: 'divider', bgcolor: softBg('error') }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'error.dark', display: 'flex', alignItems: 'center', gap: 1 }}>
                        <Box component="span" sx={{ fontSize: 16 }}>⚠️</Box>Approval history
                    </Typography>
                </Box>
                <Box sx={{ p: 2.25 }}>
                    <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: 0.5 }}>Clear all approval history</Typography>
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', mb: 1.5, lineHeight: 1.5 }}>
                        Deletes every leave and timesheet approval record from the past 30 days. The
                        requests themselves stay; the audit trail of who decided them, and when, is
                        removed. This cannot be undone.
                    </Typography>
                    <Button onClick={onClearHistory} disabled={clearHistoryMutation.isPending} variant="contained" size="small"
                        startIcon={clearHistoryMutation.isPending ? <CircularProgress size={13} color="inherit" /> : null}
                        sx={{ textTransform: 'none', bgcolor: 'error.main', '&:hover': { bgcolor: 'error.dark' }, boxShadow: 'none' }}>
                        🗑️ Clear history
                    </Button>
                </Box>
            </Box>
        </Stack>
    )
}
