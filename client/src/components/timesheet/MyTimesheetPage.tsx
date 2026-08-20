import { useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import { useState } from 'react'
import { SweetAlert } from '../ui'
import { deleteTimesheet, getMyTimesheets, getTimesheetStatusHistories } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import type { Timesheet, TimesheetStatus, TimesheetStatusHistory, UserInfo } from '../../lib/types'
import { softBg, type SxColor } from '../../lib/theme-tokens'


const WEEKLY_TARGET = 40
const MONTHLY_TARGET = 160

const PROJECT_PALETTE = ['primary.main', 'success.main', 'warning.main', '#8B5CF6', 'error.main', '#06B6D4', '#EC4899', '#84CC16']
const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']

type StatusTab = 'all' | 'Draft' | 'Submitted' | 'Approved' | 'Rejected'

const STATUS_TABS: { value: StatusTab; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'Draft', label: 'Draft' },
    { value: 'Submitted', label: 'Pending' },
    { value: 'Approved', label: 'Approved' },
    { value: 'Rejected', label: 'Needs changes' },
]

/* ─── Helpers ───────────────────────────────────────────────────────────── */

function isoWeek(date: Date): number {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
    const dayNum = d.getUTCDay() || 7
    d.setUTCDate(d.getUTCDate() + 4 - dayNum)
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
    return Math.ceil(((d.getTime() - yearStart.getTime()) / 86_400_000 + 1) / 7)
}

function formatPeriod(start: string, end: string) {
    const s = new Date(start).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    const e = new Date(end).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
    return `${s} – ${e}`
}

function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
}

function formatDateTime(iso: string) {
    const d = new Date(iso)
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' }) +
        ', ' + d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
}

function isoDateOnly(value: string) {
    return value.split('T')[0]
}

function dateInRange(d: Date, startIso: string, endIso: string) {
    const s = new Date(startIso); s.setHours(0, 0, 0, 0)
    const e = new Date(endIso); e.setHours(23, 59, 59, 999)
    return d >= s && d <= e
}

function projectColor(projectId: number) {
    return PROJECT_PALETTE[projectId % PROJECT_PALETTE.length]
}

/* ─── Page ──────────────────────────────────────────────────────────────── */

export default function MyTimesheetPage({ user: _user }: { user: UserInfo }) {
    const queryClient = useQueryClient()
    const { uiStore } = useStore()

    const [statusTab, setStatusTab] = useState<StatusTab>('all')
    const [apiError, setApiError] = useState('')

    const { data: timesheets = [], isLoading } = useQuery({
        queryKey: ['timesheets', 'mine'],
        queryFn: getMyTimesheets,
    })

    const { data: histories = [] } = useQuery({
        queryKey: ['timesheetStatusHistories'],
        queryFn: getTimesheetStatusHistories,
    })

    const today = useMemo(() => { const d = new Date(); d.setHours(0, 0, 0, 0); return d }, [])
    const currentYear = today.getFullYear()
    const currentMonth = today.getMonth()

    /* Identify current-week timesheet (today within periodStart..periodEnd) */
    const currentTimesheet = useMemo(
        () => timesheets.find((t) => dateInRange(today, t.periodStart, t.periodEnd)) ?? null,
        [timesheets, today]
    )

    /* Latest comment per timesheet, for feedback panels */
    const latestComment = useMemo(() => {
        const map = new Map<string, TimesheetStatusHistory>()
        for (const h of histories) {
            if (!h.comment) continue
            const prev = map.get(h.timesheetId)
            if (!prev || new Date(h.changedAt) > new Date(prev.changedAt)) {
                map.set(h.timesheetId, h)
            }
        }
        return map
    }, [histories])

    /* Counts by status */
    const counts = useMemo(() => {
        const c = { all: timesheets.length, Draft: 0, Submitted: 0, Approved: 0, Rejected: 0, Resubmitted: 0 }
        for (const t of timesheets) c[t.status]++
        return c
    }, [timesheets])

    const pendingCount = counts.Submitted + counts.Resubmitted

    /* Aggregate per-project hours YTD */
    const projectTotals = useMemo(() => {
        const map = new Map<number, { code: string; name: string; hours: number }>()
        for (const t of timesheets) {
            if (new Date(t.periodStart).getFullYear() !== currentYear) continue
            for (const p of t.projectSummaries ?? []) {
                const cur = map.get(p.projectId)
                if (cur) cur.hours += Number(p.hours)
                else map.set(p.projectId, { code: p.code, name: p.name, hours: Number(p.hours) })
            }
        }
        return [...map.entries()]
            .map(([id, v]) => ({ id, ...v }))
            .sort((a, b) => b.hours - a.hours)
    }, [timesheets, currentYear])

    const totalProjectHours = projectTotals.reduce((a, p) => a + p.hours, 0)

    /* Monthly hours for the year chart */
    const monthHours = useMemo(() => {
        const arr = Array(12).fill(0) as number[]
        for (const t of timesheets) {
            const m = new Date(t.periodStart).getMonth()
            const y = new Date(t.periodStart).getFullYear()
            if (y === currentYear) arr[m] += Number(t.totalHours)
        }
        return arr
    }, [timesheets, currentYear])

    const totalYTD = monthHours.reduce((a, b) => a + b, 0)
    const maxMonth = Math.max(...monthHours, MONTHLY_TARGET)

    /* On-time streak: consecutive approved/submitted timesheets whose submit happened on or before periodEnd */
    const onTimeStreak = useMemo(() => {
        const sorted = [...timesheets]
            .filter((t) => t.status === 'Approved' && t.submittedAt)
            .sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime())
        let streak = 0
        for (const t of sorted) {
            const sub = new Date(t.submittedAt!)
            const end = new Date(t.periodEnd); end.setHours(23, 59, 59, 999)
            if (sub <= end) streak++
            else break
        }
        return streak
    }, [timesheets])

    /* Group filtered timesheets for the list */
    const sortedTimesheets = useMemo(
        () => [...timesheets].sort((a, b) => new Date(b.periodStart).getTime() - new Date(a.periodStart).getTime()),
        [timesheets]
    )

    const listForTab = useMemo(() => {
        if (statusTab === 'all') return sortedTimesheets
        if (statusTab === 'Submitted')
            return sortedTimesheets.filter((t) => t.status === 'Submitted' || t.status === 'Resubmitted')
        return sortedTimesheets.filter((t) => t.status === (statusTab as TimesheetStatus))
    }, [sortedTimesheets, statusTab])

    /* In "All" tab we group sections; otherwise flat */
    const groupedList = useMemo(() => {
        if (statusTab !== 'all') return null
        const rejected = listForTab.filter((t) => t.status === 'Rejected')
        const pending = listForTab.filter((t) => t.status === 'Submitted' || t.status === 'Resubmitted')
        const approved = listForTab.filter((t) => t.status === 'Approved')
        const drafts = listForTab.filter((t) => t.status === 'Draft' && t.id !== currentTimesheet?.id)
        return { rejected, pending, approved, drafts }
    }, [statusTab, listForTab, currentTimesheet])

    /* Delete draft */
    const deleteMutation = useMutation({
        mutationFn: (id: string) => deleteTimesheet(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['timesheets', 'mine'] }),
        onError: (err) => setApiError(getApiErrorMessage(err, 'Failed to delete timesheet.')),
    })

    async function handleDeleteDraft(t: Timesheet) {
        const result = await SweetAlert.fire({
            title: 'Delete draft timesheet?',
            text: `Delete the draft for ${formatPeriod(t.periodStart, t.periodEnd)}? This cannot be undone.`,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, delete',
            cancelButtonText: 'Keep it',
            confirmButtonColor: '#EF4444',
            reverseButtons: true,
        })
        if (result.isConfirmed) {
            setApiError('')
            await deleteMutation.mutateAsync(t.id)
        }
    }

    const openWeek = (periodStart: string) => {
        uiStore.navigateToNewTimesheet(isoDateOnly(periodStart))
    }

    if (isLoading) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
                <CircularProgress size={28} />
            </Box>
        )
    }

    return (
        <>
            {apiError && (
                <Alert severity="error" onClose={() => setApiError('')} sx={{ mb: 2 }}>{apiError}</Alert>
            )}

            {/* Mini stats */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <ThisWeekStat current={currentTimesheet} />
                <MiniStat label="⏳ Pending" value={String(pendingCount)} valueColor="#F59E0B"
                          sub={`timesheet${pendingCount === 1 ? '' : 's'} awaiting approval`} />
                <MiniStat label="📊 Year to date" value={totalYTD.toFixed(0)} valueColor={'primary.main'}
                          sub={`hours · ${(totalYTD / WEEKLY_TARGET).toFixed(0)} weeks logged`} />
                <MiniStat label="🔥 On-time streak" value={String(onTimeStreak)} valueColor="#22C47A"
                          sub="weeks submitted on time" />
            </Box>

            {/* Hero: current week + project breakdown */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', md: '3fr 2fr' },
                gap: '14px', mb: '14px',
            }}>
                {currentTimesheet
                    ? <CurrentWeekCard
                        timesheet={currentTimesheet}
                        dayHours={currentTimesheet.dailyHours ?? null}
                        today={today}
                        onContinue={() => openWeek(currentTimesheet.periodStart)}
                    />
                    : <EmptyCurrentWeek onOpen={() => uiStore.navigateToNewTimesheet()} />}

                <ProjectBreakdown projects={projectTotals} total={totalProjectHours} />
            </Box>

            {/* Year activity */}
            <YearActivity monthHours={monthHours} maxMonth={maxMonth} currentMonth={currentMonth} totalYTD={totalYTD} />

            {/* Tabs + Open this week */}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: '18px', mx: '4px', mb: '10px' }}>
                <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>All timesheets</Box>
                <Box
                    component="button"
                    onClick={() => uiStore.navigateToNewTimesheet()}
                    sx={{
                        bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '14px', py: '6px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', transition: 'background 0.15s',
                        '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    📝 Open this week
                </Box>
            </Box>

            <Box sx={{ display: 'flex', gap: '2px', mb: '14px', borderBottom: '1px solid', borderColor: 'divider', px: '2px', flexWrap: 'wrap' }}>
                {STATUS_TABS.map((tab) => {
                    const active = statusTab === tab.value
                    const c =
                        tab.value === 'all' ? counts.all :
                        tab.value === 'Submitted' ? pendingCount :
                        counts[tab.value]
                    const danger = tab.value === 'Rejected' && c > 0
                    return (
                        <Box
                            key={tab.value}
                            component="button"
                            onClick={() => setStatusTab(tab.value)}
                            sx={{
                                p: '9px 16px', fontSize: 13,
                                color: active ? 'primary.main' : danger ? 'error.dark' : 'text.secondary',
                                cursor: 'pointer',
                                borderBottom: active ? `2px solid ${'primary.main'}` : '2px solid transparent',
                                mb: '-1px', display: 'flex', alignItems: 'center', gap: '6px',
                                background: 'none', border: 'none', fontFamily: 'inherit',
                                fontWeight: active ? 600 : 500,
                                '&:hover': { color: active ? 'primary.main' : 'text.primary' },
                            }}
                        >
                            {tab.label}
                            <Box component="span" sx={{
                                bgcolor: active ? softBg('primary') : danger ? softBg('error') : 'action.hover',
                                color: active ? 'primary.main' : danger ? 'error.dark' : 'text.secondary',
                                fontSize: 10, fontWeight: 600,
                                px: '7px', borderRadius: '10px',
                            }}>{c}</Box>
                        </Box>
                    )
                })}
            </Box>

            {timesheets.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    No timesheets yet. Start tracking your time by opening this week.
                </Box>
            ) : groupedList ? (
                <>
                    {groupedList.rejected.length > 0 && (
                        <>
                            <SectionHeader title="⚠️ Action needed" tone="danger" />
                            {groupedList.rejected.map((t) => (
                                <TimesheetCard key={t.id} t={t} comment={latestComment.get(t.id)}
                                               onEdit={() => openWeek(t.periodStart)} />
                            ))}
                        </>
                    )}
                    {groupedList.pending.length > 0 && (
                        <>
                            <SectionHeader title="⏳ Awaiting approval" />
                            {groupedList.pending.map((t) => (
                                <TimesheetCard key={t.id} t={t} comment={latestComment.get(t.id)}
                                               onView={() => openWeek(t.periodStart)} />
                            ))}
                        </>
                    )}
                    {groupedList.drafts.length > 0 && (
                        <>
                            <SectionHeader title="✎ Drafts" />
                            {groupedList.drafts.map((t) => (
                                <TimesheetCard key={t.id} t={t} comment={latestComment.get(t.id)}
                                               onEdit={() => openWeek(t.periodStart)}
                                               onDelete={() => void handleDeleteDraft(t)} />
                            ))}
                        </>
                    )}
                    {groupedList.approved.length > 0 && (
                        <>
                            <SectionHeader title="Approved timesheets" />
                            {groupedList.approved.map((t) => (
                                <TimesheetCard key={t.id} t={t} comment={latestComment.get(t.id)}
                                               onView={() => openWeek(t.periodStart)} />
                            ))}
                        </>
                    )}
                </>
            ) : (
                <>
                    <SectionHeader title={STATUS_TABS.find((s) => s.value === statusTab)?.label ?? ''} />
                    {listForTab.length === 0 ? (
                        <Box sx={{
                            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                            py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                        }}>
                            No timesheets in this category.
                        </Box>
                    ) : listForTab.map((t) => (
                        <TimesheetCard key={t.id} t={t} comment={latestComment.get(t.id)}
                                       onEdit={t.status === 'Draft' || t.status === 'Rejected' ? () => openWeek(t.periodStart) : undefined}
                                       onView={t.status === 'Approved' || t.status === 'Submitted' || t.status === 'Resubmitted' ? () => openWeek(t.periodStart) : undefined}
                                       onDelete={t.status === 'Draft' && t.id !== currentTimesheet?.id ? () => void handleDeleteDraft(t) : undefined} />
                    ))}
                </>
            )}
        </>
    )
}

/* ─── Subcomponents ─────────────────────────────────────────────────────── */

function MiniStat({ label, value, sub, valueColor }: {
    label: string; value: string; sub: string; valueColor?: string
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 22, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>{sub}</Box>
        </Box>
    )
}

function ThisWeekStat({ current }: { current: Timesheet | null }) {
    if (!current) {
        return <MiniStat label="📝 This week" value="—" sub="No timesheet started" />
    }
    const hours = Number(current.totalHours)
    const isUnder = hours < WEEKLY_TARGET
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                📝 This week
            </Box>
            <Box sx={{ fontSize: 22, fontWeight: 700, color: isUnder ? 'warning.main' : 'success.main', lineHeight: 1 }}>
                {hours.toFixed(1)}
                <Box component="span" sx={{ fontSize: 14, color: 'text.disabled', fontWeight: 500 }}>{` / ${WEEKLY_TARGET}h`}</Box>
            </Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '4px' }}>
                {isUnder ? `${(WEEKLY_TARGET - hours).toFixed(1)}h to go` : 'Week complete'}
            </Box>
        </Box>
    )
}

function CurrentWeekCard({ timesheet, dayHours, today, onContinue }: {
    timesheet: Timesheet
    dayHours: number[] | null
    today: Date
    onContinue: () => void
}) {
    const hours = Number(timesheet.totalHours)
    const pct = Math.min(100, (hours / WEEKLY_TARGET) * 100)
    const remaining = Math.max(0, WEEKLY_TARGET - hours)
    const weekNum = isoWeek(new Date(timesheet.periodStart))
    const periodStartDate = new Date(timesheet.periodStart); periodStartDate.setHours(0, 0, 0, 0)
    const todayIdx = Math.max(0, Math.min(4, Math.round((today.getTime() - periodStartDate.getTime()) / 86_400_000)))
    const status = timesheet.status

    return (
        <Box sx={{
            background: 'linear-gradient(135deg, #1A1A2E 0%, #2D2D4E 100%)',
            color: '#fff', borderRadius: '12px', p: '20px 22px', position: 'relative', overflow: 'hidden',
            '&::before': {
                content: '"⏱"', position: 'absolute', right: -10, bottom: -30,
                fontSize: 140, opacity: 0.08, transform: 'rotate(-12deg)',
            },
        }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: '16px', position: 'relative', zIndex: 1 }}>
                <Box>
                    <Box sx={{ fontSize: 11, opacity: 0.7, textTransform: 'uppercase', letterSpacing: '0.08em', mb: '6px' }}>
                        Current week
                    </Box>
                    <Box sx={{ fontSize: 18, fontWeight: 700, lineHeight: 1.15 }}>{formatPeriod(timesheet.periodStart, timesheet.periodEnd)}</Box>
                    <Box sx={{ fontSize: 12, opacity: 0.75, mt: '4px' }}>Week {weekNum} · Submit by Friday 6pm</Box>
                </Box>
                <CurrentStatusTag status={status} />
            </Box>

            <Box sx={{ bgcolor: 'rgba(255,255,255,0.08)', borderRadius: '10px', p: '14px 16px', position: 'relative', zIndex: 1 }}>
                <Box sx={{ display: 'flex', alignItems: 'baseline', gap: '8px', mb: '10px' }}>
                    <Box sx={{ fontSize: 32, fontWeight: 700, fontVariantNumeric: 'tabular-nums', lineHeight: 1 }}>{hours.toFixed(1)}</Box>
                    <Box sx={{ fontSize: 14, opacity: 0.7 }}>{`/ ${WEEKLY_TARGET}h logged`}</Box>
                    <Box sx={{
                        ml: 'auto', fontSize: 12, px: '10px', py: '3px', borderRadius: '12px',
                        bgcolor: remaining > 0 ? 'rgba(245,158,11,0.2)' : 'rgba(34,196,122,0.2)',
                        color: remaining > 0 ? '#FCD34D' : '#6EE7B7',
                        fontWeight: 500,
                    }}>{remaining > 0 ? `${remaining.toFixed(1)}h to go` : '✓ Complete'}</Box>
                </Box>
                <Box sx={{ height: 8, bgcolor: 'rgba(255,255,255,0.15)', borderRadius: '4px', overflow: 'hidden', mb: '12px' }}>
                    <Box sx={{ height: '100%', bgcolor: 'primary.main', borderRadius: '4px', width: `${pct}%`, transition: 'width 0.3s' }} />
                </Box>
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: '4px' }}>
                    {DAY_LABELS.map((d, i) => {
                        const h = dayHours?.[i] ?? 0
                        const isToday = i === todayIdx
                        const filled = h > 0
                        const bg = isToday
                            ? 'rgba(79,142,247,0.25)'
                            : filled ? 'rgba(34,196,122,0.18)' : 'rgba(255,255,255,0.06)'
                        const color = isToday ? '#93C5FD' : filled ? '#6EE7B7' : 'rgba(255,255,255,0.4)'
                        return (
                            <Box key={d} sx={{
                                bgcolor: bg, borderRadius: '6px', p: '8px 4px', textAlign: 'center',
                                boxShadow: isToday ? `inset 0 0 0 1px ${'primary.main'}` : 'none',
                            }}>
                                <Box sx={{ fontSize: 10, opacity: 0.6, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{d}</Box>
                                <Box sx={{ fontSize: 12, fontWeight: 600, mt: '4px', fontVariantNumeric: 'tabular-nums', color }}>
                                    {h > 0 ? `${h.toFixed(1)}h` : '—'}
                                </Box>
                            </Box>
                        )
                    })}
                </Box>
            </Box>

            <Box sx={{ display: 'flex', gap: '8px', mt: '14px', position: 'relative', zIndex: 1 }}>
                <Box
                    component="button"
                    onClick={onContinue}
                    sx={{
                        flex: 1, bgcolor: 'primary.main', color: '#fff', border: 'none',
                        p: '10px 16px', borderRadius: '8px', fontSize: 13, fontWeight: 600,
                        cursor: 'pointer', fontFamily: 'inherit',
                        '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    {status === 'Draft' ? 'Continue filling' : 'Open week'}
                </Box>
            </Box>
        </Box>
    )
}

function CurrentStatusTag({ status }: { status: TimesheetStatus }) {
    const config: Record<TimesheetStatus, { bg: SxColor; fg: string; dot: string; label: string }> = {
        Draft:       { bg: 'rgba(245,158,11,0.2)', fg: '#FCD34D', dot: 'warning.main', label: 'Draft' },
        Submitted:   { bg: 'rgba(245,158,11,0.2)', fg: '#FCD34D', dot: 'warning.main', label: 'Pending review' },
        Resubmitted: { bg: 'rgba(245,158,11,0.2)', fg: '#FCD34D', dot: 'warning.main', label: 'Re-submitted' },
        Approved:    { bg: 'rgba(34,196,122,0.2)', fg: '#6EE7B7', dot: 'success.main', label: 'Approved' },
        Rejected:    { bg: 'rgba(255,77,79,0.2)',  fg: '#FCA5A5', dot: 'error.main', label: 'Needs changes' },
    }
    const s = config[status]
    return (
        <Box component="span" sx={{
            display: 'inline-flex', alignItems: 'center', gap: '6px',
            bgcolor: s.bg, color: s.fg, px: '10px', py: '4px',
            borderRadius: '12px', fontSize: 11, fontWeight: 500, whiteSpace: 'nowrap',
        }}>
            <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: s.dot }} />
            {s.label}
        </Box>
    )
}

function EmptyCurrentWeek({ onOpen }: { onOpen: () => void }) {
    return (
        <Box sx={{
            background: 'linear-gradient(135deg, #1A1A2E 0%, #2D2D4E 100%)',
            color: '#fff', borderRadius: '12px', p: '30px 22px',
            textAlign: 'center', position: 'relative', overflow: 'hidden',
            '&::before': {
                content: '"⏱"', position: 'absolute', right: -10, bottom: -30,
                fontSize: 140, opacity: 0.08, transform: 'rotate(-12deg)',
            },
        }}>
            <Box sx={{ fontSize: 14, fontWeight: 600, mb: '6px', position: 'relative', zIndex: 1 }}>No timesheet for this week</Box>
            <Box sx={{ fontSize: 12, opacity: 0.7, mb: '14px', position: 'relative', zIndex: 1 }}>
                Start tracking — log hours by project and submit on Friday.
            </Box>
            <Box
                component="button"
                onClick={onOpen}
                sx={{
                    bgcolor: 'primary.main', color: '#fff', border: 'none',
                    p: '10px 18px', borderRadius: '8px', fontSize: 13, fontWeight: 600,
                    cursor: 'pointer', fontFamily: 'inherit', position: 'relative', zIndex: 1,
                    '&:hover': { bgcolor: 'primary.dark' },
                }}
            >
                📝 Start this week
            </Box>
        </Box>
    )
}

function ProjectBreakdown({ projects, total }: {
    projects: { id: number; code: string; name: string; hours: number }[]
    total: number
}) {
    if (projects.length === 0) {
        return (
            <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '18px 20px' }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Time by project</Box>
                <Box sx={{ fontSize: 12, color: 'text.secondary' }}>No hours logged this year yet.</Box>
            </Box>
        )
    }
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '18px 20px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '14px' }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>Time by project</Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>YTD · {total.toFixed(0)}h total</Box>
            </Box>
            <Box sx={{ fontSize: 26, fontWeight: 700, color: 'text.primary', lineHeight: 1, mb: '14px' }}>
                {total.toFixed(0)}
                <Box component="span" sx={{ fontSize: 13, fontWeight: 500, color: 'text.secondary', ml: '4px' }}>
                    hours across {projects.length} project{projects.length === 1 ? '' : 's'}
                </Box>
            </Box>
            <Box sx={{ height: 12, bgcolor: 'action.hover', borderRadius: '6px', overflow: 'hidden', display: 'flex', mb: '14px' }}>
                {projects.map((p) => (
                    <Box
                        key={p.id}
                        title={`${p.code}: ${p.hours.toFixed(1)}h`}
                        sx={{
                            height: '100%', bgcolor: projectColor(p.id),
                            width: `${(p.hours / total) * 100}%`,
                            transition: 'opacity 0.15s', '&:hover': { opacity: 0.8 },
                        }}
                    />
                ))}
            </Box>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                {projects.map((p) => (
                    <Box key={p.id} sx={{ display: 'grid', gridTemplateColumns: '10px 1fr auto auto', gap: '10px', alignItems: 'center' }}>
                        <Box sx={{ width: 10, height: 10, borderRadius: '3px', bgcolor: projectColor(p.id) }} />
                        <Box sx={{ fontSize: 12, color: 'text.primary', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            <Box component="strong" sx={{ fontWeight: 600 }}>{p.code}</Box>
                            <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>{` · ${p.name}`}</Box>
                        </Box>
                        <Box sx={{ fontSize: 12, color: 'text.primary', fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
                            {p.hours.toFixed(1)}h
                        </Box>
                        <Box sx={{ fontSize: 11, color: 'text.secondary', minWidth: 38, textAlign: 'right' }}>
                            {Math.round((p.hours / total) * 100)}%
                        </Box>
                    </Box>
                ))}
            </Box>
        </Box>
    )
}

function YearActivity({ monthHours, maxMonth, currentMonth, totalYTD }: {
    monthHours: number[]
    maxMonth: number
    currentMonth: number
    totalYTD: number
}) {
    const activeMonths = monthHours.filter((h) => h > 0).length

    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '18px 20px', mb: '14px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '14px', flexWrap: 'wrap', gap: '8px' }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                    {new Date().getFullYear()} hours by month ·{' '}
                    <Box component="strong" sx={{ color: 'primary.main', fontSize: 15 }}>{totalYTD.toFixed(0)}h</Box>
                    {' '}year to date
                </Box>
                {activeMonths >= 2 && (
                    <Box sx={{ display: 'flex', gap: '12px', fontSize: 11, color: 'text.secondary' }}>
                        <LegendSwatch color={'primary.main'} label="Logged" />
                        <LegendSwatch color="linear-gradient(180deg, #4F8EF7 0%, #3A7AE4 100%)" label="Current" />
                        <LegendSwatch color="#E4E6EA" label="Future" />
                    </Box>
                )}
            </Box>
            {activeMonths < 2 ? (
                <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '18px' }}>
                    {totalYTD > 0 ? `${totalYTD.toFixed(0)}h logged so far` : 'No hours logged yet this year.'}
                </Box>
            ) : (
                <Box sx={{ position: 'relative', height: 110, pt: '14px' }}>
                    <Box sx={{
                        position: 'absolute', left: 0, right: 0,
                        top: `${14 + (1 - MONTHLY_TARGET / maxMonth) * (110 - 14 - 18)}px`,
                        borderTop: '1px dashed #F59E0B', pointerEvents: 'none',
                    }}>
                        <Box component="span" sx={{
                            position: 'absolute', right: 0, top: -16,
                            fontSize: 10, color: 'warning.dark', bgcolor: softBg('warning'),
                            px: '6px', py: '1px', borderRadius: '3px', border: '1px solid #FDE68A',
                        }}>Target {MONTHLY_TARGET}h/mo</Box>
                    </Box>
                    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(12, 1fr)', gap: '6px', height: '100%', alignItems: 'flex-end' }}>
                        {monthHours.map((h, i) => {
                            const isFuture = i > currentMonth
                            const isCurrent = i === currentMonth
                            const heightPct = maxMonth > 0 ? (h / maxMonth) * 100 : 0
                            const bg = isFuture ? 'divider'
                                : isCurrent ? 'linear-gradient(180deg, #4F8EF7 0%, #3A7AE4 100%)'
                                : 'primary.main'
                            return (
                                <Box key={i} sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '4px', height: '100%', justifyContent: 'flex-end' }}>
                                    <Box sx={{ fontSize: 10, color: isFuture ? 'text.disabled' : 'text.primary', fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
                                        {h > 0 ? Math.round(h) : ''}
                                    </Box>
                                    <Box sx={{ width: '100%', height: 'calc(100% - 18px)', display: 'flex', alignItems: 'flex-end' }}>
                                        <Box title={`${MONTH_LABELS[i]}: ${h.toFixed(0)}h`} sx={{
                                            width: '100%', height: `${heightPct}%`, minHeight: 2,
                                            background: bg, borderRadius: '4px 4px 0 0',
                                            boxShadow: isCurrent ? '0 0 0 2px rgba(79,142,247,0.2)' : 'none',
                                            transition: 'opacity 0.15s', cursor: 'pointer',
                                            '&:hover': { opacity: 0.85 },
                                        }} />
                                    </Box>
                                    <Box sx={{ fontSize: 10, color: isCurrent ? 'text.primary' : 'text.secondary', fontWeight: isCurrent ? 700 : 500 }}>
                                        {MONTH_LABELS[i]}
                                    </Box>
                                </Box>
                            )
                        })}
                    </Box>
                </Box>
            )}
        </Box>
    )
}

function LegendSwatch({ color, label }: { color: string; label: string }) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{ width: 10, height: 10, borderRadius: '2px', background: color, display: 'inline-block' }} />
            {label}
        </Box>
    )
}

function SectionHeader({ title, tone }: { title: string; tone?: 'danger' }) {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: '18px', mx: '4px', mb: '10px' }}>
            <Box sx={{
                fontSize: 12, fontWeight: 600,
                color: tone === 'danger' ? 'error.dark' : 'text.secondary',
                textTransform: 'uppercase', letterSpacing: '0.05em',
            }}>
                {title}
            </Box>
        </Box>
    )
}

function TimesheetCard({ t, comment, onEdit, onView, onDelete }: {
    t: Timesheet
    comment?: TimesheetStatusHistory
    onEdit?: () => void
    onView?: () => void
    onDelete?: () => void
}) {
    const status = t.status
    const hours = Number(t.totalHours)
    const isUnder = hours < WEEKLY_TARGET * 0.9
    const weekNum = isoWeek(new Date(t.periodStart))
    const daily = t.dailyHours ?? [0, 0, 0, 0, 0]
    const filledDays = daily.filter((h) => h > 0).length
    const projects = (t.projectSummaries ?? []).slice(0, 6)

    const borderLeftColor =
        status === 'Submitted' || status === 'Resubmitted' ? 'warning.main' :
        status === 'Approved' ? 'success.main' :
        status === 'Rejected' ? 'error.main' :
        'text.secondary'

    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
            borderLeft: `3px solid ${borderLeftColor}`,
            borderRadius: '10px', p: '16px 18px', mb: '10px',
            transition: 'all 0.15s',
            '&:hover': { borderColor: '#D1D5DB' },
        }}>
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', sm: '1fr auto' },
                gap: '14px', alignItems: 'flex-start',
            }}>
                <Box sx={{ minWidth: 0 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap', mb: '8px' }}>
                        <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                            {formatPeriod(t.periodStart, t.periodEnd)}
                            <Box component="span" sx={{ color: 'text.disabled', fontWeight: 500, ml: '8px', fontSize: 12 }}>
                                Week {weekNum}
                            </Box>
                        </Box>
                        <StatusBadge status={status} />
                    </Box>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: '18px', mb: '10px', flexWrap: 'wrap' }}>
                        <SummaryItem label="Total" value={`${hours.toFixed(1)}h`} tone={isUnder ? 'amber' : undefined} />
                        <SummaryItem label="Target" value={`${WEEKLY_TARGET}h`} muted />
                        <SummaryItem label="Days" value={`${filledDays} / 5`} muted />
                    </Box>

                    <Box sx={{ display: 'flex', gap: '4px', mb: '8px' }}>
                        {DAY_LABELS.map((d, i) => {
                            const h = daily[i] ?? 0
                            const filled = h > 0
                            return (
                                <Box key={d} sx={{
                                    flex: 1, p: '6px 8px', borderRadius: '5px', textAlign: 'center',
                                    fontSize: 11, minWidth: 0,
                                    bgcolor: filled ? softBg('success') : 'action.hover',
                                }}>
                                    <Box sx={{ fontSize: 9, color: 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{d}</Box>
                                    <Box sx={{
                                        fontWeight: filled ? 600 : 500,
                                        color: filled ? 'success.dark' : 'text.disabled',
                                        fontVariantNumeric: 'tabular-nums', mt: '2px',
                                    }}>
                                        {filled ? `${h.toFixed(1)}h` : '—'}
                                    </Box>
                                </Box>
                            )
                        })}
                    </Box>

                    {projects.length > 0 && (
                        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: '6px', mt: '6px' }}>
                            {projects.map((p) => (
                                <Box key={p.projectId} sx={{
                                    display: 'inline-flex', alignItems: 'center', gap: '5px',
                                    bgcolor: 'action.hover', border: '1px solid', borderColor: 'divider',
                                    p: '3px 9px', borderRadius: '12px', fontSize: 11, color: 'text.primary',
                                }}>
                                    <Box sx={{ width: 8, height: 8, borderRadius: '2px', bgcolor: projectColor(p.projectId) }} />
                                    <Box component="strong" sx={{ color: 'text.primary', fontWeight: 600 }}>{p.code}</Box>
                                    <Box component="span" sx={{ color: 'text.secondary' }}>· {Number(p.hours).toFixed(1)}h</Box>
                                </Box>
                            ))}
                        </Box>
                    )}

                    <FeedbackBox t={t} comment={comment} />
                </Box>

                <Box sx={{
                    display: 'flex',
                    flexDirection: { xs: 'row', sm: 'column' },
                    gap: '6px', alignItems: 'stretch',
                    minWidth: { sm: 110 },
                }}>
                    {onEdit && (
                        <ActionButton onClick={onEdit} variant={status === 'Rejected' ? 'primary' : 'outline'}>
                            {status === 'Rejected' ? 'Fix & Resubmit' : status === 'Draft' ? 'Continue' : 'Edit'}
                        </ActionButton>
                    )}
                    {onView && (
                        <ActionButton onClick={onView} variant="ghost">View details</ActionButton>
                    )}
                    {onDelete && (
                        <ActionButton onClick={onDelete} variant="danger">Delete</ActionButton>
                    )}
                </Box>
            </Box>
        </Box>
    )
}

function SummaryItem({ label, value, tone, muted }: {
    label: string; value: string; tone?: 'amber'; muted?: boolean
}) {
    return (
        <Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{label}</Box>
            <Box sx={{
                fontSize: muted ? 14 : 16, fontWeight: muted ? 500 : 700,
                color: tone === 'amber' ? 'warning.main' : muted ? 'text.secondary' : 'text.primary',
                lineHeight: 1, mt: '3px', fontVariantNumeric: 'tabular-nums',
            }}>{value}</Box>
        </Box>
    )
}

function StatusBadge({ status }: { status: TimesheetStatus }) {
    const config: Record<TimesheetStatus, { bg: SxColor; color: string; label: string }> = {
        Draft:       { bg: softBg('info'), color: 'info.dark', label: 'Draft' },
        Submitted:   { bg: softBg('warning'), color: 'warning.dark', label: 'Pending review' },
        Resubmitted: { bg: '#F3E8FF', color: '#6D28D9', label: 'Re-submitted' },
        Approved:    { bg: softBg('success'), color: 'success.dark', label: 'Approved' },
        Rejected:    { bg: softBg('error'), color: 'error.dark', label: 'Needs changes' },
    }
    const c = config[status]
    return (
        <Box component="span" sx={{
            display: 'inline-flex', alignItems: 'center',
            bgcolor: c.bg, color: c.color, borderRadius: '20px',
            px: 1.25, py: '3px', fontSize: 11, fontWeight: 500, whiteSpace: 'nowrap',
        }}>{c.label}</Box>
    )
}

function FeedbackBox({ t, comment }: { t: Timesheet; comment?: TimesheetStatusHistory }) {
    if (t.status === 'Submitted' || t.status === 'Resubmitted') {
        return (
            <Box sx={feedbackSx(softBg('warning'), 'warning.dark', 'warning.main')}>
                <Box component="span">⏳</Box>
                <Box>Submitted {t.submittedAt ? formatDateTime(t.submittedAt) : ''} · waiting for manager to review</Box>
            </Box>
        )
    }
    if (t.status === 'Approved') {
        return (
            <Box sx={feedbackSx(softBg('success'), 'success.dark', 'success.main')}>
                <Box component="span">✓</Box>
                <Box>
                    Approved{comment ? <> by <Box component="strong">{comment.changedByUserName}</Box></> : ''}
                    {t.approvedAt ? ` on ${formatDate(t.approvedAt)}` : ''}
                </Box>
            </Box>
        )
    }
    if (t.status === 'Rejected' && comment?.comment) {
        return (
            <Box sx={feedbackSx(softBg('error'), 'error.dark', 'error.main')}>
                <Box component="span">💬</Box>
                <Box>
                    <Box component="strong">{comment.changedByUserName}:</Box> "{comment.comment}"
                </Box>
            </Box>
        )
    }
    return null
}

function feedbackSx(bg: SxColor, color: string, accent: string) {
    return {
        mt: '10px', p: '10px 12px', borderRadius: '6px', fontSize: 12,
        display: 'flex', alignItems: 'flex-start', gap: '8px',
        bgcolor: bg, color, borderLeft: `3px solid ${accent}`,
    } as const
}

function ActionButton({ onClick, variant, children }: {
    onClick: () => void
    variant: 'primary' | 'outline' | 'ghost' | 'danger'
    children: React.ReactNode
}) {
    const styles =
        variant === 'primary' ? { bg: 'primary.main', color: '#fff', border: 'none', hover: 'primary.dark', hoverColor: '#fff' } :
        variant === 'outline' ? { bg: '#fff', color: 'primary.main', border: `1px solid ${'primary.main'}`, hover: softBg('primary'), hoverColor: 'primary.main' } :
        variant === 'danger'  ? { bg: 'transparent', color: 'error.dark', border: '1px solid', borderColor: 'divider', hover: softBg('error'), hoverColor: 'error.dark' } :
                                 { bg: 'transparent', color: 'text.secondary', border: '1px solid', borderColor: 'divider', hover: 'action.hover', hoverColor: 'text.primary' }
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: styles.bg, color: styles.color, border: styles.border,
                borderRadius: '6px', px: '12px', py: '6px', fontSize: 12, fontWeight: 500,
                cursor: 'pointer', fontFamily: 'inherit', transition: 'background 0.15s',
                whiteSpace: 'nowrap',
                '&:hover': { bgcolor: styles.hover, color: styles.hoverColor },
            }}
        >
            {children}
        </Box>
    )
}
