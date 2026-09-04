import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
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
import { getTeamAttendance } from '../../lib/api'
import { formatElapsed, formatTime } from '../../lib/hooks/useAttendance'
import type { TeamMemberAttendance, TeamMemberStatus } from '../../lib/types'
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

function initials(name: string) {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('')
}

function StatusPill({ kind, children }: { kind: TeamMemberStatus | 'late'; children: React.ReactNode }) {
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

function StatCard({ accent, label, value, sub }: { accent: string; label: string; value: number | string; sub: string }) {
    return (
        <Paper elevation={0} sx={{
            bgcolor: 'background.paper',
            border: '1px solid', borderColor: 'divider',
            borderTop: `3px solid ${accent}`,
            borderRadius: '10px',
            p: '18px 20px',
        }}>
            <Typography sx={{ fontSize: 12, color: 'text.secondary', mb: 1 }}>{label}</Typography>
            <Typography sx={{ fontSize: 26, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>{value}</Typography>
            <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.5 }}>{sub}</Typography>
        </Paper>
    )
}

function TeamMemberCard({ m }: { m: TeamMemberAttendance }) {
    const leftColor = m.status === 'in' ? GREEN : m.status === 'break' ? AMBER : 'divider'
    const pillLabel = m.status === 'in' ? 'Working'
        : m.status === 'break' ? (m.isAutoBreak ? 'Idle' : 'On break')
        : m.status === 'leave' ? 'On leave'
        : 'Not in'

    return (
        <Box sx={{
            bgcolor: 'background.paper',
            border: '1px solid', borderColor: 'divider',
            borderLeftWidth: 3,
            borderLeftColor: leftColor,
            borderRadius: '8px',
            p: '12px 14px',
        }}>
            <Stack direction="row" alignItems="center" spacing={1.25} sx={{ mb: 1 }}>
                <Box sx={{
                    width: 32, height: 32, borderRadius: '50%',
                    bgcolor: BLUE, color: '#fff',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    fontSize: 11, fontWeight: 600, flexShrink: 0,
                }}>
                    {initials(m.employeeName)}
                </Box>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {m.employeeName}
                    </Typography>
                    <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                        {m.jobTitle || m.departmentName}
                    </Typography>
                </Box>
                <StatusPill kind={m.status}>{pillLabel}</StatusPill>
            </Stack>
            <Box sx={{ fontSize: 11, color: 'text.secondary' }}>
                {m.status === 'out' || m.status === 'leave' ? (
                    <span>{m.todayNote}</span>
                ) : (
                    <>
                        <span>
                            In at{' '}
                            <Box component="strong" sx={{ color: 'text.primary', fontVariantNumeric: 'tabular-nums' }}>
                                {m.checkInAt ? formatTime(m.checkInAt) : '—'}
                            </Box>
                            {' '}·{' '}
                            <Box component="strong" sx={{ color: 'text.primary', fontVariantNumeric: 'tabular-nums' }}>
                                {formatElapsed(m.workedMinutes)}
                            </Box>{' worked'}
                        </span>
                        <Box sx={{ fontSize: 10, color: 'text.disabled', mt: 0.25 }}>{m.todayNote}</Box>
                    </>
                )}
            </Box>
        </Box>
    )
}

export default function TeamAttendancePage() {
    const { data, isLoading } = useQuery({
        queryKey: ['attendance', 'team'],
        queryFn: getTeamAttendance,
        refetchInterval: 30_000,
    })

    if (isLoading && !data) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                <CircularProgress size={24} />
            </Box>
        )
    }

    const members = data?.members ?? []
    const week = data?.week ?? []

    const inCount = members.filter((m) => m.status === 'in').length
    const breakCount = members.filter((m) => m.status === 'break').length
    const outCount = members.filter((m) => m.status === 'out').length
    const leaveCount = members.filter((m) => m.status === 'leave').length

    return (
        <Stack spacing={2}>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.75 }}>
                <StatCard accent={GREEN} label="Working now" value={inCount} sub={`of ${members.length} team members`} />
                <StatCard accent={AMBER} label="On break" value={breakCount} sub="team members" />
                <StatCard accent={BLUE} label="On leave" value={leaveCount} sub="today" />
                <StatCard accent={RED} label="Not checked in" value={outCount} sub={outCount === 0 ? 'all accounted for' : 'no check-in today'} />
            </Box>

            <Paper elevation={0} sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{ p: '14px 18px', borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Live · Team</Typography>
                    <Stack direction="row" alignItems="center" spacing={0.625}>
                        <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: GREEN }} />
                        <Typography sx={{ fontSize: 11, color: GREEN }}>Auto-refreshing</Typography>
                    </Stack>
                </Box>
                <Box sx={{ p: 2.25 }}>
                    {members.length === 0 ? (
                        <Typography sx={{ fontSize: 13, color: 'text.disabled', textAlign: 'center', py: 4 }}>
                            No team members.
                        </Typography>
                    ) : (
                        <Box sx={{
                            display: 'grid',
                            gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
                            gap: 1.5,
                        }}>
                            {members.map((m) => <TeamMemberCard key={m.employeeId} m={m} />)}
                        </Box>
                    )}
                </Box>
            </Paper>

            {week.length > 0 && (
                <Paper elevation={0} sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                    <Box sx={{ p: '14px 18px', borderBottom: '1px solid', borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                        <Box>
                            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Weekly Attendance Log</Typography>
                            <Typography sx={{ fontSize: 11, color: 'text.disabled', mt: 0.25, lineHeight: 1.4 }}>
                                Time clocked in — separate from the hours your team logs on timesheets
                            </Typography>
                        </Box>
                        <Select size="small" value="this-week" sx={{ fontSize: 12, '& .MuiSelect-select': { py: 0.5, px: 1.25 } }}>
                            <MenuItem value="this-week">This week</MenuItem>
                        </Select>
                    </Box>
                    <Box sx={{ overflowX: 'auto' }}>
                        <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell sx={TH}>Employee</TableCell>
                                    {week[0].days.map((d, idx) => {
                                        const dt = new Date(d.date)
                                        const label = dt.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric' })
                                        return <TableCell key={idx} sx={TH}>{label}</TableCell>
                                    })}
                                    <TableCell sx={TH}>Total</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {week.map((row) => (
                                    <TableRow key={row.employeeId} sx={{ '&:last-child td': { borderBottom: 'none' }, '&:hover td': { bgcolor: 'action.hover' } }}>
                                        <TableCell sx={TD}>
                                            <Box component="strong">{row.employeeName}</Box>
                                        </TableCell>
                                        {row.days.map((d, idx) => (
                                            <TableCell key={idx} sx={TD}>
                                                {d.workedMinutes == null
                                                    ? <Box component="span" sx={{ color: 'text.disabled' }}>—</Box>
                                                    : (
                                                        <>
                                                            {formatElapsed(d.workedMinutes)}
                                                            {d.note === 'in' && <Box component="span" sx={{ color: 'text.secondary', fontSize: 11, ml: 0.5 }}>(in)</Box>}
                                                            {d.note === 'break' && <Box component="span" sx={{ color: AMBER, fontSize: 11, ml: 0.5 }}>(break)</Box>}
                                                        </>
                                                    )}
                                            </TableCell>
                                        ))}
                                        <TableCell sx={TD}>
                                            <Box component="strong">{formatElapsed(row.totalMinutes)}</Box>
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </Box>
                </Paper>
            )}
        </Stack>
    )
}
