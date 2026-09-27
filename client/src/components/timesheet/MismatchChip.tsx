import Box from '@mui/material/Box'
import { describeMismatchCount } from '../../lib/daily-hours'
import { softBg } from '../../lib/theme-tokens'

/*
 * "2 days don't match attendance" — how many weekdays of a timesheet disagree with
 * the time attendance recorded, as the server judged them (DailyHoursRule). A flag
 * for the reviewer; nothing about submitting or approving depends on it.
 */
export default function MismatchChip({ count }: { count?: number }) {
    const text = describeMismatchCount(count)
    if (!text) return null
    return (
        <Box component="span" sx={{
            display: 'inline-block',
            bgcolor: softBg('warning'), color: 'warning.dark',
            borderRadius: '4px', px: 0.75, py: '1px',
            fontSize: 11, fontWeight: 500,
        }}>
            {text}
        </Box>
    )
}
