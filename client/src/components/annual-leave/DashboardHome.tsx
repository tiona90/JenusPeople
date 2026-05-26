import { useMemo } from 'react'
import { useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import {
    approveTimesheet, getAdminUsers, getAnnualLeaves, getAppSettings,
    getCompanyAttendance, getDepartments, getEmployeeProfiles, getLeaveTypes,
    getMyTimesheets, getTeamAttendance, getTimesheets, rejectTimesheet, updateLeaveStatus,
} from '../../lib/api'
import { useStore } from '../../lib/mobx'
import { AdminUsersPanel, AppSettingsPanel, DepartmentsPanel, LeaveTypesPanel, ProjectsPanel } from '..'
import type {
    AnnualLeave, AnnualLeaveStatus, AttendanceIssue, DepartmentAttendance,
    LeaveType, RecentActivity, TeamAttendance, TeamMemberAttendance, Timesheet, TimesheetStatus, UserInfo,
} from '../../lib/types'

const C_BORDER = '#E4E6EA'
const C_HEADING = '#1A1A2E'
const C_MUTED = '#6B7280'
const C_BLUE = '#4F8EF7'

const WEEKLY_TARGET = 40

const LEAVE_ICONS: Record<string, string> = {
    annual: '🌴', vacation: '🌴',
    sick: '🤒',
    personal: '🏠',
    bereavement: '🕊️',
    unpaid: '💼',
    maternity: '👶', paternity: '👶', parental: '👶',
}

const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']

function greetingForHour(h: number) {
    return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening'
}

function formatTodayLong() {
    return new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })
}

function firstName(user: UserInfo) {
    return (user.displayName || user.userName || 'there').trim().split(/\s+/)[0]
}

function initials(name: string) {
    const parts = name.trim().split(/\s+/)
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

function iconForLeaveType(name?: string | null) {
    const n = (name ?? '').toLowerCase()
    for (const k in LEAVE_ICONS) if (n.includes(k)) return LEAVE_ICONS[k]
    return '📅'
}

function formatDateShort(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
}

function formatRange(s: string, e: string) {
    return s === e ? formatDateShort(s) : `${formatDateShort(s)} – ${formatDateShort(e)}`
}

function daysBetween(a: Date, b: Date) {
    return Math.round((b.getTime() - a.getTime()) / 86_400_000)
}

function nextWorkingDay(iso: string) {
    const d = new Date(iso); d.setDate(d.getDate() + 1)
    while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() + 1)
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function dateInRange(d: Date, startIso: string, endIso: string) {
    const s = new Date(startIso); s.setHours(0, 0, 0, 0)
    const e = new Date(endIso); e.setHours(23, 59, 59, 999)
    return d >= s && d <= e
}

function minutesToHm(mins: number) {
    const h = Math.floor(mins / 60), m = Math.floor(mins % 60)
    return h > 0 ? `${h}h ${m}m` : `${m}m`
}

const DashboardHome = observer(function DashboardHome() {
    const { authStore } = useStore()
    const location = useLocation()
    const user = authStore.user
    if (!user) return null

    const isAdmin = user.roles.includes('Admin')
    const isManager = user.roles.includes('Manager') && !isAdmin

    // Admin sub-routes are derived from the URL (was: uiStore.adminSection).
    const adminSection = location.pathname.startsWith('/admin/')
        ? location.pathname.split('/')[2]
        : null
    if (isAdmin && adminSection === 'users') return <AdminUsersPanel />
    if (isAdmin && adminSection === 'departments') return <DepartmentsPanel />
    if (isAdmin && (adminSection === 'leave-types' || adminSection === 'leave')) return <LeaveTypesPanel />
    if (isAdmin && adminSection === 'projects') return <ProjectsPanel />
    if (isAdmin && adminSection === 'settings') return <AppSettingsPanel />

    if (isAdmin) return <AdminDashboard user={user} />
    if (isManager) return <ManagerDashboard user={user} />
    return <EmployeeDashboard user={user} />
})

export default DashboardHome

/* ════════════════════════════════════════════════════════════════════════ */
/* EMPLOYEE                                                                 */
/* ════════════════════════════════════════════════════════════════════════ */

function EmployeeDashboard({ user }: { user: UserInfo }) {
    const { uiStore } = useStore()
    const today = useMemo(() => { const d = new Date(); d.setHours(0, 0, 0, 0); return d }, [])

    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: leaves = [], isLoading: isLoadingLeaves } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: timesheets = [], isLoading: isLoadingTs } = useQuery({ queryKey: ['timesheets', 'mine'], queryFn: getMyTimesheets })
    const { data: settings } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })

    const isLoading = isLoadingLeaves || isLoadingTs

    const myProfile = profiles.find((p) => p.userId === user.id)
    const entitlement = myProfile?.annualLeaveEntitlement ?? 0
    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])

    const currentYear = today.getFullYear()
    const myApprovedThisYear = useMemo(
        () => leaves.filter((l) => l.employeeId === user.id && l.status === 'Approved' && new Date(l.startDate).getFullYear() === currentYear),
        [leaves, user.id, currentYear]
    )

    const balanceUsed = useMemo(() =>
        myApprovedThisYear.reduce((s, l) => {
            const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined
            return s + (lt?.affectsBalance === false ? 0 : l.totalDays)
        }, 0)
    , [myApprovedThisYear, leaveTypeById])

    const balanceRemaining = Math.max(0, entitlement - balanceUsed)
    const myPendingLeaves = leaves.filter((l) => l.employeeId === user.id && l.status === 'Pending')
    const myRejectedTs = timesheets.filter((t) => t.status === 'Rejected')

    // Current week timesheet
    const currentTimesheet = useMemo(
        () => timesheets.find((t) => dateInRange(today, t.periodStart, t.periodEnd)) ?? null,
        [timesheets, today]
    )
    const currentHours = currentTimesheet ? Number(currentTimesheet.totalHours) : 0
    const hoursRemaining = Math.max(0, WEEKLY_TARGET - currentHours)

    // Days left until Friday 6pm of the current week
    const fridayEod = useMemo(() => {
        if (!currentTimesheet) return null
        const fri = new Date(currentTimesheet.periodEnd); fri.setHours(18, 0, 0, 0)
        return Math.max(0, Math.ceil((fri.getTime() - Date.now()) / 86_400_000))
    }, [currentTimesheet])

    // Streak of approved + on-time submissions (most recent backwards)
    const streak = useMemo(() => {
        const approved = [...timesheets].filter((t) => t.status === 'Approved' && t.submittedAt)
            .sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime())
        let count = 0
        for (const t of approved) {
            const end = new Date(t.periodEnd); end.setHours(23, 59, 59, 999)
            if (new Date(t.submittedAt!) <= end) count++
            else break
        }
        return count
    }, [timesheets])

    // Next upcoming leave (pending or approved, start >= today)
    const nextLeave: AnnualLeave | null = useMemo(() => {
        return [...leaves]
            .filter((l) => l.employeeId === user.id && (l.status === 'Pending' || l.status === 'Approved') && new Date(l.startDate) >= today)
            .sort((a, b) => new Date(a.startDate).getTime() - new Date(b.startDate).getTime())[0] ?? null
    }, [leaves, user.id, today])

    // Days until year end
    const yearEndDays = useMemo(() => {
        const startMonth = (settings?.leaveYearStartMonth ?? 1) - 1
        const startYear = today.getMonth() >= startMonth ? today.getFullYear() : today.getFullYear() - 1
        const lyEnd = new Date(startYear + 1, startMonth, 0)
        return Math.max(0, daysBetween(today, lyEnd))
    }, [settings, today])

    // Attention items
    const attentionItems = useMemo(() => {
        const items: AttentionItem[] = []
        if (currentTimesheet && currentHours < WEEKLY_TARGET) {
            const filled = (currentTimesheet.dailyHours ?? []).filter((h) => h > 0).length
            const missing = 5 - filled
            items.push({
                icon: '📝',
                label: `Finish this week's timesheet`,
                sub: `${missing} day${missing === 1 ? '' : 's'} still empty · ${hoursRemaining.toFixed(1)}h to log${fridayEod !== null ? ` · due in ${fridayEod} day${fridayEod === 1 ? '' : 's'}` : ''}`,
                tone: hoursRemaining > 16 ? 'urgent' : 'normal',
                onClick: () => uiStore.navigateToNewTimesheet(),
            })
        }
        for (const t of myRejectedTs.slice(0, 2)) {
            items.push({
                icon: '⚠️',
                label: `Fix rejected timesheet (${formatRange(t.periodStart, t.periodEnd)})`,
                sub: `Manager rejected — open and resubmit`,
                tone: 'urgent',
                onClick: () => uiStore.navigateToTimesheets(),
            })
        }
        for (const l of myPendingLeaves.slice(0, 2)) {
            const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId)?.name : 'Leave'
            items.push({
                icon: '⏳',
                label: `${lt} (${formatRange(l.startDate, l.endDate)}) awaiting approval`,
                sub: `Submitted ${new Date(l.createdAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })} · ${l.totalDays} day${l.totalDays === 1 ? '' : 's'}`,
                tone: 'normal',
                onClick: () => uiStore.navigateToMyLeave('requests'),
            })
        }
        return items
    }, [currentTimesheet, currentHours, hoursRemaining, fridayEod, myRejectedTs, myPendingLeaves, leaveTypeById, uiStore])

    if (isLoading) return <CenterSpinner />

    const summary = buildEmployeeSummary({
        currentHours, hoursRemaining, balanceRemaining, nextLeave, today, pendingCount: myPendingLeaves.length,
    })

    return (
        <Box>
            <GreetingHero
                gradient="linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)"
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name={`Hi ${firstName(user)} 👋`}
                summary={summary}
                meta={[
                    { l: 'This week', v: `${currentHours.toFixed(1)} / ${WEEKLY_TARGET}h` },
                    { l: 'Leave remaining', v: `${balanceRemaining} days` },
                    { l: 'Pending', v: `${myPendingLeaves.length} request${myPendingLeaves.length === 1 ? '' : 's'}` },
                    { l: 'Streak', v: streak > 0 ? `🔥 ${streak} week${streak === 1 ? '' : 's'} on-time` : '—' },
                ]}
            />

            {attentionItems.length > 0 && (
                <ActionCard title="Things needing your attention" icon="⚡" countLabel={`${attentionItems.length} item${attentionItems.length === 1 ? '' : 's'}`} countTone="urgent">
                    {attentionItems.map((item, i) => (
                        <AttentionRow key={i} item={item} />
                    ))}
                </ActionCard>
            )}

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                {currentTimesheet
                    ? <ThisWeekCard ts={currentTimesheet} hoursRemaining={hoursRemaining} todayDow={today.getDay()} onContinue={() => uiStore.navigateToNewTimesheet()} />
                    : <NoCurrentWeek onOpen={() => uiStore.navigateToNewTimesheet()} />}

                {nextLeave
                    ? <NextLeaveCard leave={nextLeave} typeName={nextLeave.leaveTypeId != null ? leaveTypeById.get(nextLeave.leaveTypeId)?.name : undefined} today={today} />
                    : <EmptyNextLeave onApply={() => uiStore.navigateToApplyLeave()} />}
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <ActionCard
                    title="Leave balance"
                    icon="📅"
                    action={<OutlineBtn onClick={() => uiStore.navigateToMyLeave('requests')}>View all</OutlineBtn>}
                >
                    <LeaveBalanceList
                        leaveTypes={leaveTypes}
                        approvedThisYear={myApprovedThisYear}
                        leaveTypeById={leaveTypeById}
                        entitlement={entitlement}
                    />
                </ActionCard>

                <ActionCard title="Quick actions" icon="⚡">
                    <QuickActions tiles={[
                        { icon: '📝', label: 'This week', sub: 'Log hours', onClick: () => uiStore.navigateToNewTimesheet() },
                        { icon: '🌴', label: 'Apply for leave', sub: 'Book time off', onClick: () => uiStore.navigateToApplyLeave() },
                        { icon: '📅', label: 'My leave', sub: 'View history', onClick: () => uiStore.navigateToMyLeave('requests') },
                        { icon: '🕐', label: 'My timesheets', sub: 'Past weeks', onClick: () => uiStore.navigateToTimesheets() },
                    ]} />
                </ActionCard>
            </Box>

            <Box sx={{ fontSize: 11, color: '#9CA3AF', textAlign: 'center', mt: '8px' }}>
                {yearEndDays} days left in this leave year.
            </Box>
        </Box>
    )
}

function buildEmployeeSummary({ currentHours, hoursRemaining, balanceRemaining, nextLeave, today, pendingCount }: {
    currentHours: number; hoursRemaining: number; balanceRemaining: number
    nextLeave: AnnualLeave | null; today: Date; pendingCount: number
}) {
    const parts: React.ReactNode[] = []
    if (hoursRemaining > 0) {
        parts.push(<>You're <strong>{hoursRemaining.toFixed(1)} hours</strong> away from finishing this week's timesheet</>)
    } else if (currentHours >= WEEKLY_TARGET) {
        parts.push(<>This week's timesheet is on target</>)
    }
    if (nextLeave) {
        const start = new Date(nextLeave.startDate); start.setHours(0, 0, 0, 0)
        const until = daysBetween(today, start)
        const lbl = until === 0 ? 'today' : until === 1 ? 'tomorrow' : `in ${until} days`
        parts.push(<>your next time off is <strong>{lbl}</strong></>)
    } else {
        parts.push(<>you have <strong>{balanceRemaining} days</strong> of leave to book</>)
    }
    if (pendingCount > 0) {
        parts.push(<>{pendingCount} request{pendingCount === 1 ? ' is' : 's are'} waiting for approval</>)
    }
    return joinParts(parts)
}

function joinParts(parts: React.ReactNode[]) {
    return parts.reduce<React.ReactNode[]>((acc, p, i) => {
        if (i > 0) acc.push(i === parts.length - 1 ? ' and ' : ', ')
        acc.push(p)
        return acc
    }, [])
}

/* ════════════════════════════════════════════════════════════════════════ */
/* MANAGER                                                                  */
/* ════════════════════════════════════════════════════════════════════════ */

function ManagerDashboard({ user }: { user: UserInfo }) {
    const { uiStore } = useStore()
    const queryClient = useQueryClient()
    const today = useMemo(() => new Date(), [])

    const { data: leaves = [], isLoading: lLoading } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: timesheets = [], isLoading: tLoading } = useQuery({ queryKey: ['timesheets'], queryFn: getTimesheets })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: team } = useQuery({ queryKey: ['attendance', 'team'], queryFn: getTeamAttendance })

    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])

    // Pending items NOT submitted by manager themselves
    const pendingLeaves = useMemo(
        () => leaves.filter((l) => l.status === 'Pending' && l.employeeId !== user.id)
            .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
        [leaves, user.id]
    )
    const pendingTs = useMemo(
        () => timesheets.filter((t) => (t.status === 'Submitted' || t.status === 'Resubmitted') && t.employeeId !== user.id)
            .sort((a, b) => new Date(a.submittedAt ?? a.createdAt).getTime() - new Date(b.submittedAt ?? b.createdAt).getTime()),
        [timesheets, user.id]
    )

    // Detect conflicts: leave requests overlapping same dept on same dates
    const conflictMap = useMemo(() => {
        const result = new Map<string, string[]>() // leaveId → list of overlapping names
        for (const a of pendingLeaves) {
            const overlapping = leaves.filter((b) =>
                b.id !== a.id
                && b.departmentName === a.departmentName
                && (b.status === 'Pending' || b.status === 'Approved')
                && b.startDate <= a.endDate && b.endDate >= a.startDate
            )
            if (overlapping.length > 0) {
                result.set(a.id, overlapping.map((b) => b.employeeName))
            }
        }
        return result
    }, [pendingLeaves, leaves])

    // Mutations
    const approveLeaveMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Approved'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
    })
    const rejectLeaveMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Rejected'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
    })
    const approveTsMut = useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['timesheets'] }),
    })
    const rejectTsMut = useMutation({
        mutationFn: (id: string) => rejectTimesheet(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['timesheets'] }),
    })
    const isMutating = approveLeaveMut.isPending || rejectLeaveMut.isPending || approveTsMut.isPending || rejectTsMut.isPending

    // Build queue: merge leaves + timesheets, sort by age
    const queue = useMemo(() => {
        const items: QueueItem[] = []
        for (const l of pendingLeaves) {
            const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined
            const startD = new Date(l.startDate)
            const daysNotice = Math.round((startD.getTime() - Date.now()) / 86_400_000)
            const tags: QueueTag[] = []
            if (daysNotice >= 0 && daysNotice < 1) tags.push({ label: '⚠ < 1 day notice', tone: 'urgent' })
            if (l.evidenceUrl) tags.push({ label: '📎 Document attached', tone: 'info' })
            const conflicts = conflictMap.get(l.id)
            if (conflicts && conflicts.length > 0) tags.push({ label: `⚠ Overlaps with ${conflicts[0]}`, tone: 'conflict' })
            items.push({
                kind: 'leave',
                id: l.id,
                name: l.employeeName,
                title: `${l.employeeName} · ${lt?.name ?? 'Leave'}`,
                meta: `${iconForLeaveType(lt?.name)} ${l.totalDays} day${l.totalDays === 1 ? '' : 's'} · ${formatRange(l.startDate, l.endDate)}`,
                tags,
                createdAt: l.createdAt,
                urgent: daysNotice >= 0 && daysNotice < 1,
            })
        }
        for (const t of pendingTs) {
            const hours = Number(t.totalHours)
            const tags: QueueTag[] = []
            if (hours < WEEKLY_TARGET * 0.9) tags.push({ label: 'Under target', tone: 'warning' })
            const submittedAt = t.submittedAt ? new Date(t.submittedAt) : null
            const periodEnd = new Date(t.periodEnd); periodEnd.setHours(23, 59, 59, 999)
            const isLate = submittedAt ? submittedAt > periodEnd : false
            if (isLate) tags.push({ label: 'Late submission', tone: 'warning' })
            for (const p of (t.projectSummaries ?? []).slice(0, 2)) {
                tags.push({ label: `${p.code} · ${Number(p.hours).toFixed(0)}h`, tone: 'info' })
            }
            items.push({
                kind: 'timesheet',
                id: t.id,
                name: t.employeeName,
                title: `${t.employeeName} · Timesheet · ${formatRange(t.periodStart, t.periodEnd)}`,
                meta: `📋 ${hours.toFixed(1)}h logged${hours >= WEEKLY_TARGET ? ' ✓' : ' (under target)'}`,
                tags,
                createdAt: t.submittedAt ?? t.createdAt,
                urgent: isLate,
            })
        }
        // Sort: urgent first, then by oldest
        items.sort((a, b) => {
            if (a.urgent !== b.urgent) return a.urgent ? -1 : 1
            return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
        })
        return items
    }, [pendingLeaves, pendingTs, leaveTypeById, conflictMap])

    // Team submissions this week
    const teamSubmissions = useMemo(() => {
        if (!team) return { submitted: 0, total: 0, missing: [] as { name: string; note: string }[] }
        const teammates = team.members
        // Find current week range
        const weekStart = new Date(today)
        const dow = weekStart.getDay()
        const offset = dow === 0 ? -6 : 1 - dow
        weekStart.setDate(weekStart.getDate() + offset); weekStart.setHours(0, 0, 0, 0)
        const weekEnd = new Date(weekStart); weekEnd.setDate(weekEnd.getDate() + 4); weekEnd.setHours(23, 59, 59, 999)

        const submitted = new Set<string>()
        for (const t of timesheets) {
            if (t.status === 'Submitted' || t.status === 'Resubmitted' || t.status === 'Approved') {
                const start = new Date(t.periodStart)
                if (start >= weekStart && start <= weekEnd) submitted.add(t.employeeId)
            }
        }
        const memberByName = new Map(teammates.map((m) => [m.employeeName, m]))
        const memberProfilesById = new Map(profiles.map((p) => [p.userId, p]))
        const missing: { name: string; note: string }[] = []
        for (const m of teammates) {
            const profile = [...memberProfilesById.values()].find((p) => p.displayName === m.employeeName)
            const userId = profile?.userId
            if (!userId) continue
            if (submitted.has(userId)) continue
            // Find any in-progress (draft) for current week
            const draft = timesheets.find((t) => t.employeeId === userId && t.status === 'Draft' && new Date(t.periodStart) >= weekStart && new Date(t.periodStart) <= weekEnd)
            const note = draft
                ? `Currently ${Number(draft.totalHours).toFixed(0)}h logged · in progress`
                : (() => {
                    const last = [...timesheets].filter((t) => t.employeeId === userId && t.submittedAt)
                        .sort((a, b) => new Date(b.submittedAt!).getTime() - new Date(a.submittedAt!).getTime())[0]
                    return last ? `Last submitted: ${formatDateShort(last.submittedAt!)}` : 'No timesheets yet'
                })()
            missing.push({ name: m.employeeName, note })
            // Mark seen so memberByName doesn't get used elsewhere
            memberByName.delete(m.employeeName)
        }
        return { submitted: submitted.size, total: teammates.length, missing: missing.slice(0, 5) }
    }, [team, timesheets, profiles, today])

    if (lLoading || tLoading) return <CenterSpinner />

    const summary = buildManagerSummary({ pendingLeaves: pendingLeaves.length, pendingTs: pendingTs.length, urgent: queue.filter((q) => q.urgent).length, conflicts: conflictMap.size })

    return (
        <Box>
            <GreetingHero
                gradient="linear-gradient(135deg, #1A1A2E 0%, #4F8EF7 100%)"
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name={`Hi ${firstName(user)} 👋`}
                summary={summary}
                meta={[
                    { l: 'Team size', v: `${team?.members.length ?? 0} people` },
                    { l: 'Working now', v: team ? `${team.members.filter((m) => m.status === 'in').length} of ${team.members.length}` : '—' },
                    { l: 'Approvals due', v: `${pendingLeaves.length + pendingTs.length} items` },
                    { l: 'On leave', v: team ? `${team.members.filter((m) => m.status === 'leave').length}` : '—' },
                ]}
            />

            <ApprovalQueueCard
                queue={queue.slice(0, 5)}
                totalQueue={queue.length}
                onApprove={(item) => item.kind === 'leave' ? approveLeaveMut.mutate(item.id) : approveTsMut.mutate(item.id)}
                onReject={(item) => item.kind === 'leave' ? rejectLeaveMut.mutate(item.id) : rejectTsMut.mutate(item.id)}
                disabled={isMutating}
                onViewAllLeave={() => uiStore.navigateToTeamLeave()}
                onViewAllTs={() => uiStore.navigateToTeamTimesheets()}
            />

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <TeamStatusNowCard team={team ?? null} />
                <TeamSubmissionsCard
                    submitted={teamSubmissions.submitted}
                    total={teamSubmissions.total}
                    missing={teamSubmissions.missing}
                />
            </Box>

            <ActionCard title="Quick actions" icon="⚡">
                <QuickActions tiles={[
                    { icon: '📅', label: 'Team leave', sub: `${pendingLeaves.length} pending`, onClick: () => uiStore.navigateToTeamLeave() },
                    { icon: '📋', label: 'Team timesheets', sub: `${pendingTs.length} pending`, onClick: () => uiStore.navigateToTeamTimesheets() },
                    { icon: '📝', label: 'My timesheet', sub: 'For this week', onClick: () => uiStore.navigateToNewTimesheet() },
                    { icon: '🌴', label: 'Apply leave', sub: 'For yourself', onClick: () => uiStore.navigateToApplyLeave() },
                ]} />
            </ActionCard>
        </Box>
    )
}

function buildManagerSummary({ pendingLeaves, pendingTs, urgent, conflicts }: {
    pendingLeaves: number; pendingTs: number; urgent: number; conflicts: number
}) {
    const total = pendingLeaves + pendingTs
    if (total === 0) return <>Your team is up to date — no items waiting for review.</>
    const parts: React.ReactNode[] = []
    parts.push(<>Your team has <strong>{total} item{total === 1 ? '' : 's'}</strong> waiting for review — <strong>{pendingLeaves} leave request{pendingLeaves === 1 ? '' : 's'}</strong> and <strong>{pendingTs} timesheet{pendingTs === 1 ? '' : 's'}</strong></>)
    if (urgent > 0) parts.push(<><strong>{urgent} {urgent === 1 ? 'is urgent' : 'are urgent'}</strong></>)
    if (conflicts > 0) parts.push(<>{conflicts} conflict{conflicts === 1 ? '' : 's'} need{conflicts === 1 ? 's' : ''} attention</>)
    return joinParts(parts)
}

/* ════════════════════════════════════════════════════════════════════════ */
/* ADMIN                                                                    */
/* ════════════════════════════════════════════════════════════════════════ */

function AdminDashboard({ user: _user }: { user: UserInfo }) {
    const { uiStore } = useStore()
    const today = useMemo(() => new Date(), [])

    const { data: leaves = [], isLoading: lLoading } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: timesheets = [], isLoading: tLoading } = useQuery({ queryKey: ['timesheets'], queryFn: getTimesheets })
    const { data: departments = [] } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: adminUsers = [] } = useQuery({ queryKey: ['adminUsers'], queryFn: getAdminUsers })
    const { data: company } = useQuery({ queryKey: ['attendance', 'company'], queryFn: getCompanyAttendance })

    const pendingLeaveCount = leaves.filter((l) => l.status === 'Pending').length
    const pendingTsCount = timesheets.filter((t) => t.status === 'Submitted' || t.status === 'Resubmitted').length
    const totalApprovals = pendingLeaveCount + pendingTsCount

    const overThreeDaysOld = useMemo(() => {
        const cutoff = Date.now() - 3 * 86_400_000
        const oldLeaves = leaves.filter((l) => l.status === 'Pending' && new Date(l.createdAt).getTime() < cutoff).length
        const oldTs = timesheets.filter((t) => (t.status === 'Submitted' || t.status === 'Resubmitted') && new Date(t.submittedAt ?? t.createdAt).getTime() < cutoff).length
        return oldLeaves + oldTs
    }, [leaves, timesheets])

    const totalUsers = adminUsers.filter((u) => u.roles.includes('Employee') || u.roles.includes('Manager')).length
    const activeDepts = departments.filter((d) => d.isActive).length

    // On-time submissions over last 4 weeks
    const onTimePct = useMemo(() => {
        const cutoff = Date.now() - 28 * 86_400_000
        const recent = timesheets.filter((t) => t.submittedAt && new Date(t.submittedAt).getTime() > cutoff && (t.status === 'Approved' || t.status === 'Submitted'))
        if (recent.length === 0) return 0
        const onTime = recent.filter((t) => {
            const end = new Date(t.periodEnd); end.setHours(23, 59, 59, 999)
            return new Date(t.submittedAt!) <= end
        }).length
        return Math.round((onTime / recent.length) * 100)
    }, [timesheets])

    if (lLoading || tLoading) return <CenterSpinner />

    const attendancePct = company && company.total > 0 ? Math.round((company.in / company.total) * 100) : 0

    return (
        <Box>
            <GreetingHero
                gradient="linear-gradient(135deg, #4338CA 0%, #8B5CF6 100%)"
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name="Workspace overview"
                summary={
                    company
                        ? <><strong>{company.in} of {company.total}</strong> employees are working right now. <strong>{totalApprovals} approval{totalApprovals === 1 ? '' : 's'}</strong> {totalApprovals === 1 ? 'is' : 'are'} pending across all departments{company.out > 0 ? <>, with <strong>{company.out} not checked in</strong></> : ''}.</>
                        : <>Tracking <strong>{totalUsers}</strong> users across <strong>{activeDepts}</strong> departments.</>
                }
                meta={[
                    { l: 'Total users', v: `${totalUsers} active` },
                    { l: 'Working now', v: company ? `${company.in} (${attendancePct}%)` : '—' },
                    { l: 'On leave today', v: company ? `${company.leave}` : '—' },
                    { l: 'Departments', v: `${activeDepts} active` },
                ]}
            />

            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <Gauge label="Pending approvals" big={totalApprovals.toString()} bigColor="#F59E0B" sub={`${pendingLeaveCount} leave · ${pendingTsCount} timesheets · ${overThreeDaysOld} over 3 days old`} barColor="#F59E0B" barPct={Math.min(100, totalApprovals * 5)} />
                <Gauge label="Today's attendance" big={`${attendancePct}%`} bigColor="#22C47A" sub={company ? `${company.in} in · ${company.leave} on leave · ${company.out} not checked in` : '—'} barColor="#22C47A" barPct={attendancePct} />
                <Gauge label="On-time submissions" big={`${onTimePct}%`} bigColor={C_BLUE} sub="last 4 weeks · target 90%" barColor={C_BLUE} barPct={onTimePct} />
                <Gauge label="Active issues" big={`${company?.issues.filter((i) => i.severity === 'danger').length ?? 0}`} bigColor="#FF4D4F" sub={company ? `${company.out} not checked in` : '—'} barColor="#FF4D4F" barPct={Math.min(100, (company?.issues.length ?? 0) * 20)} />
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <DepartmentHealthCard departments={company?.departments ?? []} leaves={leaves} timesheets={timesheets} onLive={() => uiStore.navigateToCompanyAttendance()} />
                <TodaysIssuesCard issues={company?.issues ?? []} />
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <RecentActivityCard activity={company?.recent ?? []} />
                <ActionCard title="Administration" icon="⚡">
                    <QuickActions tiles={[
                        { icon: '📅', label: 'All leave', sub: `${pendingLeaveCount} pending`, onClick: () => uiStore.navigateToTeamLeave() },
                        { icon: '📋', label: 'All timesheets', sub: `${pendingTsCount} pending`, onClick: () => uiStore.navigateToTeamTimesheets() },
                        { icon: '🏢', label: 'Company attendance', sub: 'Live view', onClick: () => uiStore.navigateToCompanyAttendance() },
                        { icon: '👤', label: 'Users', sub: `${totalUsers} active`, onClick: () => uiStore.navigateToAdminSection('users') },
                        { icon: '🏬', label: 'Departments', sub: `${activeDepts} active`, onClick: () => uiStore.navigateToAdminSection('departments') },
                        { icon: '🏷️', label: 'Leave types', sub: 'Configure', onClick: () => uiStore.navigateToAdminSection('leave-types') },
                        { icon: '📁', label: 'Projects', sub: 'Configure', onClick: () => uiStore.navigateToAdminSection('projects') },
                        { icon: '📆', label: 'Leave year', sub: 'Settings', onClick: () => uiStore.navigateToAdminSection('settings') },
                    ]} />
                </ActionCard>
            </Box>
        </Box>
    )
}

/* ════════════════════════════════════════════════════════════════════════ */
/* SHARED COMPONENTS                                                        */
/* ════════════════════════════════════════════════════════════════════════ */

interface AttentionItem {
    icon: string
    label: string
    sub: string
    tone: 'urgent' | 'normal'
    onClick: () => void
}

interface QueueTag { label: string; tone: 'urgent' | 'warning' | 'info' | 'conflict' }
interface QueueItem {
    kind: 'leave' | 'timesheet'
    id: string
    name: string
    title: string
    meta: string
    tags: QueueTag[]
    createdAt: string
    urgent: boolean
}

function CenterSpinner() {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
            <CircularProgress size={28} />
        </Box>
    )
}

function GreetingHero({ gradient, hello, name, summary, meta }: {
    gradient: string
    hello: string
    name: string
    summary: React.ReactNode
    meta: { l: string; v: string }[]
}) {
    return (
        <Box sx={{
            background: gradient, color: '#fff', borderRadius: '14px',
            p: { xs: '20px', md: '24px 28px' }, mb: '14px',
            position: 'relative', overflow: 'hidden',
        }}>
            <Box sx={{ position: 'relative', zIndex: 1 }}>
                <Box sx={{ fontSize: 12, opacity: 0.85, mb: '4px' }}>{hello}</Box>
                <Box sx={{ fontSize: { xs: 22, md: 26 }, fontWeight: 700, mb: '10px' }}>{name}</Box>
                <Box sx={{ fontSize: 13, opacity: 0.95, mb: '18px', lineHeight: 1.5, maxWidth: 760 }}>{summary}</Box>
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, auto)' },
                    gap: { xs: '12px', sm: '28px' },
                }}>
                    {meta.map((m, i) => (
                        <Box key={i}>
                            <Box sx={{ opacity: 0.75, fontSize: 11, mb: '2px' }}>{m.l}</Box>
                            <Box sx={{ fontSize: 14, fontWeight: 600 }}>{m.v}</Box>
                        </Box>
                    ))}
                </Box>
            </Box>
        </Box>
    )
}

function ActionCard({ title, icon, action, countLabel, countTone, children }: {
    title: string
    icon?: string
    action?: React.ReactNode
    countLabel?: string
    countTone?: 'urgent' | 'normal'
    children: React.ReactNode
}) {
    return (
        <Box sx={{
            bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '12px',
            overflow: 'hidden', mb: '14px',
        }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: `1px solid ${C_BORDER}`,
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: 14, fontWeight: 600, color: C_HEADING }}>
                    {icon && <Box component="span">{icon}</Box>}
                    {title}
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    {countLabel && (
                        <Box component="span" sx={{
                            fontSize: 11, fontWeight: 600, px: '8px', py: '3px',
                            borderRadius: '12px',
                            bgcolor: countTone === 'urgent' ? '#FEE2E2' : '#F4F5F7',
                            color: countTone === 'urgent' ? '#991B1B' : C_MUTED,
                        }}>{countLabel}</Box>
                    )}
                    {action}
                </Box>
            </Box>
            <Box sx={{ p: '14px 18px' }}>{children}</Box>
        </Box>
    )
}

function AttentionRow({ item }: { item: AttentionItem }) {
    return (
        <Box
            onClick={item.onClick}
            sx={{
                display: 'flex', alignItems: 'center', gap: '12px',
                p: '12px 14px', borderRadius: '8px', cursor: 'pointer',
                bgcolor: item.tone === 'urgent' ? '#FFFBEB' : '#F9FAFB',
                border: `1px solid ${item.tone === 'urgent' ? '#FDE68A' : C_BORDER}`,
                mb: '8px', transition: 'all 0.15s',
                '&:last-child': { mb: 0 },
                '&:hover': { borderColor: C_BLUE, bgcolor: '#EEF4FF' },
            }}
        >
            <Box sx={{ fontSize: 20 }}>{item.icon}</Box>
            <Box sx={{ flex: 1, minWidth: 0 }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: C_HEADING, mb: '2px' }}>{item.label}</Box>
                <Box sx={{ fontSize: 11, color: C_MUTED }}>{item.sub}</Box>
            </Box>
            <Box sx={{ color: '#9CA3AF', fontSize: 18 }}>›</Box>
        </Box>
    )
}

function OutlineBtn({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: '#fff', color: C_BLUE, border: `1px solid ${C_BLUE}`,
                px: '12px', py: '5px', borderRadius: '6px', fontSize: 12, fontWeight: 500,
                cursor: 'pointer', fontFamily: 'inherit',
                '&:hover': { bgcolor: '#EEF4FF' },
            }}
        >
            {children}
        </Box>
    )
}

function ThisWeekCard({ ts, hoursRemaining, todayDow, onContinue }: {
    ts: Timesheet
    hoursRemaining: number
    todayDow: number
    onContinue: () => void
}) {
    const daily = ts.dailyHours ?? [0, 0, 0, 0, 0]
    const hours = Number(ts.totalHours)
    const pct = Math.min(100, (hours / WEEKLY_TARGET) * 100)
    // dayDow: Sun=0 .. Sat=6  → Mon=0..Fri=4
    const todayIdx = todayDow === 0 || todayDow === 6 ? -1 : todayDow - 1

    return (
        <Box sx={{ bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '12px', overflow: 'hidden' }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: `1px solid ${C_BORDER}`,
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: 14, fontWeight: 600, color: C_HEADING }}>
                    <Box component="span">⏱</Box>
                    This week · {formatRange(ts.periodStart, ts.periodEnd)}
                </Box>
                <Box
                    component="button"
                    onClick={onContinue}
                    sx={{
                        bgcolor: C_BLUE, color: '#fff', border: 'none', borderRadius: '6px',
                        px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', '&:hover': { bgcolor: '#3A7AE4' },
                    }}
                >
                    Continue →
                </Box>
            </Box>
            <Box sx={{ p: '14px 18px' }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '6px' }}>
                    <Box sx={{ fontSize: 24, fontWeight: 700, color: C_HEADING }}>
                        {hours.toFixed(1)}
                        <Box component="span" sx={{ fontSize: 14, color: C_MUTED, fontWeight: 500 }}>{` / ${WEEKLY_TARGET}h`}</Box>
                    </Box>
                    <Box sx={{ fontSize: 11, color: hoursRemaining > 0 ? '#F59E0B' : '#22C47A', fontWeight: 600 }}>
                        {hoursRemaining > 0 ? `${hoursRemaining.toFixed(1)}h remaining` : '✓ Complete'}
                    </Box>
                </Box>
                <Box sx={{ height: 8, bgcolor: '#F4F5F7', borderRadius: '4px', overflow: 'hidden', mb: '12px' }}>
                    <Box sx={{ height: '100%', bgcolor: C_BLUE, borderRadius: '4px', width: `${pct}%` }} />
                </Box>
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: '4px' }}>
                    {DAY_LABELS.map((d, i) => {
                        const h = daily[i] ?? 0
                        const filled = h > 0
                        const isToday = i === todayIdx
                        return (
                            <Box key={d} sx={{
                                p: '6px 8px', borderRadius: '5px', textAlign: 'center', fontSize: 11,
                                bgcolor: isToday ? '#EEF4FF' : filled ? '#ECFDF5' : '#F9FAFB',
                                boxShadow: isToday ? `inset 0 0 0 1px ${C_BLUE}` : 'none',
                            }}>
                                <Box sx={{ fontSize: 9, color: isToday ? '#1D4ED8' : '#9CA3AF', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    {d}{isToday ? ' · Today' : ''}
                                </Box>
                                <Box sx={{
                                    fontWeight: 600, fontVariantNumeric: 'tabular-nums', mt: '2px',
                                    color: isToday ? '#1D4ED8' : filled ? '#065F46' : '#9CA3AF',
                                }}>
                                    {filled ? `${h.toFixed(1)}h` : '—'}
                                </Box>
                            </Box>
                        )
                    })}
                </Box>
            </Box>
        </Box>
    )
}

function NoCurrentWeek({ onOpen }: { onOpen: () => void }) {
    return (
        <Box sx={{
            bgcolor: '#F9FAFB', border: `1px dashed ${C_BORDER}`, borderRadius: '12px',
            p: '24px', textAlign: 'center',
        }}>
            <Box sx={{ fontSize: 28, mb: '8px' }}>📝</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: C_HEADING, mb: '4px' }}>No timesheet for this week</Box>
            <Box sx={{ fontSize: 12, color: C_MUTED, mb: '12px' }}>Start tracking your hours.</Box>
            <Box
                component="button"
                onClick={onOpen}
                sx={{
                    bgcolor: C_BLUE, color: '#fff', border: 'none', borderRadius: '6px',
                    px: '14px', py: '6px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                    fontFamily: 'inherit', '&:hover': { bgcolor: '#3A7AE4' },
                }}
            >
                Open this week
            </Box>
        </Box>
    )
}

function NextLeaveCard({ leave, typeName, today }: {
    leave: AnnualLeave; typeName?: string; today: Date
}) {
    const start = new Date(leave.startDate); start.setHours(0, 0, 0, 0)
    const until = daysBetween(today, start)
    const isPending = leave.status === 'Pending'
    const countdown = until === 0 ? 'Today' : until === 1 ? 'Tomorrow' : `In ${until} days`
    const sameDay = leave.startDate.slice(0, 10) === leave.endDate.slice(0, 10)

    return (
        <Box sx={{
            background: 'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',
            color: '#fff', borderRadius: '12px', p: '20px 22px',
            position: 'relative', overflow: 'hidden',
            '&::before': {
                content: '"🌴"', position: 'absolute', right: -10, bottom: -20,
                fontSize: 110, opacity: 0.15, transform: 'rotate(-12deg)',
            },
        }}>
            <Box sx={{ fontSize: 11, opacity: 0.85, textTransform: 'uppercase', letterSpacing: '0.08em', mb: '8px' }}>
                {isPending ? '⏳ Next request' : '✓ Next time off'}
            </Box>
            <Box sx={{ fontSize: 22, fontWeight: 700, lineHeight: 1.15, mb: '6px' }}>
                {sameDay
                    ? new Date(leave.startDate).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
                    : `${formatDateShort(leave.startDate)} → ${formatDateShort(leave.endDate)}`}
            </Box>
            <Box sx={{ fontSize: 13, opacity: 0.95, mb: '14px' }}>
                {leave.totalDays} {leave.totalDays === 1 ? 'day' : 'days'} of {(typeName ?? 'leave').toLowerCase()}
            </Box>
            <Box sx={{
                display: 'inline-block', bgcolor: 'rgba(255,255,255,0.18)', backdropFilter: 'blur(8px)',
                px: '14px', py: '6px', borderRadius: '16px', fontSize: 12, fontWeight: 600, mb: '14px',
            }}>
                {countdown}
            </Box>
            <Box sx={{ display: 'flex', gap: '18px', fontSize: 12, flexWrap: 'wrap' }}>
                <Box>
                    <Box sx={{ opacity: 0.8, fontSize: 11 }}>Status</Box>
                    <Box sx={{ fontWeight: 600, mt: '2px' }}>{isPending ? 'Awaiting approval' : 'Confirmed'}</Box>
                </Box>
                <Box>
                    <Box sx={{ opacity: 0.8, fontSize: 11 }}>Back at work</Box>
                    <Box sx={{ fontWeight: 600, mt: '2px' }}>{nextWorkingDay(leave.endDate)}</Box>
                </Box>
            </Box>
        </Box>
    )
}

function EmptyNextLeave({ onApply }: { onApply: () => void }) {
    return (
        <Box sx={{
            bgcolor: '#F9FAFB', border: `1px dashed ${C_BORDER}`, borderRadius: '12px',
            p: '24px', textAlign: 'center', color: C_MUTED,
        }}>
            <Box sx={{ fontSize: 28, mb: '8px' }}>🏖️</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: C_HEADING, mb: '4px' }}>No upcoming leave</Box>
            <Box sx={{ fontSize: 12, mb: '12px' }}>Time to plan your next break?</Box>
            <Box
                component="button"
                onClick={onApply}
                sx={{
                    bgcolor: C_BLUE, color: '#fff', border: 'none', borderRadius: '6px',
                    px: '14px', py: '6px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                    fontFamily: 'inherit', '&:hover': { bgcolor: '#3A7AE4' },
                }}
            >
                + Apply for leave
            </Box>
        </Box>
    )
}

function LeaveBalanceList({ leaveTypes, approvedThisYear, leaveTypeById, entitlement }: {
    leaveTypes: LeaveType[]
    approvedThisYear: AnnualLeave[]
    leaveTypeById: Map<number, LeaveType>
    entitlement: number
}) {
    const usedByType = new Map<number, number>()
    for (const l of approvedThisYear) {
        if (l.leaveTypeId != null) {
            usedByType.set(l.leaveTypeId, (usedByType.get(l.leaveTypeId) ?? 0) + l.totalDays)
        }
    }
    const rows = leaveTypes
        .filter((lt) => lt.isActive)
        .map((lt) => {
            const used = usedByType.get(lt.id) ?? 0
            const tracks = lt.affectsBalance
            const total = tracks ? entitlement : 0
            const remaining = Math.max(0, total - used)
            const pct = total > 0 ? Math.min(100, (used / total) * 100) : 0
            return { id: lt.id, name: lt.name, used, total, remaining, pct, tracks }
        })

    if (rows.length === 0) {
        return <Box sx={{ fontSize: 12, color: C_MUTED, py: '8px' }}>No active leave types.</Box>
    }

    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            {rows.map((r) => {
                const fillColor = !r.tracks ? '#9CA3AF' : r.pct >= 90 ? '#FF4D4F' : r.pct >= 70 ? '#F59E0B' : '#22C47A'
                return (
                    <Box key={r.id} sx={{ display: 'grid', gridTemplateColumns: '28px 1fr auto', gap: '10px', alignItems: 'center' }}>
                        <Box sx={{ fontSize: 18 }}>{iconForLeaveType(leaveTypeById.get(r.id)?.name)}</Box>
                        <Box>
                            <Box sx={{ fontSize: 12, fontWeight: 500, color: C_HEADING }}>{r.name}</Box>
                            <Box sx={{ height: 5, bgcolor: '#F4F5F7', borderRadius: '3px', mt: '5px', overflow: 'hidden' }}>
                                <Box sx={{ height: '100%', borderRadius: '3px', bgcolor: fillColor, width: `${r.pct}%` }} />
                            </Box>
                        </Box>
                        <Box sx={{ fontSize: 13, color: C_MUTED, fontVariantNumeric: 'tabular-nums', textAlign: 'right', minWidth: 50 }}>
                            {r.tracks && r.total > 0 ? (
                                <>
                                    <Box component="strong" sx={{ fontSize: 14, color: C_HEADING, fontWeight: 700 }}>{r.remaining}</Box>
                                    /{r.total}
                                </>
                            ) : (
                                <Box component="strong" sx={{ fontSize: 14, color: C_HEADING, fontWeight: 700 }}>{r.used}</Box>
                            )}
                        </Box>
                    </Box>
                )
            })}
        </Box>
    )
}

function QuickActions({ tiles }: { tiles: { icon: string; label: string; sub: string; onClick: () => void }[] }) {
    return (
        <Box sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, 1fr)' },
            gap: '10px',
        }}>
            {tiles.map((t, i) => (
                <Box
                    key={i}
                    component="button"
                    onClick={t.onClick}
                    sx={{
                        display: 'flex', alignItems: 'center', gap: '10px', p: '12px',
                        bgcolor: '#F9FAFB', border: `1px solid ${C_BORDER}`, borderRadius: '8px',
                        cursor: 'pointer', textAlign: 'left', fontFamily: 'inherit',
                        transition: 'all 0.15s',
                        '&:hover': { borderColor: C_BLUE, bgcolor: '#EEF4FF', transform: 'translateY(-1px)' },
                    }}
                >
                    <Box sx={{ fontSize: 20 }}>{t.icon}</Box>
                    <Box>
                        <Box sx={{ fontSize: 12, fontWeight: 600, color: C_HEADING }}>{t.label}</Box>
                        <Box sx={{ fontSize: 11, color: C_MUTED, mt: '1px' }}>{t.sub}</Box>
                    </Box>
                </Box>
            ))}
        </Box>
    )
}

/* ── Manager-only ─────────────────────────────────────────────────────── */

function ApprovalQueueCard({ queue, totalQueue, onApprove, onReject, disabled, onViewAllLeave, onViewAllTs }: {
    queue: QueueItem[]
    totalQueue: number
    onApprove: (item: QueueItem) => void
    onReject: (item: QueueItem) => void
    disabled: boolean
    onViewAllLeave: () => void
    onViewAllTs: () => void
}) {
    return (
        <Box sx={{
            bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '12px',
            overflow: 'hidden', mb: '14px',
        }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: `1px solid ${C_BORDER}`,
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <Box component="span" sx={{ fontSize: 16 }}>⚡</Box>
                    <Box sx={{ fontSize: 14, fontWeight: 600, color: C_HEADING }}>Approval queue</Box>
                    {totalQueue > 0 && (
                        <Box component="span" sx={{
                            bgcolor: '#FEE2E2', color: '#991B1B',
                            fontSize: 11, fontWeight: 600,
                            px: '8px', py: '3px', borderRadius: '12px',
                        }}>{totalQueue} waiting</Box>
                    )}
                </Box>
                <Box sx={{ display: 'flex', gap: '6px' }}>
                    <OutlineBtn onClick={onViewAllLeave}>All leave</OutlineBtn>
                    <OutlineBtn onClick={onViewAllTs}>All timesheets</OutlineBtn>
                </Box>
            </Box>
            {queue.length === 0 ? (
                <Box sx={{ p: '24px', textAlign: 'center', color: C_MUTED, fontSize: 13 }}>
                    🎉 The queue is empty. Nothing to approve right now.
                </Box>
            ) : (
                <Box>
                    {queue.map((item, i) => (
                        <ApprovalQueueRow
                            key={`${item.kind}-${item.id}`}
                            item={item}
                            isLast={i === queue.length - 1}
                            onApprove={() => onApprove(item)}
                            onReject={() => onReject(item)}
                            disabled={disabled}
                        />
                    ))}
                </Box>
            )}
        </Box>
    )
}

function ApprovalQueueRow({ item, isLast, onApprove, onReject, disabled }: {
    item: QueueItem
    isLast: boolean
    onApprove: () => void
    onReject: () => void
    disabled: boolean
}) {
    const age = formatRelativeAge(new Date(item.createdAt))
    return (
        <Box sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '36px 1fr', md: '36px 1fr auto auto' },
            gap: '12px', alignItems: 'center',
            px: '18px', py: '12px',
            borderBottom: isLast ? 'none' : '1px solid #F3F4F6',
            '&:hover': { bgcolor: '#F9FAFB' },
        }}>
            <Box sx={{
                width: 36, height: 36, borderRadius: '50%',
                bgcolor: item.urgent ? '#FF4D4F' : avatarBgFor(item.name),
                color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, fontWeight: 600,
            }}>{initials(item.name)}</Box>
            <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: C_HEADING, mb: '2px' }}>{item.title}</Box>
                <Box sx={{ fontSize: 11, color: C_MUTED, display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                    <Box component="span">{item.meta}</Box>
                    {item.tags.map((t, j) => <QueueTagPill key={j} tag={t} />)}
                </Box>
            </Box>
            <Box sx={{
                fontSize: 11, color: item.urgent ? '#991B1B' : C_MUTED, fontWeight: item.urgent ? 600 : 400,
                whiteSpace: 'nowrap', display: { xs: 'none', md: 'block' },
            }}>{age}</Box>
            <Box sx={{ display: 'flex', gap: '6px', gridColumn: { xs: '1 / -1', md: 'auto' }, mt: { xs: '8px', md: 0 } }}>
                <Box
                    component="button"
                    onClick={onApprove}
                    disabled={disabled}
                    sx={{
                        bgcolor: '#22C47A', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { bgcolor: '#18A867' },
                        '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                    }}
                >
                    Approve
                </Box>
                <Box
                    component="button"
                    onClick={onReject}
                    disabled={disabled}
                    sx={{
                        bgcolor: '#FF4D4F', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit',
                        '&:hover:not(:disabled)': { bgcolor: '#E03C3E' },
                        '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                    }}
                >
                    Reject
                </Box>
            </Box>
        </Box>
    )
}

function QueueTagPill({ tag }: { tag: QueueTag }) {
    const styles =
        tag.tone === 'urgent'   ? { bg: '#FEE2E2', color: '#991B1B' } :
        tag.tone === 'warning'  ? { bg: '#FEF3C7', color: '#92400E' } :
        tag.tone === 'conflict' ? { bg: '#FEF3C7', color: '#92400E' } :
                                  { bg: '#EFF6FF', color: '#1D4ED8' }
    return (
        <Box component="span" sx={{
            display: 'inline-flex', alignItems: 'center', fontSize: 10, fontWeight: 500,
            px: '6px', py: '2px', borderRadius: '4px', bgcolor: styles.bg, color: styles.color, whiteSpace: 'nowrap',
        }}>{tag.label}</Box>
    )
}

function formatRelativeAge(d: Date) {
    const diffMs = Date.now() - d.getTime()
    const mins = Math.floor(diffMs / 60000)
    if (mins < 5) return 'Just now'
    if (mins < 60) return `${mins}m ago`
    const hrs = Math.floor(mins / 60)
    if (hrs < 24) return `${hrs}h ago`
    const days = Math.floor(hrs / 24)
    return `${days} day${days === 1 ? '' : 's'} ago`
}

function avatarBgFor(name: string) {
    const colors = ['#4F8EF7', '#22C47A', '#F59E0B', '#8B5CF6', '#EC4899', '#06B6D4', '#84CC16']
    let hash = 0
    for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) | 0
    return colors[Math.abs(hash) % colors.length]
}

function TeamStatusNowCard({ team }: { team: TeamAttendance | null }) {
    if (!team) {
        return (
            <ActionCard title="Team status now" icon="👥">
                <Box sx={{ fontSize: 12, color: C_MUTED }}>Loading team attendance…</Box>
            </ActionCard>
        )
    }
    const inCount = team.members.filter((m) => m.status === 'in').length
    const brkCount = team.members.filter((m) => m.status === 'break').length
    const leaveCount = team.members.filter((m) => m.status === 'leave').length
    const outCount = team.members.filter((m) => m.status === 'out').length

    return (
        <ActionCard
            title="Team status now"
            icon="👥"
            action={
                <Box component="span" sx={{ fontSize: 11, color: '#22C47A', display: 'flex', alignItems: 'center', gap: '5px' }}>
                    <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: '#22C47A' }} />
                    Live
                </Box>
            }
        >
            <Box sx={{ display: 'flex', gap: '14px', mb: '14px', fontSize: 11, color: C_MUTED, flexWrap: 'wrap' }}>
                <Box><Box component="strong" sx={{ color: '#22C47A', fontSize: 14 }}>{inCount}</Box> working</Box>
                <Box><Box component="strong" sx={{ color: '#F59E0B', fontSize: 14 }}>{brkCount}</Box> on break</Box>
                <Box><Box component="strong" sx={{ color: C_BLUE, fontSize: 14 }}>{leaveCount}</Box> on leave</Box>
                <Box><Box component="strong" sx={{ color: '#9CA3AF', fontSize: 14 }}>{outCount}</Box> not in</Box>
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: '8px' }}>
                {team.members.slice(0, 12).map((m) => <TeamMemberTile key={m.employeeId} member={m} />)}
            </Box>
        </ActionCard>
    )
}

function TeamMemberTile({ member }: { member: TeamMemberAttendance }) {
    const tone =
        member.status === 'in'    ? { border: '#22C47A', sub: 'In', subColor: '#22C47A' } :
        member.status === 'break' ? { border: '#F59E0B', sub: '☕ Break', subColor: '#F59E0B' } :
        member.status === 'leave' ? { border: C_BLUE,    sub: '🌴 On leave', subColor: C_BLUE } :
                                    { border: '#9CA3AF', sub: 'Not in', subColor: '#9CA3AF' }
    const detail =
        member.status === 'in' && member.checkInAt
            ? `In since ${new Date(member.checkInAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
            : member.status === 'break' && member.onBreakSince
                ? `Since ${new Date(member.onBreakSince).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
                : tone.sub
    return (
        <Box sx={{
            display: 'flex', alignItems: 'center', gap: '8px', p: '8px 10px',
            bgcolor: '#F9FAFB', border: `1px solid ${C_BORDER}`, borderRadius: '8px',
            borderLeft: `3px solid ${tone.border}`,
        }}>
            <Box sx={{
                width: 28, height: 28, borderRadius: '50%',
                bgcolor: avatarBgFor(member.employeeName), color: '#fff',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 10, fontWeight: 600, flexShrink: 0,
            }}>{initials(member.employeeName)}</Box>
            <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: 12, fontWeight: 500, color: C_HEADING, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {member.employeeName}
                </Box>
                <Box sx={{ fontSize: 10, color: tone.subColor }}>{detail}</Box>
            </Box>
        </Box>
    )
}

function TeamSubmissionsCard({ submitted, total, missing }: {
    submitted: number; total: number
    missing: { name: string; note: string }[]
}) {
    const pct = total > 0 ? (submitted / total) * 100 : 0
    const outstanding = total - submitted
    return (
        <ActionCard title="This week's submissions" icon="📊">
            <Box sx={{ mb: '14px' }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '6px' }}>
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: C_HEADING }}>
                        {submitted}
                        <Box component="span" sx={{ fontSize: 14, color: C_MUTED, fontWeight: 500 }}>
                            {` / ${total} submitted`}
                        </Box>
                    </Box>
                    {outstanding > 0 && (
                        <Box sx={{ fontSize: 11, color: '#F59E0B', fontWeight: 600 }}>{outstanding} outstanding</Box>
                    )}
                </Box>
                <Box sx={{ height: 8, bgcolor: '#F4F5F7', borderRadius: '4px', overflow: 'hidden' }}>
                    <Box sx={{ height: '100%', bgcolor: '#22C47A', borderRadius: '4px', width: `${pct}%` }} />
                </Box>
            </Box>
            {missing.length === 0 ? (
                <Box sx={{ fontSize: 12, color: '#22C47A', textAlign: 'center', py: '6px' }}>
                    ✓ Everyone has submitted for this week.
                </Box>
            ) : (
                <>
                    <Box sx={{ fontSize: 11, color: C_MUTED, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '8px', fontWeight: 600 }}>
                        Not yet submitted
                    </Box>
                    <Box sx={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        {missing.map((m) => (
                            <Box key={m.name} sx={{ display: 'flex', alignItems: 'center', gap: '10px', py: '6px' }}>
                                <Box sx={{
                                    width: 28, height: 28, borderRadius: '50%',
                                    bgcolor: avatarBgFor(m.name), color: '#fff',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                    fontSize: 10, fontWeight: 600, flexShrink: 0,
                                }}>{initials(m.name)}</Box>
                                <Box sx={{ flex: 1, minWidth: 0 }}>
                                    <Box sx={{ fontSize: 12, fontWeight: 500, color: C_HEADING }}>{m.name}</Box>
                                    <Box sx={{ fontSize: 10, color: C_MUTED }}>{m.note}</Box>
                                </Box>
                            </Box>
                        ))}
                    </Box>
                </>
            )}
        </ActionCard>
    )
}

/* ── Admin-only ───────────────────────────────────────────────────────── */

function Gauge({ label, big, bigColor, sub, barColor, barPct }: {
    label: string; big: string; bigColor?: string; sub: string; barColor: string; barPct: number
}) {
    return (
        <Box sx={{
            bgcolor: '#fff', border: `1px solid ${C_BORDER}`, borderRadius: '10px', p: '14px 16px',
        }}>
            <Box sx={{ fontSize: 11, color: C_MUTED, textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 26, fontWeight: 700, color: bigColor ?? C_HEADING, lineHeight: 1, mb: '6px' }}>{big}</Box>
            <Box sx={{ fontSize: 11, color: C_MUTED, mb: '8px' }}>{sub}</Box>
            <Box sx={{ height: 4, bgcolor: '#F4F5F7', borderRadius: '2px', overflow: 'hidden' }}>
                <Box sx={{ height: '100%', bgcolor: barColor, borderRadius: '2px', width: `${Math.min(100, barPct)}%` }} />
            </Box>
        </Box>
    )
}

function DepartmentHealthCard({ departments, leaves, timesheets, onLive }: {
    departments: DepartmentAttendance[]
    leaves: AnnualLeave[]
    timesheets: Timesheet[]
    onLive: () => void
}) {
    const pendingByDept = useMemo(() => {
        const m = new Map<string, number>()
        for (const l of leaves) {
            if (l.status === 'Pending') {
                m.set(l.departmentName, (m.get(l.departmentName) ?? 0) + 1)
            }
        }
        for (const t of timesheets) {
            if (t.status === 'Submitted' || t.status === 'Resubmitted') {
                // Best effort — no departmentName on timesheet
            }
        }
        return m
    }, [leaves, timesheets])

    return (
        <ActionCard title="Department health" icon="🏢" action={<OutlineBtn onClick={onLive}>Live attendance</OutlineBtn>}>
            {departments.length === 0 ? (
                <Box sx={{ fontSize: 12, color: C_MUTED, py: '8px' }}>No attendance data.</Box>
            ) : (
                <>
                    <Box sx={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        {departments.map((d) => {
                            const inPct = d.total > 0 ? (d.in / d.total) * 100 : 0
                            const brkPct = d.total > 0 ? (d.break / d.total) * 100 : 0
                            const leavePct = d.total > 0 ? (d.leave / d.total) * 100 : 0
                            const outPct = d.total > 0 ? (d.out / d.total) * 100 : 0
                            const pending = pendingByDept.get(d.name) ?? 0
                            return (
                                <Box key={d.name} sx={{
                                    display: 'grid',
                                    gridTemplateColumns: { xs: '1fr', sm: '110px 1fr 130px' },
                                    gap: '10px', alignItems: 'center', fontSize: 12,
                                }}>
                                    <Box sx={{ fontWeight: 600, color: C_HEADING }}>
                                        {d.name}
                                        <Box component="span" sx={{ color: '#9CA3AF', fontWeight: 400, fontSize: 11, ml: '4px' }}>
                                            ({d.total})
                                        </Box>
                                    </Box>
                                    <Box sx={{ display: 'flex', height: 12, bgcolor: '#F4F5F7', borderRadius: '4px', overflow: 'hidden' }}>
                                        <Box title={`${d.in} working`} sx={{ width: `${inPct}%`, bgcolor: '#22C47A' }} />
                                        <Box title={`${d.break} on break`} sx={{ width: `${brkPct}%`, bgcolor: '#F59E0B' }} />
                                        <Box title={`${d.leave} on leave`} sx={{ width: `${leavePct}%`, bgcolor: C_BLUE }} />
                                        <Box title={`${d.out} not in`} sx={{ width: `${outPct}%`, bgcolor: '#E4E6EA' }} />
                                    </Box>
                                    <Box sx={{ color: C_MUTED, textAlign: { xs: 'left', sm: 'right' } }}>
                                        {pending > 0 && <>{pending} pending · </>}
                                        <Box component="strong" sx={{ color: C_HEADING }}>{Math.round(inPct)}% in</Box>
                                    </Box>
                                </Box>
                            )
                        })}
                    </Box>
                    <Box sx={{ mt: '12px', pt: '10px', borderTop: '1px solid #F3F4F6', display: 'flex', gap: '14px', fontSize: 10, color: C_MUTED, flexWrap: 'wrap' }}>
                        <LegendDot color="#22C47A" label="Working" />
                        <LegendDot color="#F59E0B" label="Break" />
                        <LegendDot color={C_BLUE} label="Leave" />
                        <LegendDot color="#E4E6EA" label="Not in" />
                    </Box>
                </>
            )}
        </ActionCard>
    )
}

function LegendDot({ color, label }: { color: string; label: string }) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{ width: 8, height: 8, borderRadius: '2px', bgcolor: color }} />
            {label}
        </Box>
    )
}

function TodaysIssuesCard({ issues }: { issues: AttendanceIssue[] }) {
    const tones: Record<string, { bg: string; bd: string; head: string; body: string }> = {
        danger:  { bg: '#FEE2E2', bd: '#FF4D4F', head: '#991B1B', body: '#7F1D1D' },
        warning: { bg: '#FEF3C7', bd: '#F59E0B', head: '#92400E', body: '#78350F' },
        info:    { bg: '#EFF6FF', bd: C_BLUE,    head: '#1D4ED8', body: '#1E3A8A' },
        success: { bg: '#D1FAE5', bd: '#22C47A', head: '#065F46', body: '#064E3B' },
    }
    return (
        <ActionCard title="Today's issues" icon="⚠️" action={<Box component="span" sx={{ fontSize: 11, color: '#9CA3AF' }}>Updated just now</Box>}>
            {issues.length === 0 ? (
                <Box sx={{
                    bgcolor: '#D1FAE5', borderLeft: '3px solid #22C47A', borderRadius: '6px',
                    p: '10px 12px',
                }}>
                    <Box sx={{ fontSize: 12, fontWeight: 600, color: '#065F46' }}>✓ All systems quiet</Box>
                    <Box sx={{ fontSize: 11, color: '#064E3B' }}>No active attendance issues.</Box>
                </Box>
            ) : (
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {issues.map((it, i) => {
                        const t = tones[it.severity] ?? tones.info
                        return (
                            <Box key={i} sx={{
                                bgcolor: t.bg, borderLeft: `3px solid ${t.bd}`, borderRadius: '6px',
                                p: '10px 12px',
                            }}>
                                <Box sx={{ fontSize: 12, fontWeight: 600, color: t.head, mb: '3px' }}>{it.title}</Box>
                                <Box sx={{ fontSize: 11, color: t.body }}>{it.detail}</Box>
                            </Box>
                        )
                    })}
                </Box>
            )}
        </ActionCard>
    )
}

function RecentActivityCard({ activity }: { activity: RecentActivity[] }) {
    return (
        <ActionCard title="Recent activity" icon="📡">
            {activity.length === 0 ? (
                <Box sx={{ fontSize: 12, color: C_MUTED, textAlign: 'center', py: '12px' }}>
                    No recent activity.
                </Box>
            ) : (
                <Box sx={{ display: 'flex', flexDirection: 'column' }}>
                    {activity.slice(0, 8).map((a, i) => (
                        <Box key={i} sx={{
                            display: 'grid', gridTemplateColumns: '24px 1fr auto', gap: '10px',
                            alignItems: 'center', py: '8px',
                            borderBottom: i === Math.min(activity.length, 8) - 1 ? 'none' : '1px solid #F3F4F6',
                        }}>
                            <Box sx={{ fontSize: 14 }}>{iconForActivity(a.action)}</Box>
                            <Box sx={{ fontSize: 12, color: '#374151' }}>
                                <Box component="strong" sx={{ color: C_HEADING, fontWeight: 600 }}>{a.employeeName}</Box>{' '}
                                {a.action}
                                {a.departmentName && (
                                    <Box component="span" sx={{ color: '#9CA3AF', ml: '4px', fontSize: 11 }}>· {a.departmentName}</Box>
                                )}
                            </Box>
                            <Box sx={{ fontSize: 11, color: '#9CA3AF', whiteSpace: 'nowrap' }}>
                                {a.minutesAgo != null ? formatMinutesAgo(a.minutesAgo) : '—'}
                            </Box>
                        </Box>
                    ))}
                </Box>
            )}
        </ActionCard>
    )
}

function iconForActivity(action: string) {
    const lower = action.toLowerCase()
    if (lower.includes('check-in') || lower.includes('checked in')) return '🟢'
    if (lower.includes('break')) return '☕'
    if (lower.includes('approve')) return '✓'
    if (lower.includes('timesheet')) return '📋'
    if (lower.includes('late')) return '⚠️'
    if (lower.includes('not check')) return '🔴'
    if (lower.includes('leave')) return '🌴'
    return '·'
}

function formatMinutesAgo(mins: number) {
    if (mins < 1) return 'just now'
    if (mins < 60) return `${mins}m ago`
    return minutesToHm(mins) + ' ago'
}

/* ────────────── Suppress unused warnings for re-exported types ─────── */
type _Unused = TimesheetStatus | AnnualLeaveStatus
const _unused: _Unused | null = null
void _unused
