import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TextField from '@mui/material/TextField'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { getCompanyAttendance } from '../../lib/api'
import { activityIcon, formatElapsed, formatTime } from '../../lib/hooks/useAttendance'
import type { RecentActivity } from '../../lib/types'
import { softBg } from '../../lib/theme-tokens'

const BLUE = 'primary.main'
const GREEN = 'success.main'
const AMBER = 'warning.main'
const RED = 'error.main'

// Not-checked-in is stated, not diagnosed. At the late-check-in hour the data
// cannot separate an unscheduled absence from someone who has not arrived yet,
// so the count and the feed rows both read neutral — the same grey the admin
// dashboard gives its "Not in" segment and its not-checked-in activity rows.
const NEUTRAL = 'text.disabled'

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

function StatCard({
    accent,
    icon,
    label,
    value,
    sub,
}: {
    accent: string
    icon: string
    label: string
    value: number | string
    sub: string
}) {
    return (
        <Paper elevation={0} sx={{
            bgcolor: 'background.paper',
            border: '1px solid', borderColor: 'divider',
            borderTop: `3px solid ${accent}`,
            borderRadius: '10px',
            p: '18px 20px',
        }}>
            <Stack direction="row" alignItems="center" spacing={0.75} sx={{ mb: 1 }}>
                <Typography sx={{ fontSize: 14 }}>{icon}</Typography>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{label}</Typography>
            </Stack>
            <Typography sx={{ fontSize: 26, fontWeight: 700, color: 'text.primary', lineHeight: 1 }}>
                {value}
            </Typography>
            <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.5 }}>{sub}</Typography>
        </Paper>
    )
}

function ActivityRow({ r }: { r: RecentActivity }) {
    return (
        <Box sx={{
            display: 'grid',
            gridTemplateColumns: '32px 1fr auto',
            gap: 1.5, alignItems: 'center',
            p: '10px 12px',
            bgcolor: 'action.hover',
            borderRadius: '6px',
            fontSize: 12,
        }}>
            <Box sx={{
                width: 28, height: 28, borderRadius: '6px', bgcolor: 'background.paper',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 13,
            }}>
                {activityIcon(r.action)}
            </Box>
            <Box>
                <Typography sx={{ fontSize: 12, fontWeight: 600, color: 'text.primary' }}>
                    {r.employeeName}{' '}
                    <Box component="span" sx={{ color: 'text.disabled', fontWeight: 400, fontSize: 11 }}>
                        · {r.departmentName}
                    </Box>
                </Typography>
                <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                    {r.action}{r.at ? ` at ${formatTime(r.at)}` : ''}
                </Typography>
            </Box>
            <Typography sx={{ fontSize: 11, color: 'text.secondary', fontVariantNumeric: 'tabular-nums' }}>
                {r.minutesAgo == null
                    ? '—'
                    : r.minutesAgo < 1
                        ? 'just now'
                        : r.minutesAgo < 60
                            ? `${r.minutesAgo} min ago`
                            : formatElapsed(r.minutesAgo) + ' ago'}
            </Typography>
        </Box>
    )
}

function progressColor(pct: number) {
    return pct >= 80 ? GREEN : pct >= 60 ? AMBER : RED
}

function minutesToHours(min: number) {
    return (min / 60).toFixed(1)
}

function csvEscape(value: string): string {
    if (/[",\n\r]/.test(value)) {
        return `"${value.replace(/"/g, '""')}"`
    }
    return value
}

function exportRecentToCsv(recent: RecentActivity[]) {
    const header = ['Employee', 'Department', 'Action', 'Time', 'Minutes ago']
    const rows = recent.map((r) => [
        csvEscape(r.employeeName),
        csvEscape(r.departmentName),
        csvEscape(r.action),
        r.at ? csvEscape(new Date(r.at).toISOString()) : '',
        r.minutesAgo == null ? '' : String(r.minutesAgo),
    ].join(','))
    const csv = [header.join(','), ...rows].join('\r\n')

    const blob = new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    const today = new Date().toISOString().split('T')[0]
    a.href = url
    a.download = `company-attendance-${today}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
}

// The order the action select lists in — chronological through a working day,
// with the timestamp-less synthetic row last. Actions the feed does not contain
// are left out, so the list stays as short as the day was.
const ACTION_ORDER = [
    'Checked in',
    'Late check-in',
    'Started break',
    'Back from break',
    'Went idle',
    'Back from idle',
    'Checked out',
    'Not checked in',
]

const TIME_WINDOWS: { value: string; label: string; minutes: number | null }[] = [
    { value: 'all', label: 'All of today', minutes: null },
    { value: '1h', label: 'Last hour', minutes: 60 },
    { value: '4h', label: 'Last 4 hours', minutes: 240 },
]

const FILTER_SELECT_SX = {
    fontSize: 12,
    '& .MuiSelect-select': { py: '7px', px: '12px' },
    '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
}

export default function CompanyAttendancePage() {
    const { data, isLoading } = useQuery({
        queryKey: ['attendance', 'company'],
        queryFn: getCompanyAttendance,
        refetchInterval: 30_000,
    })

    const [search, setSearch] = useState('')
    const [deptFilter, setDeptFilter] = useState('all')
    const [actionFilter, setActionFilter] = useState('all')
    const [windowFilter, setWindowFilter] = useState('all')

    const recent = useMemo(() => data?.recent ?? [], [data])

    // Both option lists come from the feed rather than a fixed catalogue, so an
    // admin is never offered a filter that can only return nothing.
    const deptOptions = useMemo(
        () => Array.from(new Set(recent.map((r) => r.departmentName))).sort(),
        [recent],
    )

    const actionOptions = useMemo(() => {
        const present = new Set(recent.map((r) => r.action))
        return ACTION_ORDER.filter((a) => present.has(a))
    }, [recent])

    const filteredRecent = useMemo(() => {
        let list = recent

        if (deptFilter !== 'all') list = list.filter((r) => r.departmentName === deptFilter)
        if (actionFilter !== 'all') list = list.filter((r) => r.action === actionFilter)

        const window = TIME_WINDOWS.find((w) => w.value === windowFilter)?.minutes ?? null
        if (window != null) {
            // A "Not checked in" row has no timestamp and so happened inside no
            // window; narrowing to one drops it rather than keeping a row the
            // filter cannot place in time.
            list = list.filter((r) => r.minutesAgo != null && r.minutesAgo <= window)
        }

        const q = search.trim().toLowerCase()
        if (q) list = list.filter((r) => r.employeeName.toLowerCase().includes(q))

        return list
    }, [recent, deptFilter, actionFilter, windowFilter, search])

    const filtersActive = search.trim() !== ''
        || deptFilter !== 'all'
        || actionFilter !== 'all'
        || windowFilter !== 'all'

    if (isLoading && !data) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                <CircularProgress size={24} />
            </Box>
        )
    }

    if (!data) return null

    const inPct = data.total > 0 ? Math.round((data.in / data.total) * 100) : 0

    return (
        <Stack spacing={2}>
            {/* Top-level org stats */}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.75 }}>
                <StatCard
                    accent={GREEN}
                    icon="🟢"
                    label="Working Now"
                    value={data.in}
                    sub={`of ${data.total} employees · ${inPct}%`}
                />
                <StatCard
                    accent={AMBER}
                    icon="☕"
                    label="On Break"
                    value={data.break}
                    sub="employees"
                />
                <StatCard
                    accent={NEUTRAL}
                    icon="⚪"
                    label="Not Checked In"
                    value={data.out}
                    sub={data.out > 0 ? 'no check-in recorded yet' : 'all in'}
                />
                <StatCard
                    accent={BLUE}
                    icon="📅"
                    label="On Leave"
                    value={data.leave}
                    sub="today"
                />
            </Box>

            {/* Department breakdown */}
            <Paper elevation={0} sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{
                    p: '14px 18px',
                    borderBottom: '1px solid', borderColor: 'divider',
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>By Department</Typography>
                    <Stack direction="row" alignItems="center" spacing={0.625}>
                        <Box sx={{
                            width: 6, height: 6, borderRadius: '50%', bgcolor: GREEN,
                            animation: 'pulse 2s infinite',
                            '@keyframes pulse': {
                                '0%,100%': { opacity: 1 },
                                '50%': { opacity: 0.6 },
                            },
                        }} />
                        <Typography sx={{ fontSize: 11, color: GREEN }}>Live · auto-refreshing</Typography>
                    </Stack>
                </Box>
                <Box sx={{ overflowX: 'auto' }}>
                    <Table sx={{ width: '100%', borderCollapse: 'collapse' }}>
                        <TableHead>
                            <TableRow>
                                <TableCell sx={TH}>Department</TableCell>
                                <TableCell sx={TH}>Status</TableCell>
                                <TableCell sx={TH}>Working</TableCell>
                                <TableCell sx={TH}>Break</TableCell>
                                <TableCell sx={TH}>Off</TableCell>
                                <TableCell sx={TH}>Leave</TableCell>
                                <TableCell sx={TH}>Hrs Today</TableCell>
                                <TableCell sx={TH}>Avg/Person</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {data.departments.map((d) => {
                                const pct = d.total > 0 ? (d.in / d.total) * 100 : 0
                                return (
                                    <TableRow key={d.name} sx={{ '&:hover td': { bgcolor: 'action.hover' } }}>
                                        <TableCell sx={TD}>
                                            <Box component="strong">{d.name}</Box>{' '}
                                            <Box component="span" sx={{ color: 'text.disabled', fontSize: 11 }}>({d.total})</Box>
                                        </TableCell>
                                        <TableCell sx={{ ...TD, minWidth: 160 }}>
                                            <Box sx={{ height: 6, bgcolor: 'divider', borderRadius: 3, overflow: 'hidden' }}>
                                                <Box sx={{ height: '100%', width: `${pct}%`, bgcolor: progressColor(pct), borderRadius: 3 }} />
                                            </Box>
                                            <Typography sx={{ fontSize: 10, color: 'text.secondary', mt: '3px' }}>
                                                {Math.round(pct)}% in
                                            </Typography>
                                        </TableCell>
                                        <TableCell sx={TD}>
                                            <Box component="strong" sx={{ color: GREEN }}>{d.in}</Box>
                                        </TableCell>
                                        <TableCell sx={TD}>
                                            {d.break > 0
                                                ? <Box component="strong" sx={{ color: AMBER }}>{d.break}</Box>
                                                : <Box component="span" sx={{ color: 'text.disabled' }}>0</Box>}
                                        </TableCell>
                                        <TableCell sx={TD}>
                                            {d.out > 0
                                                ? <Box component="strong" sx={{ color: 'text.primary' }}>{d.out}</Box>
                                                : <Box component="span" sx={{ color: 'text.disabled' }}>0</Box>}
                                        </TableCell>
                                        <TableCell sx={TD}>
                                            {d.leave > 0
                                                ? <Box component="strong" sx={{ color: BLUE }}>{d.leave}</Box>
                                                : <Box component="span" sx={{ color: 'text.disabled' }}>0</Box>}
                                        </TableCell>
                                        <TableCell sx={TD}>
                                            <Box component="strong">{minutesToHours(d.totalMinutes)}</Box>
                                        </TableCell>
                                        <TableCell sx={TD}>{minutesToHours(d.avgMinutes)} h</TableCell>
                                    </TableRow>
                                )
                            })}
                            <TableRow sx={{ bgcolor: 'action.hover' }}>
                                <TableCell sx={{ ...TD, fontWeight: 600 }}>
                                    <Box component="strong">All departments</Box>
                                </TableCell>
                                <TableCell sx={TD}></TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong" sx={{ color: GREEN }}>{data.in}</Box>
                                </TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong">{data.break}</Box>
                                </TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong">{data.out}</Box>
                                </TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong">{data.leave}</Box>
                                </TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong">{minutesToHours(data.totalMinutesToday)}</Box>
                                </TableCell>
                                <TableCell sx={TD}>
                                    <Box component="strong">{minutesToHours(data.avgMinutesToday)} h</Box>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </Box>
            </Paper>

            {/* Recent activity. Today's issues are not repeated here: the admin
                dashboard already leads with that card (DashboardHome's
                TodaysIssuesCard), off the same query, and the four stat cards
                plus the department table above are the same day stated as
                numbers. */}
            <Paper elevation={0} sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflow: 'hidden' }}>
                <Box sx={{
                    p: '14px 18px',
                    borderBottom: '1px solid', borderColor: 'divider',
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                        Recent Activity
                        {filtersActive && (
                            <Box component="span" sx={{ fontSize: 11, fontWeight: 400, color: 'text.secondary', ml: 0.75 }}>
                                · {filteredRecent.length} of {recent.length}
                            </Box>
                        )}
                    </Typography>
                    <Button
                        variant="outlined"
                        size="small"
                        onClick={() => exportRecentToCsv(filteredRecent)}
                        disabled={filteredRecent.length === 0}
                        sx={{
                            fontSize: 12,
                            textTransform: 'none',
                            color: BLUE,
                            borderColor: BLUE,
                            px: 1.5, py: 0.5,
                            '&:hover': { bgcolor: softBg('primary'), borderColor: BLUE },
                        }}
                    >
                        Export Log
                    </Button>
                </Box>

                {/* Filter toolbar, matching the one on All Timesheets. */}
                <Stack
                    direction="row"
                    spacing={1}
                    alignItems="center"
                    flexWrap="wrap"
                    useFlexGap
                    sx={{ p: '12px 18px', borderBottom: '1px solid', borderColor: 'divider' }}
                >
                    <TextField
                        size="small"
                        placeholder="Search by name…"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        sx={{
                            minWidth: 220,
                            '& .MuiInputBase-input': { fontSize: 12, py: '7px' },
                            '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                        }}
                    />
                    <Select
                        size="small"
                        value={deptFilter}
                        onChange={(e) => setDeptFilter(e.target.value)}
                        inputProps={{ 'aria-label': 'Filter by department' }}
                        sx={FILTER_SELECT_SX}
                    >
                        <MenuItem value="all">All departments</MenuItem>
                        {deptOptions.map((d) => <MenuItem key={d} value={d}>{d}</MenuItem>)}
                    </Select>
                    <Select
                        size="small"
                        value={actionFilter}
                        onChange={(e) => setActionFilter(e.target.value)}
                        inputProps={{ 'aria-label': 'Filter by action' }}
                        sx={FILTER_SELECT_SX}
                    >
                        <MenuItem value="all">All actions</MenuItem>
                        {actionOptions.map((a) => <MenuItem key={a} value={a}>{a}</MenuItem>)}
                    </Select>
                    <Select
                        size="small"
                        value={windowFilter}
                        onChange={(e) => setWindowFilter(e.target.value)}
                        inputProps={{ 'aria-label': 'Filter by time window' }}
                        sx={FILTER_SELECT_SX}
                    >
                        {TIME_WINDOWS.map((w) => (
                            <MenuItem key={w.value} value={w.value}>{w.label}</MenuItem>
                        ))}
                    </Select>
                </Stack>

                <Box sx={{ p: 2.25 }}>
                    <Stack spacing={1}>
                        {filteredRecent.length === 0 ? (
                            <Typography sx={{ fontSize: 13, color: 'text.disabled', textAlign: 'center', py: 3 }}>
                                {filtersActive ? 'No activity matches the filters.' : 'No activity yet today.'}
                            </Typography>
                        ) : (
                            filteredRecent.map((r, idx) => <ActivityRow key={`${r.employeeName}-${idx}`} r={r} />)
                        )}
                    </Stack>
                </Box>
            </Paper>
        </Stack>
    )
}
