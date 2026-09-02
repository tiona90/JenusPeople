import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import CircularProgress from '@mui/material/CircularProgress'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import {
    approveTimesheet,
    getDepartments,
    getProjects,
    getProjectComponents,
    getProjectTypes,
    getTimesheet,
    getTimesheets,
    rejectTimesheet,
} from '../../lib/api'
import type { Timesheet, TimesheetProjectSummary } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import { useAppSettings } from '../../lib/hooks/useAppSettings'
import { softBg, type SxColor } from '../../lib/theme-tokens'
import { RejectReasonDialog } from '../ui'
import { buildTimesheetsCsv } from './timesheet-csv'

const BLUE = 'primary.main'
const GREEN = 'success.main'
const AMBER = 'warning.main'
const RED = 'error.main'

type FilterTab = 'all' | 'pending' | 'approved' | 'rejected'

function initials(name: string) {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('')
}

function isPendingStatus(status: string) {
    return status === 'Submitted' || status === 'Resubmitted'
}

function formatSubmittedDay(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function formatSubmittedTime(iso: string) {
    return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
}

function startOfWeekIso(d: Date): string {
    const c = new Date(d)
    c.setHours(0, 0, 0, 0)
    const day = c.getDay()
    const diff = day === 0 ? -6 : 1 - day
    c.setDate(c.getDate() + diff)
    const y = c.getFullYear()
    const m = String(c.getMonth() + 1).padStart(2, '0')
    const dd = String(c.getDate()).padStart(2, '0')
    return `${y}-${m}-${dd}`
}

function formatWeekHeader(periodStartIso: string): string {
    const start = new Date(periodStartIso)
    start.setHours(0, 0, 0, 0)
    const end = new Date(start)
    end.setDate(start.getDate() + 4) // Mon-Fri for label
    const sameMonth = start.getMonth() === end.getMonth() && start.getFullYear() === end.getFullYear()
    if (sameMonth) {
        const tail = end.toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })
        return `Week of ${start.getDate()} – ${end.getDate()} ${tail}`
    }
    const fmt = (d: Date) => d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    return `Week of ${fmt(start)} – ${fmt(end)} ${end.getFullYear()}`
}

// Default timesheet policy, used until AppSettings loads (and as a fallback).
const DEFAULT_WEEKLY_TARGET = 40
const DEFAULT_DEADLINE_DAY = 'fri'
const DEFAULT_DEADLINE_TIME = '18:00'

// Week tokens in period order — periodStart is always a Monday, so the index
// is the day offset to add to reach the deadline weekday.
const DAY_OFFSET: Record<string, number> = { mon: 0, tue: 1, wed: 2, thu: 3, fri: 4, sat: 5, sun: 6 }

// The submission deadline (UTC) for a given week, derived from the configurable
// AppSettings deadline day + time.
function getDeadlineUtc(periodStart: string, deadlineDay: string, deadlineTime: string): Date {
    const offset = DAY_OFFSET[deadlineDay] ?? DAY_OFFSET[DEFAULT_DEADLINE_DAY]
    const [h, m] = deadlineTime.split(':').map(Number)
    const d = new Date(periodStart)
    d.setUTCDate(d.getUTCDate() + offset)
    d.setUTCHours(Number.isFinite(h) ? h : 18, Number.isFinite(m) ? m : 0, 0, 0)
    return d
}

function isLateSubmission(periodStart: string, submittedAt: string | null | undefined, deadlineDay: string, deadlineTime: string) {
    if (!submittedAt) return false
    return new Date(submittedAt) > getDeadlineUtc(periodStart, deadlineDay, deadlineTime)
}

function statusBadge(status: string) {
    const map: Record<string, { bg: SxColor; color: string; label: string }> = {
        Submitted:   { bg: softBg('warning'), color: 'warning.dark', label: 'Pending review' },
        Resubmitted: { bg: softBg('primary'), color: 'primary.dark', label: 'Resubmitted' },
        Approved:    { bg: softBg('success'), color: 'success.dark', label: 'Approved' },
        Rejected:    { bg: softBg('error'), color: 'error.dark', label: 'Needs changes' },
        Draft:       { bg: softBg('info'), color: 'info.dark', label: 'Draft' },
    }
    return map[status] ?? { bg: 'divider', color: 'text.secondary', label: status }
}

function StatMini({
    icon, label, value, valueColor, sub,
}: {
    icon: string
    label: string
    value: string | number
    valueColor?: string
    sub: string
}) {
    return (
        <Paper elevation={0} sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
            p: '14px 16px',
        }}>
            <Typography sx={{
                fontSize: 11, color: 'text.secondary',
                textTransform: 'uppercase', letterSpacing: '0.05em',
                mb: 0.75,
                display: 'flex', alignItems: 'center', gap: 0.75,
            }}>
                <Box component="span" sx={{ fontSize: 12 }}>{icon}</Box>
                {label}
            </Typography>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>
                {value}
            </Typography>
            <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: 0.5 }}>{sub}</Typography>
        </Paper>
    )
}

function ProjectChip({ p }: { p: TimesheetProjectSummary }) {
    return (
        <Box sx={{
            bgcolor: softBg('primary'), color: 'info.dark',
            px: 1, py: '2px',
            borderRadius: '10px',
            fontSize: 11,
            whiteSpace: 'nowrap',
        }}>
            {(p.code || p.name)} · {Number(p.hours).toFixed(1)}h
        </Box>
    )
}

function DailyBreakdown({ ts }: { ts: Timesheet }) {
    const { data, isLoading } = useQuery({
        queryKey: ['timesheet', ts.id],
        queryFn: () => getTimesheet(ts.id),
    })
    const entries = (data?.entries as TimesheetEntry[] | undefined) ?? []

    // Entries carry type and component ids, not their names. Same query keys the
    // page uses, so these are cache reads rather than second fetches.
    const { data: projectTypes = [] } = useQuery({
        queryKey: ['projectTypes'],
        queryFn: getProjectTypes,
    })
    const { data: components = [] } = useQuery({
        queryKey: ['projectComponents'],
        queryFn: getProjectComponents,
    })
    const typeById = useMemo(() => new Map(projectTypes.map((t) => [t.id, t])), [projectTypes])
    const componentById = useMemo(() => new Map(components.map((c) => [c.id, c])), [components])

    const days = useMemo(() => {
        const periodStart = new Date(ts.periodStart)
        periodStart.setHours(0, 0, 0, 0)
        const out: { date: Date; name: string; total: number; tasks: { project: string; type: string; component: string; notes: string }[] }[] = []
        for (let i = 0; i < 5; i++) {
            const d = new Date(periodStart)
            d.setDate(periodStart.getDate() + i)
            const dayName = d.toLocaleDateString('en-GB', { weekday: 'short' })
            const dayKey = d.toISOString().split('T')[0]
            const dayEntries = entries.filter((e) => e.date.split('T')[0] === dayKey)
            const total = dayEntries.reduce((s, e) => s + Number(e.hoursWorked), 0)
            const tasks = dayEntries.map((e) => ({
                project: e.project?.code ?? e.project?.name ?? `Project #${e.projectId}`,
                // Blank when the entry has none, which the render then leaves out.
                type: (e.projectTypeId != null ? typeById.get(e.projectTypeId)?.name : '') ?? '',
                component: (e.projectComponentId != null ? componentById.get(e.projectComponentId)?.name : '') ?? '',
                notes: e.notes ?? '—',
            }))
            out.push({ date: d, name: dayName, total, tasks })
        }
        return out
    }, [entries, ts.periodStart, typeById, componentById])

    if (isLoading && entries.length === 0) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
                <CircularProgress size={20} />
            </Box>
        )
    }

    return (
        <>
            <Typography sx={{
                fontSize: 11, color: 'text.secondary',
                textTransform: 'uppercase', letterSpacing: '0.05em',
                mb: 1,
            }}>
                Daily breakdown
            </Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 1 }}>
                {days.map((d) => {
                    const isEmpty = d.total === 0
                    return (
                        <Box key={d.name} sx={{
                            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
                            borderRadius: '6px', p: '10px 12px',
                        }}>
                            <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 0.75 }}>
                                <Typography sx={{
                                    fontSize: 11, fontWeight: 600, color: 'text.secondary',
                                    textTransform: 'uppercase', letterSpacing: '0.05em',
                                }}>
                                    {d.name}
                                </Typography>
                                <Typography sx={{
                                    fontSize: 13, fontWeight: 700,
                                    color: isEmpty ? 'text.disabled' : 'text.primary',
                                }}>
                                    {d.total.toFixed(1)}h
                                </Typography>
                            </Stack>
                            <Box sx={{
                                fontSize: 11,
                                color: isEmpty ? 'text.disabled' : 'text.primary',
                                fontStyle: isEmpty ? 'italic' : 'normal',
                                lineHeight: 1.4,
                            }}>
                                {isEmpty ? (
                                    <span>Nothing logged</span>
                                ) : (
                                    d.tasks.map((t, idx) => (
                                        <Box key={idx} sx={{ py: '2px' }}>
                                            {t.type && (
                                                <>
                                                    <Box component="span" sx={{ color: 'text.secondary', fontSize: 10 }}>
                                                        {t.type}
                                                    </Box>
                                                    {' · '}
                                                </>
                                            )}
                                            <Box component="span" sx={{ color: BLUE, fontWeight: 500, fontSize: 10 }}>
                                                {t.project}
                                            </Box>
                                            {t.component && (
                                                <>
                                                    {' · '}
                                                    <Box component="span" sx={{ color: 'text.secondary', fontSize: 10 }}>
                                                        {t.component}
                                                    </Box>
                                                </>
                                            )}
                                            {' · '}
                                            {t.notes}
                                        </Box>
                                    ))
                                )}
                            </Box>
                        </Box>
                    )
                })}
            </Box>
        </>
    )
}

function ReviewRow({
    ts,
    deptName,
    selected,
    onToggleSelect,
    expanded,
    onToggleExpand,
    onApprove,
    onReject,
    actionPending,
    weeklyTarget,
    deadlineDay,
    deadlineTime,
}: {
    ts: Timesheet
    deptName: string
    selected: boolean
    onToggleSelect: () => void
    expanded: boolean
    onToggleExpand: () => void
    onApprove: () => void
    onReject: () => void
    actionPending: boolean
    weeklyTarget: number
    deadlineDay: string
    deadlineTime: string
}) {
    const pending = isPendingStatus(ts.status)
    const target = weeklyTarget
    const hoursDiff = Number(ts.totalHours) < target * 0.9 ? 'under' : Number(ts.totalHours) > target ? 'over' : 'ok'
    const hoursColor = hoursDiff === 'under' ? AMBER : hoursDiff === 'over' ? BLUE : 'text.primary'
    const late = isLateSubmission(ts.periodStart, ts.submittedAt, deadlineDay, deadlineTime)
    const submitted = ts.submittedAt

    const rowBg = selected ? softBg('primary') : expanded ? 'action.hover' : 'transparent'

    return (
        <>
            <Box
                onClick={onToggleExpand}
                sx={{
                    display: 'grid',
                    gridTemplateColumns: '32px 240px 100px 1fr 160px auto',
                    gap: 1.75,
                    alignItems: 'center',
                    p: '12px 16px',
                    borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: rowBg,
                    transition: 'background 0.15s',
                    cursor: 'pointer',
                    '&:hover': { bgcolor: selected ? softBg('primary') : 'action.hover' },
                }}
            >
                <Box onClick={(e) => e.stopPropagation()}>
                    <Checkbox
                        size="small"
                        checked={selected}
                        onChange={onToggleSelect}
                        sx={{ p: 0, color: BLUE, '&.Mui-checked': { color: BLUE } }}
                    />
                </Box>

                <Stack direction="row" alignItems="center" spacing={1.25} sx={{ minWidth: 0 }}>
                    <Box sx={{
                        width: 30, height: 30, borderRadius: '50%',
                        bgcolor: BLUE, color: '#fff',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 10, fontWeight: 600, flexShrink: 0,
                    }}>
                        {initials(ts.employeeName)}
                    </Box>
                    <Box sx={{ minWidth: 0 }}>
                        <Typography sx={{
                            fontSize: 13, fontWeight: 600, color: 'text.primary',
                            whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                        }}>
                            {ts.employeeName}
                        </Typography>
                        <Box sx={{
                            display: 'inline-block',
                            bgcolor: softBg('info'), color: 'info.dark',
                            borderRadius: '4px', px: 0.75, py: '1px',
                            fontSize: 11, fontWeight: 500,
                            mt: '2px',
                        }}>
                            {deptName}
                        </Box>
                    </Box>
                </Stack>

                <Box>
                    <Typography sx={{
                        fontSize: 14, fontWeight: 700,
                        color: hoursColor,
                        fontVariantNumeric: 'tabular-nums',
                    }}>
                        {Number(ts.totalHours).toFixed(1)}h
                    </Typography>
                    <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                        of {target}h
                    </Typography>
                </Box>

                <Stack direction="row" spacing={0.5} flexWrap="wrap" sx={{ rowGap: 0.5 }}>
                    {(ts.projectSummaries ?? []).slice(0, 4).map((p) => <ProjectChip key={p.projectId} p={p} />)}
                    {(ts.projectSummaries?.length ?? 0) > 4 && (
                        <Box sx={{
                            bgcolor: 'action.hover', color: 'text.secondary',
                            px: 1, py: '2px',
                            borderRadius: '10px',
                            fontSize: 11,
                        }}>
                            +{(ts.projectSummaries!.length - 4)} more
                        </Box>
                    )}
                </Stack>

                <Box>
                    {submitted ? (
                        <>
                            <Typography sx={{ fontSize: 12, color: 'text.primary', fontWeight: 600 }}>
                                {formatSubmittedDay(submitted)}
                            </Typography>
                            <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>
                                {formatSubmittedTime(submitted)}
                                {' '}
                                {late ? (
                                    <Box component="span" sx={{ color: AMBER }}>· Late</Box>
                                ) : pending ? (
                                    <Box component="span" sx={{ color: GREEN }}>· On time</Box>
                                ) : null}
                            </Typography>
                        </>
                    ) : (
                        <Typography sx={{ fontSize: 11, color: 'text.secondary' }}>Not submitted</Typography>
                    )}
                </Box>

                <Stack direction="row" spacing={0.5} onClick={(e) => e.stopPropagation()}>
                    {pending ? (
                        <>
                            <Button
                                size="small"
                                variant="contained"
                                onClick={onApprove}
                                disabled={actionPending}
                                sx={{
                                    fontSize: 12, textTransform: 'none',
                                    bgcolor: GREEN, color: '#fff',
                                    px: 1.5, py: '5px', minWidth: 'unset',
                                    boxShadow: 'none',
                                    '&:hover': { bgcolor: 'success.dark', boxShadow: 'none' },
                                }}
                            >
                                ✓ Approve
                            </Button>
                            <Button
                                size="small"
                                variant="contained"
                                onClick={onReject}
                                disabled={actionPending}
                                sx={{
                                    fontSize: 12, textTransform: 'none',
                                    bgcolor: RED, color: '#fff',
                                    px: 1.5, py: '5px', minWidth: 'unset',
                                    boxShadow: 'none',
                                    '&:hover': { bgcolor: 'error.dark', boxShadow: 'none' },
                                }}
                            >
                                ✕ Reject
                            </Button>
                        </>
                    ) : (
                        <Box sx={{
                            display: 'inline-flex', alignItems: 'center',
                            bgcolor: statusBadge(ts.status).bg,
                            color: statusBadge(ts.status).color,
                            fontSize: 11, fontWeight: 500,
                            px: 1.25, py: '3px',
                            borderRadius: '20px',
                            whiteSpace: 'nowrap',
                        }}>
                            {statusBadge(ts.status).label}
                        </Box>
                    )}
                </Stack>
            </Box>

            {expanded && (
                <Box sx={{
                    gridColumn: '1 / -1',
                    p: '14px 16px',
                    bgcolor: 'action.hover',
                    borderTop: '1px solid', borderTopColor: 'divider',
                    borderBottom: '1px solid', borderBottomColor: 'divider',
                }}>
                    <DailyBreakdown ts={ts} />
                </Box>
            )}
        </>
    )
}

export default function AllTimesheetsPage() {
    const queryClient = useQueryClient()

    const [tab, setTab] = useState<FilterTab>('all')
    const [search, setSearch] = useState('')
    const [deptFilter, setDeptFilter] = useState<string>('all')
    const [weekFilter, setWeekFilter] = useState<string>('all')
    const [projectFilter, setProjectFilter] = useState<string>('all')
    const [statusFilter, setStatusFilter] = useState<string>('all')
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
    const [expandedId, setExpandedId] = useState<string | null>(null)
    const [actionTarget, setActionTarget] = useState<string | null>(null)
    const [rejectDialog, setRejectDialog] = useState<{ ids: string[]; label: string } | null>(null)
    const [rejectReason, setRejectReason] = useState('')
    const [rejectError, setRejectError] = useState('')

    const { data: timesheets = [], isLoading } = useQuery({
        queryKey: ['timesheets'],
        queryFn: getTimesheets,
    })

    // Timesheet policy (weekly target + submission deadline) is admin-configurable
    // via AppSettings; fall back to the historical defaults until it loads.
    const { data: appSettings } = useAppSettings()
    const weeklyTarget = appSettings?.weeklyHoursTarget ?? DEFAULT_WEEKLY_TARGET
    const deadlineDay = appSettings?.timesheetSubmissionDeadlineDay ?? DEFAULT_DEADLINE_DAY
    const deadlineTime = appSettings?.timesheetSubmissionDeadlineTime ?? DEFAULT_DEADLINE_TIME

    const { data: departments = [] } = useQuery({
        queryKey: ['departments'],
        queryFn: getDepartments,
    })

    const { data: projects = [] } = useQuery({
        queryKey: ['projects'],
        queryFn: getProjects,
    })

    // Only used to name the entry types and components in the CSV export.
    const { data: projectTypes = [] } = useQuery({
        queryKey: ['projectTypes'],
        queryFn: getProjectTypes,
    })

    const { data: components = [] } = useQuery({
        queryKey: ['projectComponents'],
        queryFn: getProjectComponents,
    })

    const deptById = useMemo(() => new Map(departments.map((d) => [d.id, d.name])), [departments])
    const deptNames = useMemo(() => Array.from(new Set(departments.map((d) => d.name))).sort(), [departments])

    const approveMutation = useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            setActionTarget(null)
        },
    })

    const rejectMutation = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => rejectTimesheet(id, comment),
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
            setActionTarget(null)
        },
    })

    function openRejectDialog(ts: Timesheet) {
        setRejectDialog({
            ids: [ts.id],
            label: `${ts.employeeName} · ${new Date(ts.periodStart).toLocaleDateString('en-GB')} – ${new Date(ts.periodEnd).toLocaleDateString('en-GB')}`,
        })
        setRejectReason('')
        setRejectError('')
    }

    function openBulkRejectDialog() {
        if (selectedIds.size === 0) return
        setRejectDialog({
            ids: Array.from(selectedIds),
            label: `${selectedIds.size} selected timesheet${selectedIds.size === 1 ? '' : 's'}`,
        })
        setRejectReason('')
        setRejectError('')
    }

    function closeRejectDialog() {
        if (rejectMutation.isPending) return
        setRejectDialog(null)
        setRejectReason('')
        setRejectError('')
    }

    async function confirmReject() {
        if (!rejectDialog) return
        const trimmed = rejectReason.trim()
        if (trimmed.length === 0) {
            setRejectError('Please provide a reason for rejecting.')
            return
        }
        for (const id of rejectDialog.ids) {
            try {
                await rejectTimesheet(id, trimmed)
            } catch {/* keep going */}
        }
        if (rejectDialog.ids.length > 1) clearSelection()
        await queryClient.invalidateQueries({ queryKey: ['timesheets'] })
        await queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
        setRejectDialog(null)
        setRejectReason('')
        setRejectError('')
    }

    // Filter logic
    const filtered = useMemo(() => {
        let list = timesheets
        if (tab === 'pending') list = list.filter((t) => isPendingStatus(t.status))
        else if (tab === 'approved') list = list.filter((t) => t.status === 'Approved')
        else if (tab === 'rejected') list = list.filter((t) => t.status === 'Rejected')

        if (deptFilter !== 'all') {
            const deptId = departments.find((d) => d.name === deptFilter)?.id
            if (deptId != null) list = list.filter((t) => t.departmentId === deptId)
        }

        if (projectFilter !== 'all') {
            const pid = Number(projectFilter)
            list = list.filter((t) => (t.projectSummaries ?? []).some((p) => p.projectId === pid))
        }

        if (weekFilter !== 'all') {
            list = list.filter((t) => startOfWeekIso(new Date(t.periodStart)) === weekFilter)
        }

        if (statusFilter !== 'all') {
            list = list.filter((t) => t.status === statusFilter)
        }

        const q = search.trim().toLowerCase()
        if (q) list = list.filter((t) => t.employeeName.toLowerCase().includes(q))

        return list.slice().sort((a, b) => {
            const aDate = a.submittedAt ?? a.createdAt
            const bDate = b.submittedAt ?? b.createdAt
            return new Date(bDate).getTime() - new Date(aDate).getTime()
        })
    }, [timesheets, tab, deptFilter, projectFilter, weekFilter, statusFilter, search, departments])

    // Group by week
    const weekGroups = useMemo(() => {
        const groups = new Map<string, Timesheet[]>()
        for (const t of filtered) {
            const wk = startOfWeekIso(new Date(t.periodStart))
            const arr = groups.get(wk) ?? []
            arr.push(t)
            groups.set(wk, arr)
        }
        return Array.from(groups.entries())
            .sort((a, b) => b[0].localeCompare(a[0]))
            .map(([weekStart, items]) => ({ weekStart, items }))
    }, [filtered])

    // Week options from timesheets
    const weekOptions = useMemo(() => {
        const set = new Set<string>()
        for (const t of timesheets) set.add(startOfWeekIso(new Date(t.periodStart)))
        return Array.from(set).sort((a, b) => b.localeCompare(a))
    }, [timesheets])

    // Status options (the "Actions" column reflects status) — only those present in the data
    const statusOptions = useMemo(() => {
        const present = new Set<string>(timesheets.map((t) => t.status))
        const order = ['Draft', 'Submitted', 'Resubmitted', 'Approved', 'Rejected']
        return order
            .filter((s) => present.has(s))
            .map((s) => ({ value: s, label: statusBadge(s).label }))
    }, [timesheets])

    // Counts for tabs
    const counts = useMemo(() => ({
        all: timesheets.length,
        pending: timesheets.filter((t) => isPendingStatus(t.status)).length,
        approved: timesheets.filter((t) => t.status === 'Approved').length,
        rejected: timesheets.filter((t) => t.status === 'Rejected').length,
    }), [timesheets])

    // Top stats (current state of filtered set vs entire)
    const submittedThisWeek = useMemo(() => {
        const thisWeek = startOfWeekIso(new Date())
        return timesheets.filter((t) => startOfWeekIso(new Date(t.periodStart)) === thisWeek)
    }, [timesheets])
    const submittedHoursTotal = submittedThisWeek.reduce((s, t) => s + Number(t.totalHours), 0)
    const approvedThisWeek = submittedThisWeek.filter((t) => t.status === 'Approved').length
    const onTimeCount = submittedThisWeek.filter((t) => !isLateSubmission(t.periodStart, t.submittedAt, deadlineDay, deadlineTime)).length
    const onTimePct = submittedThisWeek.length > 0
        ? Math.round((onTimeCount / submittedThisWeek.length) * 100)
        : 0

    // Selection helpers
    const filteredPendingIds = useMemo(
        () => filtered.filter((t) => isPendingStatus(t.status)).map((t) => t.id),
        [filtered]
    )
    const allSelectedInView = filteredPendingIds.length > 0
        && filteredPendingIds.every((id) => selectedIds.has(id))

    const toggleSelect = (id: string) =>
        setSelectedIds((prev) => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id)
            else next.add(id)
            return next
        })

    const clearSelection = () => setSelectedIds(new Set())

    const selectAllInView = () => {
        if (allSelectedInView) clearSelection()
        else setSelectedIds(new Set(filteredPendingIds))
    }

    const bulkApprove = async () => {
        for (const id of Array.from(selectedIds)) {
            try {
                await approveTimesheet(id)
            } catch {/* keep going */}
        }
        clearSelection()
        await queryClient.invalidateQueries({ queryKey: ['timesheets'] })
    }

    const [exporting, setExporting] = useState(false)

    const exportCsv = async () => {
        if (filtered.length === 0) return
        setExporting(true)
        try {
            const projectById = new Map(projects.map((p) => [p.id, p]))
            const typeById = new Map(projectTypes.map((t) => [t.id, t]))
            const componentById = new Map(components.map((c) => [c.id, c]))

            const details = await Promise.all(
                filtered.map((t) =>
                    queryClient.fetchQuery({
                        queryKey: ['timesheet', t.id],
                        queryFn: () => getTimesheet(t.id),
                    })
                )
            )

            const sources = filtered.map((t, i) => ({
                timesheet: t,
                entries: (details[i]?.entries as TimesheetEntry[] | undefined) ?? [],
                departmentName: deptById.get(t.departmentId) ?? '',
            }))

            const csv = buildTimesheetsCsv(sources, projectById, typeById, componentById)

            const blob = new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8;' })
            const url = URL.createObjectURL(blob)
            const a = document.createElement('a')
            const today = new Date().toISOString().split('T')[0]
            a.href = url
            a.download = `timesheets-${today}.csv`
            document.body.appendChild(a)
            a.click()
            document.body.removeChild(a)
            URL.revokeObjectURL(url)
        } finally {
            setExporting(false)
        }
    }

    const tabs: { value: FilterTab; label: string; count: number }[] = [
        { value: 'all', label: 'All', count: counts.all },
        { value: 'pending', label: 'Pending', count: counts.pending },
        { value: 'approved', label: 'Approved', count: counts.approved },
        { value: 'rejected', label: 'Needs changes', count: counts.rejected },
    ]

    return (
        <Stack spacing={1.75}>
            {/* Summary stats */}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.75 }}>
                <StatMini
                    icon="⏳"
                    label="Awaiting Review"
                    value={counts.pending}
                    valueColor={AMBER}
                    sub="timesheets · review by Mon 5pm"
                />
                <StatMini
                    icon="✓"
                    label="Approved This Week"
                    value={approvedThisWeek}
                    valueColor={GREEN}
                    sub={`of ${submittedThisWeek.length} submitted`}
                />
                <StatMini
                    icon="📊"
                    label="Total Hours"
                    value={submittedHoursTotal.toFixed(1)}
                    sub="submitted across all depts"
                />
                <StatMini
                    icon="⏰"
                    label="On-Time Rate"
                    value={`${onTimePct}%`}
                    valueColor={BLUE}
                    sub={submittedThisWeek.length === 0
                        ? 'no submissions this week'
                        : `${onTimeCount} of ${submittedThisWeek.length} on time`}
                />
            </Box>

            {/* Bulk action bar */}
            {selectedIds.size > 0 && (
                <Box sx={{
                    bgcolor: 'background.paper',
                    color: 'text.primary',
                    border: '1px solid', borderColor: 'divider',
                    borderRadius: '10px',
                    p: '10px 16px',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                }}>
                    <Typography sx={{ fontSize: 13 }}>
                        <Box component="strong" sx={{ fontSize: 14 }}>{selectedIds.size}</Box>
                        {' '}timesheet{selectedIds.size === 1 ? '' : 's'} selected
                    </Typography>
                    <Stack direction="row" spacing={1}>
                        <Button
                            size="small"
                            variant="outlined"
                            onClick={clearSelection}
                            sx={{
                                fontSize: 12, textTransform: 'none',
                                color: 'text.primary', borderColor: 'divider',
                                bgcolor: 'transparent',
                                px: 1.5, py: '5px',
                                '&:hover': { bgcolor: 'action.hover', borderColor: 'divider' },
                            }}
                        >
                            Clear
                        </Button>
                        <Button
                            size="small"
                            variant="contained"
                            onClick={() => void bulkApprove()}
                            sx={{
                                fontSize: 12, textTransform: 'none',
                                bgcolor: GREEN, color: '#fff',
                                px: 1.5, py: '5px',
                                boxShadow: 'none',
                                '&:hover': { bgcolor: 'success.dark', boxShadow: 'none' },
                            }}
                        >
                            ✓ Approve Selected
                        </Button>
                        <Button
                            size="small"
                            variant="contained"
                            onClick={openBulkRejectDialog}
                            sx={{
                                fontSize: 12, textTransform: 'none',
                                bgcolor: RED, color: '#fff',
                                px: 1.5, py: '5px',
                                boxShadow: 'none',
                                '&:hover': { bgcolor: 'error.dark', boxShadow: 'none' },
                            }}
                        >
                            ✕ Reject Selected
                        </Button>
                    </Stack>
                </Box>
            )}

            {/* Tabs */}
            <Box sx={{
                display: 'flex',
                gap: '2px',
                borderBottom: '1px solid', borderColor: 'divider',
                px: '2px',
            }}>
                {tabs.map((t) => {
                    const active = tab === t.value
                    return (
                        <Box
                            key={t.value}
                            onClick={() => setTab(t.value)}
                            sx={{
                                display: 'flex', alignItems: 'center', gap: 0.75,
                                px: 2, py: '9px',
                                fontSize: 13,
                                fontWeight: active ? 600 : 500,
                                color: active ? BLUE : 'text.secondary',
                                cursor: 'pointer',
                                borderBottom: active ? `2px solid ${BLUE}` : '2px solid transparent',
                                mb: '-1px',
                                '&:hover': { color: active ? BLUE : 'text.primary' },
                            }}
                        >
                            {t.label}
                            <Box sx={{
                                bgcolor: active ? softBg('primary') : 'action.hover',
                                color: active ? BLUE : 'text.secondary',
                                fontSize: 10, fontWeight: 600,
                                px: '7px', py: '1px',
                                borderRadius: '10px',
                            }}>
                                {t.count}
                            </Box>
                        </Box>
                    )
                })}
            </Box>

            {/* Filter toolbar */}
            <Box sx={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1.5,
                p: '12px 16px',
                bgcolor: 'background.paper',
                border: '1px solid', borderColor: 'divider',
                borderRadius: '10px',
                flexWrap: 'wrap',
            }}>
                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
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
                        sx={{
                            fontSize: 12,
                            '& .MuiSelect-select': { py: '7px', px: '12px' },
                            '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                        }}
                    >
                        <MenuItem value="all">All departments</MenuItem>
                        {deptNames.map((d) => <MenuItem key={d} value={d}>{d}</MenuItem>)}
                    </Select>
                    <Select
                        size="small"
                        value={weekFilter}
                        onChange={(e) => setWeekFilter(e.target.value)}
                        sx={{
                            fontSize: 12,
                            '& .MuiSelect-select': { py: '7px', px: '12px' },
                            '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                        }}
                    >
                        <MenuItem value="all">All weeks</MenuItem>
                        {weekOptions.map((w) => (
                            <MenuItem key={w} value={w}>{formatWeekHeader(w)}</MenuItem>
                        ))}
                    </Select>
                    <Select
                        size="small"
                        value={projectFilter}
                        onChange={(e) => setProjectFilter(e.target.value)}
                        sx={{
                            fontSize: 12,
                            '& .MuiSelect-select': { py: '7px', px: '12px' },
                            '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                        }}
                    >
                        <MenuItem value="all">All projects</MenuItem>
                        {projects.map((p) => (
                            <MenuItem key={p.id} value={String(p.id)}>{p.code} — {p.name}</MenuItem>
                        ))}
                    </Select>
                    <Select
                        size="small"
                        value={statusFilter}
                        onChange={(e) => setStatusFilter(e.target.value)}
                        sx={{
                            fontSize: 12,
                            '& .MuiSelect-select': { py: '7px', px: '12px' },
                            '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                        }}
                    >
                        <MenuItem value="all">All statuses</MenuItem>
                        {statusOptions.map((s) => (
                            <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>
                        ))}
                    </Select>
                </Stack>
                <Button
                    size="small"
                    variant="outlined"
                    onClick={() => void exportCsv()}
                    disabled={filtered.length === 0 || exporting}
                    sx={{
                        fontSize: 12, textTransform: 'none',
                        color: BLUE, borderColor: BLUE,
                        px: 1.5, py: '5px',
                        '&:hover': { bgcolor: softBg('primary'), borderColor: BLUE },
                    }}
                >
                    {exporting ? 'Preparing CSV…' : '⤓ Export CSV'}
                </Button>
            </Box>

            {/* Week groups */}
            {isLoading ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                    <CircularProgress size={24} />
                </Box>
            ) : weekGroups.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper',
                    border: '1px solid', borderColor: 'divider',
                    borderRadius: '10px',
                    py: 6,
                    textAlign: 'center',
                }}>
                    <Typography sx={{ fontSize: 13, color: 'text.disabled' }}>No timesheets match the filters.</Typography>
                </Box>
            ) : (
                weekGroups.map(({ weekStart, items }) => {
                    const totalHrs = items.reduce((s, t) => s + Number(t.totalHours), 0)
                    const avg = items.length > 0 ? totalHrs / items.length : 0
                    const pendingInWeek = items.filter((t) => isPendingStatus(t.status)).length
                    const isCurrent = weekStart === startOfWeekIso(new Date())

                    return (
                        <Box key={weekStart} sx={{
                            bgcolor: 'background.paper',
                            border: '1px solid', borderColor: 'divider',
                            borderRadius: '10px',
                            overflow: 'hidden',
                        }}>
                            <Box sx={{
                                p: '12px 16px',
                                bgcolor: 'action.hover',
                                borderBottom: '1px solid', borderColor: 'divider',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'space-between',
                            }}>
                                <Box>
                                    <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                        {formatWeekHeader(weekStart)}
                                        {isCurrent && (
                                            <Box component="span" sx={{ fontSize: 11, fontWeight: 500, color: GREEN, ml: 0.75 }}>
                                                · Current
                                            </Box>
                                        )}
                                    </Typography>
                                    <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: '2px' }}>
                                        {items.length} submitted
                                    </Typography>
                                </Box>
                                <Stack direction="row" spacing={2} sx={{ fontSize: 11, color: 'text.secondary' }}>
                                    <Box>
                                        <Box component="strong" sx={{ color: 'text.primary', fontSize: 12 }}>{totalHrs.toFixed(1)}</Box>
                                        {' total hrs'}
                                    </Box>
                                    <Box>
                                        <Box component="strong" sx={{ color: 'text.primary', fontSize: 12 }}>{avg.toFixed(1)}</Box>
                                        {' avg/person'}
                                    </Box>
                                    {pendingInWeek > 0 ? (
                                        <Box>
                                            <Box component="strong" sx={{ color: 'text.primary', fontSize: 12 }}>{pendingInWeek}</Box>
                                            {' pending'}
                                        </Box>
                                    ) : (
                                        <Box sx={{ color: GREEN }}>
                                            <Box component="strong">✓</Box> Complete
                                        </Box>
                                    )}
                                </Stack>
                            </Box>

                            {/* Column header */}
                            <Box sx={{
                                p: '6px 14px 8px',
                                display: 'grid',
                                gridTemplateColumns: '32px 240px 100px 1fr 160px auto',
                                gap: 1.75,
                                alignItems: 'center',
                                bgcolor: 'action.hover',
                                borderBottom: '1px solid', borderBottomColor: 'divider',
                                fontSize: 10,
                                color: 'text.disabled',
                                fontWeight: 600,
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em',
                            }}>
                                <Checkbox
                                    size="small"
                                    checked={allSelectedInView && filteredPendingIds.length > 0}
                                    indeterminate={selectedIds.size > 0 && !allSelectedInView}
                                    onChange={selectAllInView}
                                    sx={{ p: 0, color: BLUE, '&.Mui-checked, &.MuiCheckbox-indeterminate': { color: BLUE } }}
                                />
                                <span>Employee</span>
                                <span>Hours</span>
                                <span>Projects</span>
                                <span>Submitted</span>
                                <span style={{ textAlign: 'right' }}>Actions</span>
                            </Box>

                            {items.map((ts) => (
                                <ReviewRow
                                    key={ts.id}
                                    ts={ts}
                                    deptName={deptById.get(ts.departmentId) ?? '—'}
                                    selected={selectedIds.has(ts.id)}
                                    onToggleSelect={() => toggleSelect(ts.id)}
                                    expanded={expandedId === ts.id}
                                    onToggleExpand={() => setExpandedId((cur) => cur === ts.id ? null : ts.id)}
                                    onApprove={() => {
                                        setActionTarget(ts.id)
                                        approveMutation.mutate(ts.id)
                                    }}
                                    onReject={() => openRejectDialog(ts)}
                                    actionPending={actionTarget === ts.id && (approveMutation.isPending || rejectMutation.isPending)}
                                    weeklyTarget={weeklyTarget}
                                    deadlineDay={deadlineDay}
                                    deadlineTime={deadlineTime}
                                />
                            ))}
                        </Box>
                    )
                })
            )}

            {/* Reject reason dialog */}
            <RejectReasonDialog
                open={rejectDialog !== null}
                title={rejectDialog && rejectDialog.ids.length > 1 ? 'Reject selected timesheets' : 'Reject timesheet'}
                label={rejectDialog?.label ?? ''}
                reason={rejectReason}
                error={rejectError}
                isPending={rejectMutation.isPending}
                onReasonChange={(value) => {
                    setRejectReason(value)
                    if (rejectError) setRejectError('')
                }}
                onClose={closeRejectDialog}
                onConfirm={() => void confirmReject()}
            />
        </Stack>
    )
}
