import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import {
    getAnnualLeaves, getDepartments, getEmployeeProfiles, getHolidays, getLeaveStatusHistories,
    getLeaveTypes, updateLeaveStatus,
} from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type {
    AnnualLeave, EmployeeProfile, LeaveStatusHistory, LeaveType, UserInfo,
} from '../../lib/types'

const C_BORDER = '#E4E6EA'
const C_HEADING = '#1A1A2E'
const C_MUTED = '#6B7280'
const C_BLUE = '#4F8EF7'

const LEAVE_ICONS: Record<string, string> = {
    annual: '🌴', vacation: '🌴',
    sick: '🤒', personal: '🏠',
    bereavement: '🕊️', unpaid: '💼',
    maternity: '👶', paternity: '👶', parental: '👶',
}

type TypeKey = 'annual' | 'sick' | 'personal' | 'bereavement' | 'unpaid' | 'maternity' | 'other'

const MONTH_NAMES = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
]

type StatusTab = 'all' | 'pending' | 'urgent' | 'conflict' | 'approved' | 'rejected'

type DateRange = 'this-month' | 'next-30' | 'next-90' | 'past-month' | 'all-time'

const STATUS_TABS: { value: StatusTab; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'pending', label: 'Pending' },
    { value: 'urgent', label: '⚠ Urgent' },
    { value: 'conflict', label: '⚠ Conflicts' },
    { value: 'approved', label: 'Approved' },
    { value: 'rejected', label: 'Rejected' },
]

/* ─── helpers ───────────────────────────────────────────────────────────── */

function leaveTypeKey(name?: string | null): TypeKey {
    const n = (name ?? '').toLowerCase()
    if (n.includes('annual') || n.includes('vacation')) return 'annual'
    if (n.includes('sick')) return 'sick'
    if (n.includes('personal')) return 'personal'
    if (n.includes('bereavement')) return 'bereavement'
    if (n.includes('unpaid')) return 'unpaid'
    if (n.includes('maternity') || n.includes('paternity') || n.includes('parental')) return 'maternity'
    return 'other'
}

function iconFor(name?: string | null) {
    const n = (name ?? '').toLowerCase()
    for (const k in LEAVE_ICONS) if (n.includes(k)) return LEAVE_ICONS[k]
    return '📅'
}

function initials(name: string) {
    const parts = (name ?? '').trim().split(/\s+/)
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

function avatarBg(name: string) {
    const palette = ['#4F8EF7', '#22C47A', '#F59E0B', '#8B5CF6', '#EC4899', '#06B6D4', '#84CC16', '#FF4D4F']
    let hash = 0
    for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) | 0
    return palette[Math.abs(hash) % palette.length]
}

function isoDate(d: Date) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function fmtShort(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
}

function fmtDateTime(iso: string) {
    const d = new Date(iso)
    return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) +
        ', ' + d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
}

function daysFromToday(iso: string) {
    const t = new Date(); t.setHours(0, 0, 0, 0)
    const d = new Date(iso); d.setHours(0, 0, 0, 0)
    return Math.round((d.getTime() - t.getTime()) / 86_400_000)
}

function overlaps(a: AnnualLeave, b: AnnualLeave) {
    return a.id !== b.id
        && a.startDate <= b.endDate
        && a.endDate >= b.startDate
        && (b.status === 'Pending' || b.status === 'Approved')
}

/* ═══════════════════════════════════════════════════════════════════════ */
/* Main page                                                                */
/* ═══════════════════════════════════════════════════════════════════════ */

const AllLeaveAdminPage = observer(function AllLeaveAdminPage({ user: _user }: { user: UserInfo }) {
    const queryClient = useQueryClient()
    const today = useMemo(() => { const d = new Date(); d.setHours(0, 0, 0, 0); return d }, [])

    const [statusTab, setStatusTab] = useState<StatusTab>('all')
    const [deptFilter, setDeptFilter] = useState<string>('all')
    const [typeFilter, setTypeFilter] = useState<string>('all')
    const [dateRange, setDateRange] = useState<DateRange>('this-month')
    const [searchText, setSearchText] = useState('')
    const [selected, setSelected] = useState<Set<string>>(new Set())
    const [expanded, setExpanded] = useState<Set<string>>(new Set())
    const [calMonth, setCalMonth] = useState(today.getMonth())
    const [calYear, setCalYear] = useState(today.getFullYear())
    const [apiError, setApiError] = useState('')

    const { data: leaves = [], isLoading } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: departmentList = [] } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: histories = [] } = useQuery({ queryKey: ['leaveStatusHistories'], queryFn: getLeaveStatusHistories })
    const { data: holidays = [] } = useQuery({
        queryKey: ['holidays', calYear],
        queryFn: () => getHolidays(calYear),
        staleTime: 60 * 60 * 1000,
    })

    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])
    const profileByUserId = useMemo(() => new Map(profiles.map((p) => [p.userId, p])), [profiles])

    const departments = useMemo(
        () => Array.from(new Set(leaves.map((l) => l.departmentName).filter(Boolean))).sort(),
        [leaves]
    )

    /* Detect conflicts (overlapping in same dept, both pending/approved) */
    const conflictMap = useMemo(() => {
        const map = new Map<string, AnnualLeave[]>()
        for (const a of leaves) {
            if (a.status !== 'Pending' && a.status !== 'Approved') continue
            const collisions = leaves.filter((b) =>
                b.departmentName === a.departmentName && overlaps(a, b)
            )
            if (collisions.length > 0) map.set(a.id, collisions)
        }
        return map
    }, [leaves])

    /* Status histories indexed by leave id (latest with comment) */
    const lastHistory = useMemo(() => {
        const map = new Map<string, LeaveStatusHistory>()
        for (const h of histories) {
            const prev = map.get(h.annualLeaveId)
            if (!prev || new Date(h.changedAt) > new Date(prev.changedAt)) map.set(h.annualLeaveId, h)
        }
        return map
    }, [histories])

    /* Per-leave: detect "urgent" — submitted < 24h before start, and still pending */
    function isUrgent(l: AnnualLeave) {
        if (l.status !== 'Pending') return false
        const start = new Date(l.startDate)
        const created = new Date(l.createdAt)
        return start.getTime() - created.getTime() < 86_400_000 && start.getTime() >= Date.now() - 86_400_000
    }

    /* Apply filters */
    const filtered = useMemo(() => {
        let out = leaves.slice()

        if (statusTab === 'pending') out = out.filter((l) => l.status === 'Pending')
        else if (statusTab === 'urgent') out = out.filter(isUrgent)
        else if (statusTab === 'conflict') out = out.filter((l) => conflictMap.has(l.id))
        else if (statusTab === 'approved') out = out.filter((l) => l.status === 'Approved')
        else if (statusTab === 'rejected') out = out.filter((l) => l.status === 'Rejected')

        if (deptFilter !== 'all') out = out.filter((l) => l.departmentName === deptFilter)

        if (typeFilter !== 'all') {
            const tid = Number(typeFilter)
            out = out.filter((l) => l.leaveTypeId === tid)
        }

        const now = new Date(); now.setHours(0, 0, 0, 0)
        if (dateRange === 'this-month') {
            const monthStart = new Date(now.getFullYear(), now.getMonth(), 1)
            const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0)
            out = out.filter((l) => new Date(l.endDate) >= monthStart && new Date(l.startDate) <= monthEnd)
        } else if (dateRange === 'next-30') {
            const limit = new Date(now); limit.setDate(limit.getDate() + 30)
            out = out.filter((l) => new Date(l.endDate) >= now && new Date(l.startDate) <= limit)
        } else if (dateRange === 'next-90') {
            const limit = new Date(now); limit.setDate(limit.getDate() + 90)
            out = out.filter((l) => new Date(l.endDate) >= now && new Date(l.startDate) <= limit)
        } else if (dateRange === 'past-month') {
            const start = new Date(now); start.setDate(start.getDate() - 30)
            out = out.filter((l) => new Date(l.endDate) >= start && new Date(l.startDate) <= now)
        }

        if (searchText.trim()) {
            const q = searchText.trim().toLowerCase()
            out = out.filter((l) => l.employeeName.toLowerCase().includes(q))
        }

        return out.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    }, [leaves, statusTab, deptFilter, typeFilter, dateRange, searchText, conflictMap])

    /* Counts (computed against raw leaves, NOT filtered) */
    const counts = useMemo(() => {
        const c = {
            all: leaves.length,
            pending: leaves.filter((l) => l.status === 'Pending').length,
            approved: leaves.filter((l) => l.status === 'Approved').length,
            rejected: leaves.filter((l) => l.status === 'Rejected').length,
            urgent: leaves.filter(isUrgent).length,
            conflict: conflictMap.size,
        }
        return c
    }, [leaves, conflictMap])

    /* Stats */
    const daysOffThisMonth = useMemo(() => {
        const m0 = new Date(today.getFullYear(), today.getMonth(), 1)
        const m1 = new Date(today.getFullYear(), today.getMonth() + 1, 0)
        return leaves
            .filter((l) => l.status === 'Approved' && new Date(l.endDate) >= m0 && new Date(l.startDate) <= m1)
            .reduce((sum, l) => sum + l.totalDays, 0)
    }, [leaves, today])

    const onLeaveToday = useMemo(
        () => leaves.filter((l) => l.status === 'Approved' && new Date(l.startDate) <= today && new Date(l.endDate) >= today).length,
        [leaves, today]
    )

    /* Heatmap data — leave count per ISO date in current calendar month */
    const heatmap = useMemo(() => {
        const map = new Map<string, { count: number; people: string[] }>()
        const monthStart = new Date(calYear, calMonth, 1)
        const monthEnd = new Date(calYear, calMonth + 1, 0)
        for (const l of leaves) {
            if (l.status !== 'Pending' && l.status !== 'Approved') continue
            const start = new Date(l.startDate); start.setHours(0, 0, 0, 0)
            const end = new Date(l.endDate); end.setHours(23, 59, 59, 999)
            if (end < monthStart || start > monthEnd) continue
            for (let d = new Date(Math.max(start.getTime(), monthStart.getTime())); d <= end && d <= monthEnd; d.setDate(d.getDate() + 1)) {
                const iso = isoDate(d)
                const cur = map.get(iso) ?? { count: 0, people: [] }
                cur.count++
                if (!cur.people.includes(l.employeeName)) cur.people.push(l.employeeName)
                map.set(iso, cur)
            }
        }
        return map
    }, [leaves, calMonth, calYear])

    /* Dept breakdown */
    const deptStats = useMemo(() => {
        const deptNameById = new Map(departmentList.map((d) => [d.id, d.name]))
        const map = new Map<string, { total: number; used: number; pending: number; entitled: number }>()
        for (const p of profiles) {
            const dept = deptNameById.get(p.departmentId) ?? 'Unassigned'
            const cur = map.get(dept) ?? { total: 0, used: 0, pending: 0, entitled: 0 }
            cur.total++
            cur.entitled += p.annualLeaveEntitlement > 0 ? p.annualLeaveEntitlement : 20
            map.set(dept, cur)
        }
        const year = today.getFullYear()
        for (const l of leaves) {
            const dept = l.departmentName ?? 'Unassigned'
            const cur = map.get(dept)
            if (!cur) continue
            if (l.status === 'Approved' && new Date(l.startDate).getFullYear() === year) {
                cur.used += l.totalDays
            } else if (l.status === 'Pending') {
                cur.pending++
            }
        }
        return Array.from(map.entries())
            .map(([name, v]) => ({ name, ...v }))
            .filter((d) => d.total > 0)
            .sort((a, b) => a.name.localeCompare(b.name))
    }, [profiles, leaves, departmentList, today])

    const totalUsed = deptStats.reduce((s, d) => s + d.used, 0)
    const totalAllowance = deptStats.reduce((s, d) => s + d.entitled, 0)

    /* Mutations */
    const approveMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Approved'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
        onError: (err) => setApiError(getApiErrorMessage(err, 'Approval failed.')),
    })
    const rejectMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Rejected'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
        onError: (err) => setApiError(getApiErrorMessage(err, 'Rejection failed.')),
    })
    const isWorking = approveMut.isPending || rejectMut.isPending

    function toggleSelected(id: string) {
        setSelected((prev) => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id); else next.add(id)
            return next
        })
    }

    function toggleExpanded(id: string) {
        setExpanded((prev) => {
            const next = new Set(prev)
            if (next.has(id)) next.delete(id); else next.add(id)
            return next
        })
    }

    async function bulkApprove() {
        for (const id of selected) await approveMut.mutateAsync(id).catch(() => {})
        setSelected(new Set())
    }
    async function bulkReject() {
        for (const id of selected) await rejectMut.mutateAsync(id).catch(() => {})
        setSelected(new Set())
    }

    function navMonth(delta: number) {
        let m = calMonth + delta, y = calYear
        if (m < 0) { m = 11; y-- } else if (m > 11) { m = 0; y++ }
        setCalMonth(m); setCalYear(y)
    }

    if (isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }

    const pending = filtered.filter((l) => l.status === 'Pending')
    const decided = filtered.filter((l) => l.status !== 'Pending')
    const showHeatmapAlert = Array.from(heatmap.entries())
        .map(([iso, v]) => ({ iso, ...v }))
        .filter((d) => d.count >= 3)
        .sort((a, b) => b.count - a.count)[0]

    return (
        <Box>
            {apiError && (
                <Alert severity="error" onClose={() => setApiError('')} sx={{ mb: 2 }}>{apiError}</Alert>
            )}

            {/* Summary stats */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <StatCard label="⏳ Awaiting Review" value={String(counts.pending)} valueColor="#F59E0B" sub="leave requests pending" />
                <StatCard label="⚠️ Need Attention" value={String(counts.urgent + counts.conflict)} valueColor="#FF4D4F" sub={`${counts.urgent} urgent · ${counts.conflict} conflicts`} />
                <StatCard label="📅 Days Off This Month" value={String(daysOffThisMonth)} valueColor={C_BLUE} sub="across all departments" />
                <StatCard label="🏖️ Currently On Leave" value={String(onLeaveToday)} valueColor="#22C47A" sub="employees out today" />
            </Box>

            {/* Heatmap calendar */}
            <Heatmap
                month={calMonth}
                year={calYear}
                heatmap={heatmap}
                holidays={new Set(holidays.map((h) => h.date.slice(0, 10)))}
                today={today}
                onNav={navMonth}
                alert={showHeatmapAlert}
            />

            {/* Dept breakdown */}
            <DeptBreakdown stats={deptStats} totalUsed={totalUsed} totalAllowance={totalAllowance} onFilter={(d) => setDeptFilter(d)} />

            {/* Bulk action bar */}
            {selected.size > 0 && (
                <BulkBar
                    count={selected.size}
                    onClear={() => setSelected(new Set())}
                    onApprove={() => void bulkApprove()}
                    onReject={() => void bulkReject()}
                    disabled={isWorking}
                />
            )}

            {/* Status tabs */}
            <Box sx={{ display: 'flex', gap: '2px', mb: '14px', borderBottom: `1px solid ${C_BORDER}`, px: '2px', flexWrap: 'wrap' }}>
                {STATUS_TABS.map((tab) => {
                    const active = statusTab === tab.value
                    const c =
                        tab.value === 'all' ? counts.all :
                        tab.value === 'pending' ? counts.pending :
                        tab.value === 'urgent' ? counts.urgent :
                        tab.value === 'conflict' ? counts.conflict :
                        tab.value === 'approved' ? counts.approved :
                        counts.rejected
                    const dangerTone = tab.value === 'urgent' && c > 0
                    const warnTone = tab.value === 'conflict' && c > 0
                    return (
                        <Box
                            key={tab.value}
                            component="button"
                            onClick={() => setStatusTab(tab.value)}
                            sx={{
                                p: '9px 16px', fontSize: 13,
                                color: active ? C_BLUE : dangerTone ? '#991B1B' : warnTone ? '#92400E' : C_MUTED,
                                cursor: 'pointer',
                                borderBottom: active ? `2px solid ${C_BLUE}` : '2px solid transparent',
                                mb: '-1px', display: 'flex', alignItems: 'center', gap: '6px',
                                background: 'none', border: 'none', fontFamily: 'inherit',
                                fontWeight: active ? 600 : 500,
                                '&:hover': { color: active ? C_BLUE : C_HEADING },
                            }}
                        >
                            {tab.label}
                            <Box component="span" sx={{
                                bgcolor: active ? '#EEF4FF' : dangerTone ? '#FEE2E2' : warnTone ? '#FEF3C7' : '#F4F5F7',
                                color: active ? C_BLUE : dangerTone ? '#991B1B' : warnTone ? '#92400E' : C_MUTED,
                                fontSize: 10, fontWeight: 600,
                                px: '7px', borderRadius: '10px',
                            }}>{c}</Box>
                        </Box>
                    )
                })}
            </Box>

            {/* Filter toolbar */}
            <Box sx={{
                bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '10px',
                p: '10px 12px', display: 'flex', gap: '10px', flexWrap: 'wrap',
                alignItems: 'center', mb: '14px',
            }}>
                <Box sx={{ flex: 1, minWidth: 180 }}>
                    <Box
                        component="input"
                        type="search"
                        placeholder="Search by name…"
                        value={searchText}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchText(e.target.value)}
                        sx={{
                            width: '100%', p: '7px 10px', fontSize: 13, fontFamily: 'inherit',
                            border: `1px solid ${C_BORDER}`, borderRadius: '6px', outline: 'none',
                            '&:focus': { borderColor: C_BLUE, boxShadow: '0 0 0 3px rgba(79,142,247,0.1)' },
                        }}
                    />
                </Box>
                <SelectFilter value={deptFilter} onChange={setDeptFilter} options={[
                    { value: 'all', label: 'All departments' },
                    ...departments.map((d) => ({ value: d, label: d })),
                ]} />
                <SelectFilter value={typeFilter} onChange={setTypeFilter} options={[
                    { value: 'all', label: 'All leave types' },
                    ...leaveTypes.filter((lt) => lt.isActive).map((lt) => ({ value: String(lt.id), label: `${iconFor(lt.name)} ${lt.name}` })),
                ]} />
                <SelectFilter value={dateRange} onChange={(v) => setDateRange(v as DateRange)} options={[
                    { value: 'this-month', label: 'This month' },
                    { value: 'next-30', label: 'Next 30 days' },
                    { value: 'next-90', label: 'Next 90 days' },
                    { value: 'past-month', label: 'Past month' },
                    { value: 'all-time', label: 'All time' },
                ]} />
            </Box>

            {/* Pending section */}
            {pending.length > 0 && (
                <SectionHeader title="⏳ Awaiting Review" subtitle={`${pending.length} request${pending.length === 1 ? '' : 's'}`}
                               meta={`${counts.urgent > 0 ? `${counts.urgent} urgent · ` : ''}${counts.conflict > 0 ? `${counts.conflict} with conflicts` : 'priority review'}`} />
            )}
            {pending.map((l) => (
                <LeaveRow
                    key={l.id}
                    leave={l}
                    leaveTypeById={leaveTypeById}
                    profile={profileByUserId.get(l.employeeId)}
                    isExpanded={expanded.has(l.id)}
                    isSelected={selected.has(l.id)}
                    isUrgent={isUrgent(l)}
                    conflicts={conflictMap.get(l.id)}
                    history={histories.filter((h) => h.annualLeaveId === l.id).sort((a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime())}
                    lastHistory={lastHistory.get(l.id)}
                    leaves={leaves}
                    onToggleExpand={() => toggleExpanded(l.id)}
                    onToggleSelect={() => toggleSelected(l.id)}
                    onApprove={() => approveMut.mutate(l.id)}
                    onReject={() => rejectMut.mutate(l.id)}
                    disabled={isWorking}
                />
            ))}

            {/* Decided section */}
            {decided.length > 0 && (
                <SectionHeader title="📋 Recently Decided" subtitle={`${decided.length} result${decided.length === 1 ? '' : 's'}`}
                               meta={`${filtered.filter((l) => l.status === 'Approved').length} approved · ${filtered.filter((l) => l.status === 'Rejected').length} rejected`} />
            )}
            {decided.map((l) => (
                <LeaveRow
                    key={l.id}
                    leave={l}
                    leaveTypeById={leaveTypeById}
                    profile={profileByUserId.get(l.employeeId)}
                    isExpanded={expanded.has(l.id)}
                    isSelected={false}
                    isUrgent={false}
                    conflicts={conflictMap.get(l.id)}
                    history={histories.filter((h) => h.annualLeaveId === l.id).sort((a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime())}
                    lastHistory={lastHistory.get(l.id)}
                    leaves={leaves}
                    onToggleExpand={() => toggleExpanded(l.id)}
                    onToggleSelect={() => toggleSelected(l.id)}
                    onApprove={() => approveMut.mutate(l.id)}
                    onReject={() => rejectMut.mutate(l.id)}
                    disabled={isWorking}
                    hideCheckbox
                />
            ))}

            {filtered.length === 0 && (
                <Box sx={{
                    bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '10px',
                    py: 6, textAlign: 'center', color: C_MUTED, fontSize: 13,
                }}>
                    No leave requests match the current filters.
                </Box>
            )}
        </Box>
    )
})

export default AllLeaveAdminPage

/* ═══════════════════════════════════════════════════════════════════════ */
/* Subcomponents                                                            */
/* ═══════════════════════════════════════════════════════════════════════ */

function StatCard({ label, value, sub, valueColor }: {
    label: string; value: string; sub: string; valueColor?: string
}) {
    return (
        <Box sx={{ bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '10px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: C_MUTED, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 22, fontWeight: 700, color: valueColor ?? C_HEADING, lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 11, color: C_MUTED, mt: '4px' }}>{sub}</Box>
        </Box>
    )
}

function Heatmap({ month, year, heatmap, holidays, today, onNav, alert }: {
    month: number
    year: number
    heatmap: Map<string, { count: number; people: string[] }>
    holidays: Set<string>
    today: Date
    onNav: (delta: number) => void
    alert: { iso: string; count: number; people: string[] } | undefined
}) {
    const firstOfMonth = new Date(year, month, 1)
    const startDow = firstOfMonth.getDay()
    const startOffset = startDow === 0 ? 6 : startDow - 1
    const daysInMonth = new Date(year, month + 1, 0).getDate()
    const todayIso = isoDate(today)

    const cells: React.ReactNode[] = []
    for (let i = 0; i < startOffset; i++) cells.push(<Box key={`b-${i}`} sx={{ aspectRatio: '1' }} />)
    for (let d = 1; d <= daysInMonth; d++) {
        const date = new Date(year, month, d)
        const iso = isoDate(date)
        const dow = date.getDay()
        const weekend = dow === 0 || dow === 6
        const data = heatmap.get(iso)
        const holiday = holidays.has(iso)
        const conflictCount = data && data.count >= 3 ? data.count : 0
        const count = data?.count ?? 0
        const isToday = iso === todayIso

        let bg = '#F9FAFB'
        let color = C_HEADING
        if (weekend) { bg = '#F4F5F7'; color = '#C7C7CC' }
        if (holiday) { bg = '#FEF3C7'; color = '#92400E' }
        if (count === 1) { bg = '#DBEAFE'; color = '#1D4ED8' }
        else if (count === 2) { bg = '#BFDBFE'; color = '#1D4ED8' }
        else if (count === 3) { bg = '#93C5FD'; color = '#1D4ED8' }
        else if (count >= 4) { bg = C_BLUE; color = '#fff' }
        if (conflictCount > 0) { bg = '#FEF3C7'; color = '#92400E' }

        cells.push(
            <Box
                key={iso}
                title={data ? `${d} ${MONTH_NAMES[month]}: ${data.count} on leave (${data.people.join(', ')})` : `${d} ${MONTH_NAMES[month]}`}
                sx={{
                    aspectRatio: '1', bgcolor: bg, color, borderRadius: '6px',
                    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                    fontSize: 11, position: 'relative',
                    border: holiday ? '1px solid #F59E0B' : 'none',
                    boxShadow: isToday ? `inset 0 0 0 2px ${C_HEADING}` : conflictCount > 0 ? 'inset 0 0 0 1px #F59E0B' : 'none',
                    cursor: count > 0 ? 'help' : 'default',
                }}
            >
                <Box sx={{ fontWeight: isToday ? 700 : 500 }}>{d}</Box>
                {count > 0 && <Box sx={{ fontSize: 9, fontWeight: 700, mt: '2px' }}>{count}</Box>}
                {conflictCount > 0 && (
                    <Box component="span" sx={{ position: 'absolute', top: 2, right: 3, fontSize: 9 }}>⚠</Box>
                )}
            </Box>
        )
    }

    return (
        <Box sx={{ bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '12px', p: '14px 18px', mb: '14px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '10px', mb: '12px' }}>
                <Box>
                    <Box sx={{ fontSize: 13, fontWeight: 600, color: C_HEADING }}>{MONTH_NAMES[month]} {year} · Leave Calendar</Box>
                    <Box sx={{ fontSize: 11, color: C_MUTED, mt: '2px' }}>Click a request below to see details</Box>
                </Box>
                <Box sx={{ display: 'flex', gap: '14px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <Box sx={{ display: 'flex', gap: '10px', fontSize: 10, color: C_MUTED, flexWrap: 'wrap' }}>
                        <Legend color="#F9FAFB" label="None" bordered />
                        <Legend color="#DBEAFE" label="1" />
                        <Legend color="#BFDBFE" label="2" />
                        <Legend color="#93C5FD" label="3" />
                        <Legend color={C_BLUE} label="4+" />
                        <Legend color="#FEF3C7" label="⚠ Conflict" bordered borderColor="#F59E0B" />
                        <Legend color="#FEF3C7" label="🎉 Holiday" bordered borderColor="#F59E0B" />
                    </Box>
                    <Box sx={{ display: 'flex', gap: '4px' }}>
                        <CalNavBtn onClick={() => onNav(-1)}>‹</CalNavBtn>
                        <CalNavBtn onClick={() => onNav(1)}>›</CalNavBtn>
                    </Box>
                </Box>
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '4px', mb: '4px' }}>
                {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((d) => (
                    <Box key={d} sx={{ textAlign: 'center', fontSize: 10, color: '#9CA3AF', fontWeight: 600, textTransform: 'uppercase' }}>{d}</Box>
                ))}
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '4px' }}>
                {cells}
            </Box>
            {alert && (
                <Box sx={{
                    mt: '12px', p: '10px 14px', bgcolor: '#FFFBEB',
                    border: '1px solid #FDE68A', borderRadius: '8px',
                    display: 'flex', alignItems: 'flex-start', gap: '8px',
                    fontSize: 12, color: '#92400E',
                }}>
                    <Box component="span">⚠️</Box>
                    <Box>
                        <Box component="strong">{fmtShort(alert.iso)}:</Box>{' '}
                        {alert.count} employees on leave that day ({alert.people.join(', ')}).
                        Could impact multiple departments — review carefully.
                    </Box>
                </Box>
            )}
        </Box>
    )
}

function Legend({ color, label, bordered, borderColor }: {
    color: string; label: string; bordered?: boolean; borderColor?: string
}) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{
                width: 12, height: 12, borderRadius: '2px', bgcolor: color,
                border: bordered ? `1px solid ${borderColor ?? C_BORDER}` : 'none',
                display: 'inline-block',
            }} />
            {label}
        </Box>
    )
}

function CalNavBtn({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                width: 28, height: 28, border: `1px solid ${C_BORDER}`, bgcolor: '#fff',
                borderRadius: '5px', cursor: 'pointer', fontSize: 14, color: C_MUTED, fontFamily: 'inherit',
                '&:hover': { bgcolor: '#F4F5F7', color: C_HEADING },
            }}
        >
            {children}
        </Box>
    )
}

function DeptBreakdown({ stats, totalUsed, totalAllowance, onFilter }: {
    stats: { name: string; total: number; used: number; pending: number; entitled: number }[]
    totalUsed: number
    totalAllowance: number
    onFilter: (dept: string) => void
}) {
    return (
        <Box sx={{ bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '12px', p: '14px 18px', mb: '14px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '12px', flexWrap: 'wrap', gap: '4px' }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: C_HEADING }}>Leave by Department</Box>
                <Box sx={{ fontSize: 11, color: C_MUTED }}>YTD · {totalUsed} of {totalAllowance} days used</Box>
            </Box>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                {stats.length === 0 ? (
                    <Box sx={{ fontSize: 12, color: C_MUTED, py: '6px' }}>No departments configured.</Box>
                ) : stats.map((d) => {
                    const pct = d.entitled > 0 ? (d.used / d.entitled) * 100 : 0
                    const fillColor = pct >= 75 ? '#FF4D4F' : pct >= 50 ? '#F59E0B' : '#22C47A'
                    return (
                        <Box key={d.name} sx={{
                            display: 'grid',
                            gridTemplateColumns: { xs: '1fr', sm: '140px 1fr 130px auto' },
                            gap: '10px', alignItems: 'center',
                        }}>
                            <Box>
                                <Box sx={{ fontSize: 12, fontWeight: 600, color: C_HEADING }}>{d.name}</Box>
                                <Box sx={{ fontSize: 11, color: '#9CA3AF' }}>{d.total} {d.total === 1 ? 'person' : 'people'}</Box>
                            </Box>
                            <Box sx={{ position: 'relative', height: 22, bgcolor: '#F4F5F7', borderRadius: '4px', overflow: 'hidden' }}>
                                <Box sx={{
                                    height: '100%', bgcolor: fillColor, width: `${Math.min(100, pct)}%`,
                                    display: 'flex', alignItems: 'center', justifyContent: 'flex-end',
                                    pr: '8px', fontSize: 10, color: '#fff', fontWeight: 600,
                                }}>
                                    {pct >= 14 && `${d.used}d · ${Math.round(pct)}%`}
                                </Box>
                            </Box>
                            <Box sx={{ fontSize: 12, color: C_MUTED }}>
                                {d.pending > 0
                                    ? <><Box component="strong" sx={{ color: '#F59E0B' }}>{d.pending}</Box> pending</>
                                    : <Box component="span" sx={{ color: '#22C47A' }}>✓ None pending</Box>}
                            </Box>
                            <Box
                                component="button"
                                onClick={() => onFilter(d.name)}
                                sx={{
                                    fontSize: 11, color: C_BLUE, bgcolor: 'transparent',
                                    border: 'none', cursor: 'pointer', fontFamily: 'inherit',
                                    '&:hover': { textDecoration: 'underline' },
                                }}
                            >
                                Filter
                            </Box>
                        </Box>
                    )
                })}
            </Box>
        </Box>
    )
}

function BulkBar({ count, onClear, onApprove, onReject, disabled }: {
    count: number; onClear: () => void; onApprove: () => void; onReject: () => void; disabled: boolean
}) {
    return (
        <Box sx={{
            position: 'sticky', top: 0, zIndex: 5,
            bgcolor: C_HEADING, color: '#fff', borderRadius: '10px',
            p: '10px 14px', display: 'flex', alignItems: 'center', gap: '14px',
            mb: '14px', flexWrap: 'wrap',
            boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
        }}>
            <Box sx={{ fontSize: 13 }}>
                <Box component="strong">{count}</Box> leave request{count === 1 ? '' : 's'} selected
            </Box>
            <Box sx={{ ml: 'auto', display: 'flex', gap: '8px' }}>
                <Box
                    component="button"
                    onClick={onClear}
                    disabled={disabled}
                    sx={{
                        bgcolor: 'transparent', color: '#fff', border: '1px solid #fff',
                        px: '12px', py: '5px', borderRadius: '6px', fontSize: 12, fontWeight: 500,
                        cursor: 'pointer', fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { bgcolor: 'rgba(255,255,255,0.1)' },
                        '&:disabled': { opacity: 0.5 },
                    }}
                >Clear</Box>
                <Box
                    component="button"
                    onClick={onApprove}
                    disabled={disabled}
                    sx={{
                        bgcolor: '#22C47A', color: '#fff', border: 'none',
                        px: '14px', py: '6px', borderRadius: '6px', fontSize: 12, fontWeight: 600,
                        cursor: 'pointer', fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { bgcolor: '#18A867' },
                        '&:disabled': { opacity: 0.5 },
                    }}
                >✓ Approve Selected</Box>
                <Box
                    component="button"
                    onClick={onReject}
                    disabled={disabled}
                    sx={{
                        bgcolor: '#FF4D4F', color: '#fff', border: 'none',
                        px: '14px', py: '6px', borderRadius: '6px', fontSize: 12, fontWeight: 600,
                        cursor: 'pointer', fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { bgcolor: '#E03C3E' },
                        '&:disabled': { opacity: 0.5 },
                    }}
                >✕ Reject Selected</Box>
            </Box>
        </Box>
    )
}

function SelectFilter({ value, onChange, options }: {
    value: string
    onChange: (v: string) => void
    options: { value: string; label: string }[]
}) {
    return (
        <Box
            component="select"
            value={value}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => onChange(e.target.value)}
            sx={{
                fontSize: 12, fontFamily: 'inherit', p: '7px 10px',
                border: `1px solid ${C_BORDER}`, borderRadius: '6px',
                color: '#374151', bgcolor: '#fff', outline: 'none', cursor: 'pointer',
                '&:focus': { borderColor: C_BLUE },
            }}
        >
            {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </Box>
    )
}

function SectionHeader({ title, subtitle, meta }: { title: string; subtitle?: string; meta?: string }) {
    return (
        <Box sx={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'center',
            mt: '18px', mx: '4px', mb: '10px', flexWrap: 'wrap', gap: '6px',
        }}>
            <Box>
                <Box sx={{
                    fontSize: 12, fontWeight: 600, color: C_HEADING,
                    textTransform: 'uppercase', letterSpacing: '0.05em',
                }}>
                    {title}
                    {subtitle && <Box component="span" sx={{ color: C_MUTED, fontWeight: 500, ml: '8px' }}>· {subtitle}</Box>}
                </Box>
            </Box>
            {meta && <Box sx={{ fontSize: 11, color: C_MUTED }}>{meta}</Box>}
        </Box>
    )
}

function LeaveRow({
    leave, leaveTypeById, profile, isExpanded, isSelected, isUrgent,
    conflicts, history, lastHistory, leaves,
    onToggleExpand, onToggleSelect, onApprove, onReject, disabled, hideCheckbox,
}: {
    leave: AnnualLeave
    leaveTypeById: Map<number, LeaveType>
    profile?: EmployeeProfile
    isExpanded: boolean
    isSelected: boolean
    isUrgent: boolean
    conflicts?: AnnualLeave[]
    history: LeaveStatusHistory[]
    lastHistory?: LeaveStatusHistory
    leaves: AnnualLeave[]
    onToggleExpand: () => void
    onToggleSelect: () => void
    onApprove: () => void
    onReject: () => void
    disabled: boolean
    hideCheckbox?: boolean
}) {
    const typeName = leave.leaveTypeId != null ? leaveTypeById.get(leave.leaveTypeId)?.name : 'Annual'
    const typeKey = leaveTypeKey(typeName)
    const isPending = leave.status === 'Pending'
    const hasConflict = !!conflicts && conflicts.length > 0
    const accent = isUrgent ? '#FF4D4F' : hasConflict ? '#F59E0B'
        : isPending ? '#F59E0B'
        : leave.status === 'Approved' ? '#22C47A'
        : leave.status === 'Rejected' ? '#FF4D4F' : '#9CA3AF'

    // Balance computation
    const usedThisYear = useMemo(() => {
        const year = new Date().getFullYear()
        return leaves
            .filter((l) => l.employeeId === leave.employeeId && l.status === 'Approved' && new Date(l.startDate).getFullYear() === year)
            .reduce((sum, l) => {
                const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined
                return sum + (lt?.affectsBalance === false ? 0 : l.totalDays)
            }, 0)
    }, [leaves, leave.employeeId, leaveTypeById])

    const entitlement = profile?.annualLeaveEntitlement ?? 0
    const balAfter = entitlement - usedThisYear - (leave.status === 'Pending' ? leave.totalDays : 0)
    const balPct = entitlement > 0 ? Math.min(100, (usedThisYear / entitlement) * 100) : 0
    const fillColor = balPct >= 95 ? '#FF4D4F' : balPct >= 80 ? '#F59E0B' : '#22C47A'

    const daysUntil = daysFromToday(leave.startDate)
    const noticeText = daysUntil < 0 ? 'Past' : daysUntil === 0 ? 'Today' : daysUntil === 1 ? 'Tomorrow' : `${daysUntil} days notice`

    return (
        <Box sx={{
            bgcolor: '#fff', border: `1px solid ${C_BORDER}`,
            borderLeft: `3px solid ${accent}`,
            borderRadius: '10px', mb: '8px',
            ...(isSelected && { boxShadow: `inset 0 0 0 2px ${C_BLUE}`, bgcolor: '#F0F7FF' }),
        }}>
            <Box
                onClick={onToggleExpand}
                sx={{
                    display: 'grid',
                    gridTemplateColumns: {
                        xs: '24px 1fr auto',
                        md: '24px 220px 120px 200px 150px 130px auto',
                    },
                    gap: '12px', alignItems: 'center',
                    p: '14px 16px', cursor: 'pointer',
                    '&:hover': { bgcolor: isSelected ? '#F0F7FF' : '#F9FAFB' },
                }}
            >
                {hideCheckbox ? <Box /> : (
                    <Box
                        component="input"
                        type="checkbox"
                        checked={isSelected}
                        disabled={!isPending}
                        onChange={onToggleSelect}
                        onClick={(e: React.MouseEvent) => e.stopPropagation()}
                        sx={{
                            cursor: isPending ? 'pointer' : 'not-allowed',
                            width: 16, height: 16,
                            accentColor: C_BLUE,
                            opacity: isPending ? 1 : 0.3,
                        }}
                    />
                )}

                {/* Person */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0 }}>
                    <Box sx={{
                        width: 36, height: 36, borderRadius: '50%',
                        bgcolor: avatarBg(leave.employeeName), color: '#fff',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 12, fontWeight: 600, flexShrink: 0,
                    }}>{initials(leave.employeeName)}</Box>
                    <Box sx={{ minWidth: 0 }}>
                        <Box sx={{ fontSize: 13, fontWeight: 600, color: C_HEADING, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {leave.employeeName}
                        </Box>
                        <Box sx={{ display: 'inline-block', mt: '2px', bgcolor: '#EFF6FF', color: '#1D4ED8', borderRadius: '4px', px: '6px', py: '1px', fontSize: 10, fontWeight: 500 }}>
                            {leave.departmentName || '—'}
                        </Box>
                    </Box>
                </Box>

                {/* Type pill */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    <Box component="span" sx={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        bgcolor: typeColors[typeKey].bg, color: typeColors[typeKey].fg,
                        fontSize: 11, fontWeight: 500, px: '8px', py: '3px',
                        borderRadius: '12px', whiteSpace: 'nowrap',
                    }}>
                        {iconFor(typeName)} {typeName ?? '—'}
                    </Box>
                </Box>

                {/* Dates */}
                <Box sx={{ display: { xs: 'none', md: 'block' }, minWidth: 0 }}>
                    <Box sx={{ fontSize: 12, color: C_HEADING, fontWeight: 600 }}>
                        {leave.startDate.slice(0, 10) === leave.endDate.slice(0, 10)
                            ? fmtShort(leave.startDate)
                            : `${fmtShort(leave.startDate)} – ${fmtShort(leave.endDate)}`}
                        <Box component="span" sx={{
                            display: 'inline-block', ml: '6px', bgcolor: '#F4F5F7',
                            color: '#374151', px: '6px', py: '1px', borderRadius: '8px',
                            fontSize: 10, fontWeight: 500,
                        }}>{leave.totalDays} {leave.totalDays === 1 ? 'day' : 'days'}</Box>
                    </Box>
                    <Box sx={{ fontSize: 10, color: C_MUTED, mt: '3px' }}>
                        {noticeText}
                        {isUrgent && <Box component="span" sx={{ color: '#991B1B', ml: '6px' }}>· ⚠ urgent</Box>}
                        {hasConflict && <Box component="span" sx={{ color: '#92400E', ml: '6px' }}>· ⚠ {conflicts!.length} overlap{conflicts!.length === 1 ? '' : 's'}</Box>}
                        {leave.evidenceUrl && <Box component="span" sx={{ ml: '6px' }}>· 📎</Box>}
                    </Box>
                </Box>

                {/* Balance */}
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                    {entitlement > 0 ? (
                        <>
                            <Box sx={{ fontSize: 11, color: C_MUTED }}>{usedThisYear}/{entitlement} used</Box>
                            <Box sx={{ height: 4, bgcolor: '#F4F5F7', borderRadius: '2px', overflow: 'hidden', mt: '4px' }}>
                                <Box sx={{ height: '100%', bgcolor: fillColor, width: `${balPct}%` }} />
                            </Box>
                            <Box sx={{ fontSize: 10, color: C_MUTED, mt: '4px' }}>
                                <Box component="span" sx={{
                                    fontWeight: 600,
                                    color: balAfter < 0 ? '#FF4D4F' : balAfter <= 3 ? '#F59E0B' : C_HEADING,
                                }}>{balAfter}</Box> left after
                            </Box>
                        </>
                    ) : (
                        <Box sx={{ fontSize: 11, color: '#9CA3AF' }}>—</Box>
                    )}
                </Box>

                {/* Submitted */}
                <Box sx={{ display: { xs: 'none', md: 'block' }, fontSize: 11, color: C_MUTED }}>
                    <Box sx={{ fontWeight: 600, color: '#374151' }}>{fmtShort(leave.createdAt)}</Box>
                    {!isPending && lastHistory && (
                        <Box sx={{ mt: '2px' }}>
                            {leave.status === 'Approved' ? '✓' : '✕'} by {lastHistory.changedByUserName}
                        </Box>
                    )}
                </Box>

                {/* Actions */}
                <Box
                    onClick={(e: React.MouseEvent) => e.stopPropagation()}
                    sx={{ display: 'flex', gap: '6px', justifyContent: 'flex-end', flexShrink: 0 }}
                >
                    {isPending ? (
                        <>
                            <ActionBtn variant="success" onClick={onApprove} disabled={disabled}>Approve</ActionBtn>
                            <ActionBtn variant="danger" onClick={onReject} disabled={disabled}>Reject</ActionBtn>
                        </>
                    ) : (
                        <ActionBtn variant="ghost" onClick={(e) => { e.stopPropagation(); onToggleExpand() }}>
                            {isExpanded ? 'Hide' : 'View'}
                        </ActionBtn>
                    )}
                </Box>
            </Box>

            {isExpanded && (
                <Box sx={{
                    px: '16px', py: '14px', borderTop: '1px solid #F3F4F6',
                    bgcolor: '#FAFBFC',
                    display: 'grid',
                    gridTemplateColumns: { xs: '1fr', md: '1fr 1fr 1fr' },
                    gap: '14px',
                }}>
                    <ExpandBlock title="Reason given">
                        {leave.reason ? (
                            <Box sx={{ fontSize: 12, fontStyle: 'italic', color: '#374151', lineHeight: 1.5 }}>"{leave.reason}"</Box>
                        ) : (
                            <Box component="em" sx={{ fontSize: 12, color: '#9CA3AF' }}>No reason provided</Box>
                        )}
                        {leave.evidenceUrl && (
                            <Box sx={{ mt: '8px' }}>
                                <Box
                                    component="a"
                                    href={leave.evidenceUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    onClick={(e: React.MouseEvent) => e.stopPropagation()}
                                    sx={{
                                        display: 'inline-flex', alignItems: 'center', gap: '6px',
                                        p: '4px 10px 4px 6px', bgcolor: '#fff',
                                        border: `1px solid ${C_BORDER}`, borderRadius: '14px',
                                        fontSize: 11, color: '#374151', textDecoration: 'none',
                                        '&:hover': { bgcolor: '#EEF4FF', borderColor: C_BLUE, color: C_BLUE },
                                    }}
                                >
                                    <Box component="span" sx={{ width: 18, height: 18, borderRadius: '50%', bgcolor: '#FEE2E2', color: '#991B1B', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 10 }}>📄</Box>
                                    View attachment
                                </Box>
                            </Box>
                        )}
                    </ExpandBlock>

                    {hasConflict ? (
                        <ExpandBlock title="⚠️ Overlapping leave">
                            {conflicts!.slice(0, 4).map((c) => (
                                <Box key={c.id} sx={{ display: 'flex', alignItems: 'center', gap: '8px', mb: '6px' }}>
                                    <Box sx={{
                                        width: 26, height: 26, borderRadius: '50%',
                                        bgcolor: avatarBg(c.employeeName), color: '#fff',
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        fontSize: 10, fontWeight: 600, flexShrink: 0,
                                    }}>{initials(c.employeeName)}</Box>
                                    <Box sx={{ minWidth: 0 }}>
                                        <Box sx={{ fontSize: 12, fontWeight: 500, color: C_HEADING }}>{c.employeeName}</Box>
                                        <Box sx={{ fontSize: 10, color: C_MUTED }}>{fmtShort(c.startDate)} – {fmtShort(c.endDate)}</Box>
                                    </Box>
                                </Box>
                            ))}
                            <Box sx={{ mt: '8px', pt: '8px', borderTop: '1px dashed #E4E6EA', fontSize: 11, color: '#92400E' }}>
                                Check coverage in {leave.departmentName ?? 'this department'} carefully.
                            </Box>
                        </ExpandBlock>
                    ) : (
                        <ExpandBlock title="✓ Coverage">
                            <Box sx={{ fontSize: 12, color: '#065F46', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Box component="span" sx={{ color: '#22C47A' }}>●</Box>
                                No conflicts in {leave.departmentName ?? 'this department'}
                            </Box>
                            <Box sx={{ fontSize: 11, color: C_MUTED, mt: '6px' }}>
                                No overlapping approved or pending leave on these dates.
                            </Box>
                        </ExpandBlock>
                    )}

                    <ExpandBlock title="Timeline">
                        <TimelineEntry when={fmtDateTime(leave.createdAt)} what={`${leave.employeeName} submitted request`} />
                        {history.map((h) => (
                            <TimelineEntry
                                key={h.id}
                                when={fmtDateTime(h.changedAt)}
                                what={`${h.newStatus} by ${h.changedByUserName}${h.comment ? ` — "${h.comment}"` : ''}`}
                            />
                        ))}
                    </ExpandBlock>
                </Box>
            )}

            {!isExpanded && leave.status === 'Rejected' && lastHistory?.comment && (
                <Box sx={{
                    mx: '16px', mb: '14px', p: '8px 12px',
                    bgcolor: '#FEE2E2', borderLeft: '3px solid #FF4D4F',
                    borderRadius: '6px', fontSize: 12, color: '#991B1B',
                }}>
                    <Box component="strong">{lastHistory.changedByUserName}:</Box> "{lastHistory.comment}"
                </Box>
            )}
        </Box>
    )
}

function ExpandBlock({ title, children }: { title: string; children: React.ReactNode }) {
    return (
        <Box sx={{ bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '8px', p: '12px 14px' }}>
            <Box sx={{ fontSize: 11, fontWeight: 600, color: C_MUTED, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '8px' }}>
                {title}
            </Box>
            {children}
        </Box>
    )
}

function TimelineEntry({ when, what }: { when: string; what: string }) {
    return (
        <Box sx={{ display: 'flex', gap: '10px', fontSize: 11, mb: '6px', '&:last-child': { mb: 0 } }}>
            <Box sx={{ color: C_MUTED, minWidth: 110, flexShrink: 0 }}>{when}</Box>
            <Box sx={{ color: '#374151' }}>{what}</Box>
        </Box>
    )
}

function ActionBtn({ variant, onClick, disabled, children }: {
    variant: 'success' | 'danger' | 'ghost'
    onClick: (e: React.MouseEvent) => void
    disabled?: boolean
    children: React.ReactNode
}) {
    const styles =
        variant === 'success' ? { bg: '#22C47A', color: '#fff', hover: '#18A867', border: 'none' } :
        variant === 'danger'  ? { bg: '#FF4D4F', color: '#fff', hover: '#E03C3E', border: 'none' } :
                                 { bg: 'transparent', color: C_MUTED, hover: '#F4F5F7', border: `1px solid ${C_BORDER}` }
    return (
        <Box
            component="button"
            onClick={onClick}
            disabled={disabled}
            sx={{
                bgcolor: styles.bg, color: styles.color, border: styles.border,
                borderRadius: '6px', px: '12px', py: '5px',
                fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
                whiteSpace: 'nowrap',
                '&:hover:not(:disabled)': { bgcolor: styles.hover },
                '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
            }}
        >
            {children}
        </Box>
    )
}

const typeColors: Record<TypeKey, { bg: string; fg: string }> = {
    annual:      { bg: '#DBEAFE', fg: '#1D4ED8' },
    sick:        { bg: '#FEE2E2', fg: '#991B1B' },
    personal:    { bg: '#E0E7FF', fg: '#5B21B6' },
    bereavement: { bg: '#E5E7EB', fg: '#374151' },
    unpaid:      { bg: '#F3F4F6', fg: '#6B7280' },
    maternity:   { bg: '#FCE7F3', fg: '#9D174D' },
    other:       { bg: '#F3F4F6', fg: '#6B7280' },
}
