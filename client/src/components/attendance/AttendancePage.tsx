import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { getAttendanceHistory } from '../../lib/api'
import {
    formatElapsed,
    formatTime,
    useAttendanceActions,
    useAttendanceToday,
    useLiveElapsedMinutes,
} from '../../lib/hooks/useAttendance'
import type { AttendanceEvent, AttendanceEventType } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'

const BLUE = 'primary.main'
const GREEN = 'success.main'
const AMBER = 'warning.main'
const RED = 'error.main'

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
    borderBottom: '1px solid #F3F4F6',
}

const EVENT_LABEL: Record<AttendanceEventType, string> = {
    'check-in': 'Checked in',
    'check-out': 'Checked out',
    'break-start': 'Started break',
    'break-end': 'Back from break',
    'auto-break-start': 'Went idle',
    'auto-break-end': 'Back from idle',
}

const EVENT_ICON: Record<AttendanceEventType, string> = {
    'check-in': '🟢',
    'check-out': '🔴',
    'break-start': '☕',
    'break-end': '🟢',
    'auto-break-start': '💤',
    'auto-break-end': '🟢',
}

function StatusPill({ kind, children }: { kind: 'in' | 'out' | 'break' | 'late' | 'leave'; children: React.ReactNode }) {
    const styles: Record<string, { bg: SxColor; color: string; dot: string }> = {
        in:    { bg: softBg('success'), color: 'success.dark', dot: GREEN },
        out:   { bg: 'divider', color: 'text.secondary', dot: 'text.disabled' },
        break: { bg: softBg('warning'), color: 'warning.dark', dot: AMBER },
        late:  { bg: softBg('error'), color: 'error.dark', dot: RED },
        leave: { bg: softBg('info'), color: 'info.dark', dot: BLUE },
    }
    const s = styles[kind]
    return (
        <Box component="span" sx={{
            display: 'inline-flex', alignItems: 'center', gap: 0.625,
            px: 1.1, py: '3px',
            borderRadius: 12,
            fontSize: 11, fontWeight: 500,
            bgcolor: s.bg, color: s.color,
        }}>
            <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: s.dot }} />
            {children}
        </Box>
    )
}

function formatHistoryDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function fmtHm(minutes: number | null) {
    if (minutes == null) return '—'
    return formatElapsed(minutes)
}

export default function AttendancePage() {
    const { data: today, isLoading } = useAttendanceToday()
    const { checkIn, checkOut, startBreak, endBreak, anyPending } = useAttendanceActions()
    const elapsed = useLiveElapsedMinutes(today)
    const { data: history = [], isLoading: isLoadingHistory } = useQuery({
        queryKey: ['attendance', 'history', 30],
        queryFn: () => getAttendanceHistory(30),
    })

    const status = today?.status ?? 'out'
    const isOut = status === 'out' || status === 'done'
    const isBreak = status === 'break'

    const stateLabel = isOut ? 'Not checked in' : isBreak ? 'On break' : 'Working'
    const elapsedDisplay = isOut && status !== 'done' ? '—' : formatElapsed(elapsed)

    const thisWeekMinutes = history.slice(-7).reduce((s, d) => s + d.workedMinutes, 0)
    const daysLoggedThisWeek = history.slice(-7).filter((d) => d.workedMinutes > 0).length
    const thisMonthMinutes = history.reduce((s, d) => s + d.workedMinutes, 0)
    const monthDays = history.filter((d) => d.workedMinutes > 0).length
    const monthAvg = monthDays > 0 ? thisMonthMinutes / monthDays : 0

    const todayHeader = useMemo(
        () => new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' }),
        [],
    )

    const events: AttendanceEvent[] = today?.events ?? []

    return (
        <Stack spacing={2}>
            {/* Top row: clock card + 2 stats */}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.75 }}>
                <Box sx={(theme) => ({
                    gridColumn: 'span 2',
                    background: theme.palette.mode === 'dark'
                        ? 'linear-gradient(135deg, #1e3a8a 0%, #0f172a 100%)'
                        : 'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',
                    color: '#fff',
                    borderRadius: '10px',
                    p: '18px 20px',
                })}>
                    <Typography sx={{ fontSize: 12, opacity: 0.85, mb: 0.75 }}>
                        {stateLabel}{!isOut && today?.checkInAt ? ` since ${formatTime(today.checkInAt)}` : ''}
                    </Typography>
                    <Typography sx={{ fontSize: 28, fontWeight: 700, lineHeight: 1, fontVariantNumeric: 'tabular-nums' }}>
                        {isLoading && !today ? '—' : elapsedDisplay}
                    </Typography>
                    <Typography sx={{ fontSize: 11, opacity: 0.85, mt: 0.5 }}>
                        {isOut
                            ? (status === 'done' ? 'Day complete' : 'Click below to start your day')
                            : isBreak
                                ? 'On break — resume when ready'
                                : 'Hours worked today (breaks excluded)'}
                    </Typography>
                    <Stack direction="row" spacing={1} sx={{ mt: 1.75 }}>
                        {status === 'out' && (
                            <ClockButton
                                variant="primary"
                                onClick={() => checkIn.mutate()}
                                disabled={anyPending}
                                pending={checkIn.isPending}
                            >
                                ▶ Check In
                            </ClockButton>
                        )}
                        {status === 'done' && (
                            <Typography sx={{ fontSize: 12, opacity: 0.85 }}>You've checked out for today.</Typography>
                        )}
                        {status === 'in' && (
                            <>
                                <ClockButton
                                    variant="ghost"
                                    onClick={() => startBreak.mutate()}
                                    disabled={anyPending}
                                    pending={startBreak.isPending}
                                >
                                    ☕ Break
                                </ClockButton>
                                <ClockButton
                                    variant="primary"
                                    onClick={() => checkOut.mutate()}
                                    disabled={anyPending}
                                    pending={checkOut.isPending}
                                >
                                    Check Out
                                </ClockButton>
                            </>
                        )}
                        {status === 'break' && (
                            <>
                                <ClockButton
                                    variant="primary"
                                    onClick={() => endBreak.mutate()}
                                    disabled={anyPending}
                                    pending={endBreak.isPending}
                                >
                                    ▶ Resume
                                </ClockButton>
                                <ClockButton
                                    variant="ghost"
                                    onClick={() => checkOut.mutate()}
                                    disabled={anyPending}
                                    pending={checkOut.isPending}
                                >
                                    Check Out
                                </ClockButton>
                            </>
                        )}
                    </Stack>
                </Box>

                <StatCard
                    accent={GREEN}
                    label="This Week"
                    value={formatElapsed(thisWeekMinutes)}
                    sub={`${daysLoggedThisWeek} day${daysLoggedThisWeek === 1 ? '' : 's'} logged`}
                    caption="Time clocked in — separate from your logged timesheet hours"
                />
                <StatCard
                    accent={BLUE}
                    label="Last 30 days"
                    value={formatElapsed(thisMonthMinutes)}
                    sub={monthDays > 0 ? `avg ${formatElapsed(Math.round(monthAvg))}/day` : 'no days yet'}
                />
            </Box>

            {/* Two-up: today's sessions + this week's hours */}
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.75 }}>
                <Card title={`Today · ${todayHeader}`}>
                    <Stack spacing={1}>
                        {events.length === 0 ? (
                            <Box sx={{ textAlign: 'center', py: 4 }}>
                                <Typography sx={{ fontSize: 28 }}>⏰</Typography>
                                <Typography sx={{ fontSize: 13, color: 'text.disabled' }}>No activity yet today</Typography>
                            </Box>
                        ) : (
                            events.map((e, idx) => {
                                const isLast = idx === events.length - 1
                                const isActive = isLast && !isOut
                                return (
                                    <Box key={e.id} sx={{
                                        display: 'grid',
                                        gridTemplateColumns: '32px 1fr auto',
                                        gap: 1.5, alignItems: 'center',
                                        p: '10px 12px',
                                        bgcolor: isActive ? softBg('success') : 'action.hover',
                                        borderLeft: isActive ? `3px solid ${GREEN}` : 'none',
                                        borderRadius: '6px',
                                        fontSize: 12,
                                    }}>
                                        <Box sx={{
                                            width: 28, height: 28, borderRadius: '6px', bgcolor: 'background.paper',
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            fontSize: 13,
                                        }}>
                                            {EVENT_ICON[e.type]}
                                        </Box>
                                        <Box>
                                            <Typography sx={{
                                                fontSize: 12, fontWeight: 600, color: 'text.primary',
                                                fontVariantNumeric: 'tabular-nums',
                                            }}>
                                                {formatTime(e.at)}
                                            </Typography>
                                            <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                                                {EVENT_LABEL[e.type]}
                                            </Typography>
                                        </Box>
                                        <Box />
                                    </Box>
                                )
                            })
                        )}
                    </Stack>
                    <Box sx={{
                        mt: 1.75, pt: 1.75,
                        borderTop: '1px solid #F3F4F6',
                        display: 'flex', justifyContent: 'space-between',
                        fontSize: 12, color: 'text.secondary',
                    }}>
                        <span>
                            Total break time:{' '}
                            <Box component="strong" sx={{ color: 'text.primary' }}>
                                {today?.totalBreakMinutes ?? 0} min
                            </Box>
                        </span>
                        <span>
                            Productive hours:{' '}
                            <Box component="strong" sx={{ color: 'text.primary' }}>
                                {elapsedDisplay}
                            </Box>
                        </span>
                    </Box>
                </Card>

                <Card title="This Week's Hours">
                    <Stack spacing={1.25}>
                        {history.slice(-7).map((day) => {
                            const dayName = new Date(day.date).toLocaleDateString('en-GB', { weekday: 'long' })
                            const isInProgress = day.status === 'in-progress'
                            const isToday = day.date === today?.date
                            const pct = Math.min(100, (day.workedMinutes / (8 * 60)) * 100)
                            const barColor = day.workedMinutes === 0
                                ? 'divider'
                                : isInProgress ? BLUE : GREEN
                            return (
                                <Box key={day.date}>
                                    <Stack direction="row" justifyContent="space-between" sx={{ mb: 0.5 }}>
                                        <Stack direction="row" alignItems="center" spacing={0.75}>
                                            <Typography sx={{ fontSize: 12, color: day.workedMinutes === 0 && !isToday ? 'text.disabled' : 'text.primary' }}>
                                                {dayName}
                                            </Typography>
                                            {isToday && (
                                                <Box sx={{
                                                    bgcolor: '#DBEAFE', color: 'info.dark',
                                                    fontSize: 10, fontWeight: 500,
                                                    px: 0.75, py: '1px',
                                                    borderRadius: 10,
                                                }}>
                                                    Today
                                                </Box>
                                            )}
                                        </Stack>
                                        <Typography sx={{ fontSize: 12, color: day.workedMinutes === 0 && !isToday ? 'text.disabled' : 'text.primary' }}>
                                            {day.workedMinutes === 0 && !isToday
                                                ? '—'
                                                : `${formatElapsed(day.workedMinutes)}${isInProgress ? ' · ongoing' : ''}`}
                                        </Typography>
                                    </Stack>
                                    <Box sx={{ height: 6, bgcolor: 'divider', borderRadius: 3, overflow: 'hidden' }}>
                                        <Box sx={{ height: '100%', width: `${pct}%`, bgcolor: barColor, borderRadius: 3 }} />
                                    </Box>
                                </Box>
                            )
                        })}
                        {history.length === 0 && !isLoadingHistory && (
                            <Typography sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: 2 }}>
                                No history yet — check in to start.
                            </Typography>
                        )}
                    </Stack>
                </Card>
            </Box>

            {/* History table */}
            <Card
                title="Attendance History"
                action={
                    <Select size="small" value="30" sx={{ fontSize: 12, '& .MuiSelect-select': { py: 0.5, px: 1.25 } }}>
                        <MenuItem value="30">Last 30 days</MenuItem>
                    </Select>
                }
                noPadding
            >
                <Box sx={{ overflowX: 'auto' }}>
                    <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                        <TableHead>
                            <TableRow>
                                <TableCell sx={TH}>Date</TableCell>
                                <TableCell sx={TH}>Check In</TableCell>
                                <TableCell sx={TH}>Check Out</TableCell>
                                <TableCell sx={TH}>Break</TableCell>
                                <TableCell sx={TH}>Hours</TableCell>
                                <TableCell sx={TH}>Status</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {isLoadingHistory ? (
                                <TableRow>
                                    <TableCell sx={TD} colSpan={6} align="center">
                                        <CircularProgress size={20} />
                                    </TableCell>
                                </TableRow>
                            ) : history.length === 0 ? (
                                <TableRow>
                                    <TableCell sx={{ ...TD, textAlign: 'center', color: 'text.disabled' }} colSpan={6}>
                                        No history yet.
                                    </TableCell>
                                </TableRow>
                            ) : (
                                history.slice().reverse().map((d) => (
                                    <TableRow key={d.date} sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}>
                                        <TableCell sx={TD}>
                                            <Box component="strong" sx={{ fontWeight: d.date === today?.date ? 700 : 500 }}>
                                                {formatHistoryDate(d.date)}
                                            </Box>
                                        </TableCell>
                                        <TableCell sx={TD}>{d.checkInAt ? formatTime(d.checkInAt) : '—'}</TableCell>
                                        <TableCell sx={TD}>{d.checkOutAt ? formatTime(d.checkOutAt) : '—'}</TableCell>
                                        <TableCell sx={TD}>{d.totalBreakMinutes > 0 ? `${d.totalBreakMinutes} min` : '—'}</TableCell>
                                        <TableCell sx={TD}>{fmtHm(d.workedMinutes > 0 ? d.workedMinutes : null)}</TableCell>
                                        <TableCell sx={TD}>
                                            {d.status === 'in-progress'
                                                ? <StatusPill kind="in">In progress</StatusPill>
                                                : d.status === 'complete'
                                                    ? <StatusPill kind="in">Complete</StatusPill>
                                                    : d.status === 'late'
                                                        ? <StatusPill kind="late">Late arrival</StatusPill>
                                                        : <StatusPill kind="out">No record</StatusPill>}
                                        </TableCell>
                                    </TableRow>
                                ))
                            )}
                        </TableBody>
                    </Table>
                </Box>
            </Card>
        </Stack>
    )
}

function ClockButton({
    onClick,
    disabled,
    pending,
    variant,
    children,
}: {
    onClick: () => void
    disabled: boolean
    pending: boolean
    variant: 'primary' | 'ghost'
    children: React.ReactNode
}) {
    const sx = variant === 'primary'
        ? { bgcolor: 'background.paper', color: 'info.dark', '&:hover': { bgcolor: softBg('info') } }
        : { bgcolor: 'rgba(255,255,255,0.15)', color: '#fff', border: '1px solid rgba(255,255,255,0.3)', '&:hover': { bgcolor: 'rgba(255,255,255,0.25)' } }
    return (
        <Button
            onClick={onClick}
            disabled={disabled}
            startIcon={pending ? <CircularProgress size={12} color="inherit" /> : null}
            sx={{
                textTransform: 'none',
                fontSize: 12, fontWeight: 500,
                px: 1.5, py: 1,
                flex: 1,
                boxShadow: 'none',
                ...sx,
            }}
        >
            {children}
        </Button>
    )
}

function StatCard({ accent, label, value, sub, caption }: { accent: string; label: string; value: string; sub: string; caption?: string }) {
    return (
        <Paper elevation={0} sx={{
            bgcolor: 'background.paper',
            border: '1px solid', borderColor: 'divider',
            borderTop: `3px solid ${accent}`,
            borderRadius: '10px',
            p: '18px 20px',
        }}>
            <Typography sx={{ fontSize: 12, color: 'text.secondary', mb: 1 }}>{label}</Typography>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>{value}</Typography>
            <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.5 }}>{sub}</Typography>
            {caption && (
                <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.75, lineHeight: 1.4 }}>
                    {caption}
                </Typography>
            )}
        </Paper>
    )
}

function Card({
    title,
    action,
    children,
    noPadding,
}: {
    title: string
    action?: React.ReactNode
    children: React.ReactNode
    noPadding?: boolean
}) {
    return (
        <Paper elevation={0} sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
            <Box sx={{
                p: '14px 18px',
                borderBottom: '1px solid', borderColor: 'divider',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            }}>
                <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>{title}</Typography>
                {action}
            </Box>
            <Box sx={{ p: noPadding ? 0 : '18px' }}>{children}</Box>
        </Paper>
    )
}
