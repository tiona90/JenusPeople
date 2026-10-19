import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Popover from '@mui/material/Popover'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import { logTaskTime } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { LogTaskTimeResult, WorkTask } from '../../lib/types'

/**
 * Hours on a task without opening the timesheet: a date (today by default, never
 * after it), hours in half hours and an optional note. The server puts them on the
 * week's draft and refuses a week already submitted; its message shows here.
 */
export default function LogTimeButton({ task, today, onLogged, dense = false }: {
    task: WorkTask
    today: string
    onLogged: (result: LogTaskTimeResult, hours: number, date: string) => void
    dense?: boolean
}) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null)
    const [date, setDate] = useState(today)
    const [hours, setHours] = useState('1')
    const [notes, setNotes] = useState('')

    const h = Number(hours)
    const hoursValid = Number.isFinite(h) && h >= 0.5 && h <= 24 && Number.isInteger(h * 2)
    const dateValid = /^\d{4}-\d{2}-\d{2}$/.test(date) && date <= today

    const log = useMutation({
        mutationFn: () => logTaskTime(task.id, { date, hours: h, notes: notes.trim() || null }),
        onSuccess: (result) => {
            setAnchor(null)
            onLogged(result, h, date)
        },
    })

    const open = (e: React.MouseEvent<HTMLElement>) => {
        setDate(today); setHours('1'); setNotes(''); log.reset()
        setAnchor(e.currentTarget)
    }

    return (
        <>
            <Box
                component="button"
                type="button"
                onClick={open}
                sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                    bgcolor: 'background.paper', color: 'text.primary',
                    border: '1px solid', borderColor: 'divider', borderRadius: '6px', px: dense ? '8px' : '10px', py: '6px',
                    fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                    '&:hover': { borderColor: 'primary.main', color: 'primary.main' },
                }}
            >
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>⏱</Box>Log time
            </Box>
            <Popover
                open={anchor != null}
                anchorEl={anchor}
                onClose={() => { if (!log.isPending) setAnchor(null) }}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
                slotProps={{ paper: { sx: { p: 2, width: 300, borderRadius: '10px', mt: '4px' } } }}
            >
                <Stack spacing={1.5} component="form" onSubmit={(e) => { e.preventDefault(); if (hoursValid && dateValid) log.mutate() }}>
                    {log.error && <Alert severity="error" sx={{ fontSize: 12 }}>{getApiErrorMessage(log.error, 'The time could not be logged.')}</Alert>}
                    <Stack direction="row" spacing={1}>
                        <TextField
                            label="Date" type="date" size="small" value={date}
                            onChange={(e) => setDate(e.target.value)}
                            error={!dateValid}
                            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
                            sx={{ flex: 1 }}
                        />
                        <TextField
                            label="Hours" type="number" size="small" value={hours}
                            onChange={(e) => setHours(e.target.value)}
                            error={!hoursValid}
                            slotProps={{ htmlInput: { step: 0.5, min: 0.5, max: 24 } }}
                            sx={{ width: 90 }}
                        />
                    </Stack>
                    {/* The entry's note holds 300; a second log on the same day appends, which the server checks. */}
                    <TextField
                        label="Description / Notes" size="small" value={notes} onChange={(e) => setNotes(e.target.value)}
                        slotProps={{ htmlInput: { maxLength: 300 } }}
                    />
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                        <Button size="small" onClick={() => setAnchor(null)} disabled={log.isPending}>Cancel</Button>
                        <Button size="small" variant="contained" type="submit" disabled={log.isPending || !hoursValid || !dateValid}>Log</Button>
                    </Stack>
                </Stack>
            </Popover>
        </>
    )
}
