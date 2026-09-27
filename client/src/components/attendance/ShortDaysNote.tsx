import Box from '@mui/material/Box'
import { describeShortDay } from '../../lib/daily-hours'
import { softBg } from '../../lib/theme-tokens'
import type { ShortDay } from '../../lib/types'

/*
 * Who came in under the day's target on the previous working day, as the server
 * judged it (DailyHoursRule, ShortDayDigest). Shared by Team Attendance and the
 * manager's dashboard; renders nothing when nobody was short or the API predates
 * the field.
 */
export default function ShortDaysNote({ date, people }: { date?: string | null; people?: ShortDay[] | null }) {
    if (!date || !people || people.length === 0) return null

    const label = new Date(`${date}T00:00:00Z`).toLocaleDateString('en-GB', {
        weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC',
    }).replace(',', '')

    return (
        <Box sx={{
            bgcolor: softBg('warning'), color: 'warning.dark',
            border: '1px solid', borderColor: 'warning.main',
            borderRadius: '8px', p: '8px 12px', fontSize: 12, lineHeight: 1.5,
        }}>
            <Box component="strong">Short days on {label}:</Box>{' '}
            {people.map((p) => `${p.employeeName} · ${describeShortDay(p.shortByMinutes)}`).join(' — ')}
        </Box>
    )
}
