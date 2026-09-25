import { useEffect, useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { observer } from 'mobx-react-lite'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { alpha, useTheme, type Theme } from '@mui/material/styles'
import { BarChart } from '@mui/x-charts/BarChart'
import { LineChart } from '@mui/x-charts/LineChart'
import {
    approveTimesheet, getAdminUsers, getAnnualLeaves, getAppSettings,
    getCompanyAttendance, getDepartments, getEmployeeProfiles, getLeaveTypes,
    getMyTimesheets, getProjectActivityTypes, getProjectComponents, getProjects, getProjectTypes,
    getTeamAttendance, getTeamAttendanceHistory, getTimesheets, rejectTimesheet, updateLeaveStatus,
} from '../../lib/api'
import { approveButtonLabel, approveOutcome, canDecide, isOpenStatus, isTimesheetWithManager, isWithManager, type ApprovalViewer } from '../../lib/approval-stage'
import { currentYearEntitlement } from '../../lib/leave-allowance'
import { isAwaitingDocument } from '../../lib/attachment-policy'
import { isAdministrator, isSystemAdministrator } from '../../lib/roles'
import { activityIcon } from '../../lib/hooks/useAttendance'
import { describeBreakVariance } from '../../lib/break-policy'
import { useOfferedLeaveTypes } from '../../lib/hooks'
import { buildLeaveBalanceRows, type LeaveBalanceRow } from '../../lib/leave-balance-rows'
import { useStore } from '../../lib/mobx'
import { describeWorkingDays } from '../../lib/working-week'
import { iconForLeaveType } from './leave-icons'
import { ActivityTypesPanel, AdminUsersPanel, AppSettingsPanel, ComponentsPanel, DataMaintenancePanel, DepartmentsPanel, LeaveTypesPanel, OrgSettingsPanel, ProjectsPanel, ProjectTypesPanel, SystemLogPanel } from '..'
import type {
    AdminUser, AnnualLeave, AnnualLeaveStatus, AttendanceIssue, Department, DepartmentAttendance, EmployeeProfile, LeaveType,
    RecentActivity, TeamAttendance, TeamHistory, TeamMemberAttendance, Timesheet, TimesheetStatus, UserInfo,
} from '../../lib/types'

const WEEKLY_TARGET = 40

// Soft semantic-tint backgrounds used for status pills, alerts, and hover states.
// `alpha` keeps them legible in both light and dark modes by tinting the
// theme's semantic colors rather than hardcoding pastel hex values.
const softBg = (palette: keyof Theme['palette']) => (theme: Theme) => {
    const p = theme.palette[palette]
    if (p && typeof p === 'object' && 'main' in p && typeof p.main === 'string') {
        return alpha(p.main, theme.palette.mode === 'dark' ? 0.18 : 0.12)
    }
    return 'transparent'
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

// Reactive "current time" for memos that need to re-derive as time passes
// (day-level countdowns/cutoffs), not just when their data deps change.
function useNow(intervalMs = 300_000) {
    const [now, setNow] = useState(() => Date.now())
    useEffect(() => {
        const id = setInterval(() => setNow(Date.now()), intervalMs)
        return () => clearInterval(id)
    }, [intervalMs])
    return now
}

const DashboardHome = observer(function DashboardHome() {
    const { authStore } = useStore()
    const location = useLocation()
    const user = authStore.user
    if (!user) return null

    const isAdmin = isAdministrator(user.roles)
    // The configuration panels below are system administration; the route guard in
    // App.tsx already turns an HR Administrator away from /admin/*, and this agrees with it.
    const isSystemAdmin = isSystemAdministrator(user.roles)
    const isManager = user.roles.includes('Manager') && !isAdmin

    // System Administrator sub-routes are derived from the URL (was: uiStore.adminSection).
    const adminSection = location.pathname.startsWith('/admin/')
        ? location.pathname.split('/')[2]
        : null
    if (isSystemAdmin && adminSection === 'users') return <AdminUsersPanel />
    if (isSystemAdmin && adminSection === 'departments') return <DepartmentsPanel />
    if (isSystemAdmin && (adminSection === 'leave-types' || adminSection === 'leave')) return <LeaveTypesPanel />
    if (isSystemAdmin && adminSection === 'projects') return <ProjectsPanel />
    if (isSystemAdmin && adminSection === 'project-activities') return <ActivityTypesPanel />
    if (isSystemAdmin && adminSection === 'components') return <ComponentsPanel />
    if (isSystemAdmin && adminSection === 'project-types') return <ProjectTypesPanel />
    if (isSystemAdmin && adminSection === 'organization') return <AppSettingsPanel />
    if (isSystemAdmin && adminSection === 'reminders-notifications') return <OrgSettingsPanel />
    if (isSystemAdmin && adminSection === 'maintenance') return <DataMaintenancePanel />
    if (isSystemAdmin && adminSection === 'system-log') return <SystemLogPanel />

    // An HR Administrator has the reach without the configuration, and a dashboard
    // to match: the decisions waiting on them, who is away, and balances to watch —
    // not the workspace overview a System Administrator opens on.
    if (isAdmin && !isSystemAdmin) return <HrDashboard user={user} />
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
    // This year's figure, pro-rated by the server for a mid-year joiner when the
    // balance type asks for it — what the API will actually approve up to.
    const entitlement = currentYearEntitlement(myProfile)
    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])

    const currentYear = today.getFullYear()
    const myApprovedThisYear = useMemo(
        () => leaves.filter((l) => l.employeeId === user.id && l.status === 'Approved' && new Date(l.startDate).getFullYear() === currentYear),
        [leaves, user.id, currentYear]
    )

    /* The balance card shows what the employee may actually request, measured
       against whichever ledger the type keeps — the same two rules My Leave
       applies, shared so the two cards cannot disagree. Listing every active type
       against the pooled entitlement offered a woman paternity leave and reported
       a per-child type as 0. */
    const { offeredLeaveTypes, ledgerByTypeId } = useOfferedLeaveTypes(leaveTypes, user.gender, user.employmentStartDate)
    const balanceRows = useMemo(
        () => buildLeaveBalanceRows({
            leaveTypes: offeredLeaveTypes,
            approvedThisYear: myApprovedThisYear,
            entitlement,
            ledgerByTypeId,
            firstYear: { employmentStartDate: myProfile?.employmentStartDate, leaveYearStartMonth: settings?.leaveYearStartMonth ?? 1 },
        }),
        [offeredLeaveTypes, myApprovedThisYear, entitlement, ledgerByTypeId, myProfile?.employmentStartDate, settings?.leaveYearStartMonth],
    )

    const balanceUsed = useMemo(() =>
        myApprovedThisYear.reduce((s, l) => {
            const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined
            return s + (lt?.affectsBalance === false ? 0 : l.totalDays)
        }, 0)
    , [myApprovedThisYear, leaveTypeById])

    const balanceRemaining = Math.max(0, entitlement - balanceUsed)
    const myPendingLeaves = leaves.filter((l) => l.employeeId === user.id && isOpenStatus(l.status))
    const myRejectedTs = timesheets.filter((t) => t.status === 'Rejected')

    // Current week timesheet
    const currentTimesheet = useMemo(
        () => timesheets.find((t) => dateInRange(today, t.periodStart, t.periodEnd)) ?? null,
        [timesheets, today]
    )
    const currentHours = currentTimesheet ? Number(currentTimesheet.totalHours) : 0
    const hoursRemaining = Math.max(0, WEEKLY_TARGET - currentHours)

    // Days left until Friday 6pm of the current week
    const now = useNow()
    const fridayEod = useMemo(() => {
        if (!currentTimesheet) return null
        const fri = new Date(currentTimesheet.periodEnd); fri.setHours(18, 0, 0, 0)
        return Math.max(0, Math.ceil((fri.getTime() - now) / 86_400_000))
    }, [currentTimesheet, now])

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
            .filter((l) => l.employeeId === user.id && (isOpenStatus(l.status) || l.status === 'Approved') && new Date(l.startDate) >= today)
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
                gradient={{
                    light: 'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',
                    dark: 'linear-gradient(135deg, #1e3a8a 0%, #0f172a 100%)',
                }}
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name={`Hi ${firstName(user)} 👋`}
                summary={summary}
                meta={[
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
                    <LeaveBalanceList rows={balanceRows} />
                </ActionCard>

                <ActionCard title="Quick actions" icon="⚡">
                    <QuickActions tiles={[
                        { icon: '📝', label: 'This week', sub: 'Log hours', onClick: () => uiStore.navigateToNewTimesheet() },
                        { icon: '📅', label: 'My leave', sub: 'View history', onClick: () => uiStore.navigateToMyLeave('requests') },
                        { icon: '🕐', label: 'My timesheets', sub: 'Past weeks', onClick: () => uiStore.navigateToTimesheets() },
                    ]} />
                </ActionCard>
            </Box>

            <Box sx={{ fontSize: 11, color: 'text.disabled', textAlign: 'center', mt: '8px' }}>
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
    const { data: teamHistory } = useQuery({ queryKey: ['attendance', 'team', 'history', 30], queryFn: () => getTeamAttendanceHistory(30) })

    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])

    // The manager's own EmployeeProfile id. AnnualLeave.employeeId is an Identity
    // user id, but TimesheetDto.employeeId is an EmployeeProfile id, so excluding
    // the manager's own timesheet needs the profile id, not user.id.
    const myProfileId = useMemo(() => profiles.find((p) => p.userId === user.id)?.id, [profiles, user.id])

    // Pending items NOT submitted by manager themselves
    const pendingLeaves = useMemo(
        () => leaves.filter((l) => isOpenStatus(l.status) && l.employeeId !== user.id)
            .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
        [leaves, user.id]
    )
    const pendingTs = useMemo(
        () => timesheets.filter((t) => (t.status === 'Submitted' || t.status === 'Resubmitted') && t.employeeId !== myProfileId)
            .sort((a, b) => new Date(a.submittedAt ?? a.createdAt).getTime() - new Date(b.submittedAt ?? b.createdAt).getTime()),
        [timesheets, myProfileId]
    )

    // Detect conflicts: leave requests overlapping same dept on same dates
    const conflictMap = useMemo(() => buildConflictMap(pendingLeaves, leaves), [pendingLeaves, leaves])

    // Mutations
    const approveLeaveMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Approved'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
    })
    const rejectLeaveMut = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => updateLeaveStatus(id, 'Rejected', comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            void queryClient.invalidateQueries({ queryKey: ['leaveStatusHistories'] })
        },
    })
    const approveTsMut = useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['timesheets'] }),
    })
    const rejectTsMut = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => rejectTimesheet(id, comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
        },
    })
    const isMutating = approveLeaveMut.isPending || rejectLeaveMut.isPending || approveTsMut.isPending || rejectTsMut.isPending

    const [rejectTarget, setRejectTarget] = useState<{ kind: 'leave' | 'timesheet'; id: string; label: string } | null>(null)
    const isRejecting = rejectLeaveMut.isPending || rejectTsMut.isPending

    async function confirmReject(reason: string) {
        if (!rejectTarget) return
        if (rejectTarget.kind === 'leave') {
            await rejectLeaveMut.mutateAsync({ id: rejectTarget.id, comment: reason })
        } else {
            await rejectTsMut.mutateAsync({ id: rejectTarget.id, comment: reason })
        }
        setRejectTarget(null)
    }

    // Build queue: merge leaves + timesheets, sort by age
    const now = useNow()
    const queue = useMemo(
        () => buildApprovalQueue(pendingLeaves, pendingTs, leaveTypeById, conflictMap, now, { isHrAdministrator: false }),
        [pendingLeaves, pendingTs, leaveTypeById, conflictMap, now],
    )

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
        // Both sides are EmployeeProfile ids: team.members[].employeeId is projected
        // from EmployeeProfile.Id, as is TimesheetDto.EmployeeId.
        const missing: { name: string; note: string }[] = []
        let submittedCount = 0
        for (const m of teammates) {
            if (submitted.has(m.employeeId)) { submittedCount++; continue }
            // Find any in-progress (draft) for current week
            const draft = timesheets.find((t) => t.employeeId === m.employeeId && t.status === 'Draft' && new Date(t.periodStart) >= weekStart && new Date(t.periodStart) <= weekEnd)
            const note = draft
                ? `Currently ${Number(draft.totalHours).toFixed(0)}h logged · in progress`
                : (() => {
                    const last = [...timesheets].filter((t) => t.employeeId === m.employeeId && t.submittedAt)
                        .sort((a, b) => new Date(b.submittedAt!).getTime() - new Date(a.submittedAt!).getTime())[0]
                    return last ? `Last submitted: ${formatDateShort(last.submittedAt!)}` : 'No timesheets yet'
                })()
            missing.push({ name: m.employeeName, note })
        }
        return { submitted: submittedCount, total: teammates.length, missing: missing.slice(0, 5) }
    }, [team, timesheets, today])

    if (lLoading || tLoading) return <CenterSpinner />

    const decidableLeaves = queue.filter((q) => q.kind === 'leave' && q.decidable).length
    const summary = buildManagerSummary({ pendingLeaves: decidableLeaves, pendingTs: pendingTs.length, urgent: queue.filter((q) => q.urgent).length, conflicts: conflictMap.size })

    return (
        <Box>
            <GreetingHero
                gradient={{
                    light: 'linear-gradient(135deg, #1A1A2E 0%, #4F8EF7 100%)',
                    dark: 'linear-gradient(135deg, #0f172a 0%, #1e40af 100%)',
                }}
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name={`Hi ${firstName(user)} 👋`}
                summary={summary}
                meta={[
                    { l: 'Team size', v: `${team?.members.length ?? 0} people` },
                    { l: 'Working now', v: team ? `${team.members.filter((m) => m.status === 'in').length} of ${team.members.length}` : '—' },
                    { l: 'On leave', v: team ? `${team.members.filter((m) => m.status === 'leave').length}` : '—' },
                ]}
            />

            <ApprovalQueueCard
                queue={queue.slice(0, 5)}
                totalQueue={queue.length}
                onApprove={(item) => item.kind === 'leave' ? approveLeaveMut.mutate(item.id) : approveTsMut.mutate(item.id)}
                onReject={(item) => setRejectTarget({ kind: item.kind, id: item.id, label: item.title })}
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

            <TeamHealthCard leaves={leaves} teamHistory={teamHistory ?? null} />

            <ActionCard title="Quick actions" icon="⚡">
                <QuickActions tiles={[
                    { icon: '📝', label: 'My timesheet', sub: 'For this week', onClick: () => uiStore.navigateToNewTimesheet() },
                    { icon: '🌴', label: 'Apply leave', sub: 'For yourself', onClick: () => uiStore.navigateToApplyLeave() },
                ]} />
            </ActionCard>

            <RejectDialog
                target={rejectTarget}
                onClose={() => setRejectTarget(null)}
                onConfirm={confirmReject}
                busy={isRejecting}
            />
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

    const { data: users = [], isLoading: uLoading } = useQuery({ queryKey: ['adminUsers'], queryFn: getAdminUsers })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: departments = [], isLoading: dLoading } = useQuery({ queryKey: ['departments'], queryFn: getDepartments })
    const { data: projects = [] } = useQuery({ queryKey: ['projects'], queryFn: getProjects })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: activities = [] } = useQuery({ queryKey: ['projectActivityTypes'], queryFn: getProjectActivityTypes })
    const { data: components = [] } = useQuery({ queryKey: ['projectComponents'], queryFn: getProjectComponents })
    const { data: projectTypes = [] } = useQuery({ queryKey: ['projectTypes'], queryFn: getProjectTypes })
    const { data: settings } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })

    const workspace = useMemo(() => buildWorkspaceOverview(users, profiles, departments), [users, profiles, departments])

    if (uLoading || dLoading) return <CenterSpinner />

    const activeLeaveTypes = leaveTypes.filter((t) => t.isActive).length
    const activeProjects = projects.filter((p) => p.isActive).length
    const activeActivities = activities.filter((a) => a.isActive).length
    const activeComponents = components.filter((c) => c.isActive).length
    const activeProjectTypes = projectTypes.filter((t) => t.isActive).length
    const remindersOn = settings?.reminders.filter((r) => r.enabled).length ?? 0

    const attention: AttentionItem[] = []
    if (workspace.invitesPending.length > 0) {
        attention.push({
            icon: '✉️', tone: 'urgent',
            label: `${workspace.invitesPending.length} invite${workspace.invitesPending.length === 1 ? '' : 's'} not yet accepted`,
            sub: workspace.invitesPending.slice(0, 3).map((u) => u.displayName || u.email).join(', ') + (workspace.invitesPending.length > 3 ? ` +${workspace.invitesPending.length - 3}` : ''),
            onClick: () => uiStore.navigateToAdminSection('users'),
        })
    }
    if (workspace.noDepartment.length > 0) {
        attention.push({
            icon: '🧭', tone: 'urgent',
            label: `${workspace.noDepartment.length} ${workspace.noDepartment.length === 1 ? 'person has' : 'people have'} no department`,
            sub: 'Invisible to every manager and to leave routing until placed',
            onClick: () => uiStore.navigateToAdminSection('users'),
        })
    }
    if (workspace.withoutManager.length > 0) {
        attention.push({
            icon: '👥', tone: 'urgent',
            label: `${workspace.withoutManager.length} department${workspace.withoutManager.length === 1 ? '' : 's'} without a manager`,
            sub: `${workspace.withoutManager.map((d) => d.name).join(', ')} — nobody to approve their leave`,
            onClick: () => uiStore.navigateToAdminSection('departments'),
        })
    }
    if (workspace.deactivated.length > 0) {
        attention.push({
            icon: '⏸', tone: 'normal',
            label: `${workspace.deactivated.length} deactivated account${workspace.deactivated.length === 1 ? '' : 's'}`,
            sub: 'Kept for their approval history; they cannot sign in',
            onClick: () => uiStore.navigateToAdminSection('users'),
        })
    }
    if (workspace.emptyDepartments.length > 0) {
        attention.push({
            icon: '🏢', tone: 'normal',
            label: `${workspace.emptyDepartments.length} empty department${workspace.emptyDepartments.length === 1 ? '' : 's'}`,
            sub: workspace.emptyDepartments.map((d) => d.name).join(', '),
            onClick: () => uiStore.navigateToAdminSection('departments'),
        })
    }

    return (
        <Box>
            <GreetingHero
                gradient={{
                    light: 'linear-gradient(135deg, #4338CA 0%, #8B5CF6 100%)',
                    dark: 'linear-gradient(135deg, #1e1b4b 0%, #4c1d95 100%)',
                }}
                hello={`${greetingForHour(today.getHours())} · ${formatTodayLong()}`}
                name="Workspace overview"
                summary={buildWorkspaceSummary(workspace)}
                meta={[
                    { l: 'Accounts', v: `${workspace.active.length} active` },
                    { l: 'Departments', v: `${workspace.activeDepartments.length} active` },
                    { l: 'Leave types', v: `${activeLeaveTypes} active` },
                    { l: 'Projects', v: `${activeProjects} active` },
                ]}
            />

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: '12px', mb: '14px' }}>
                <Gauge
                    label="Accounts"
                    big={`${workspace.active.length}`}
                    bigColor="primary.main"
                    sub={`${workspace.deactivated.length} deactivated · ${workspace.invitesPending.length} invite${workspace.invitesPending.length === 1 ? '' : 's'} pending`}
                    barColor="primary.main"
                    barPct={users.length > 0 ? (workspace.active.length / users.length) * 100 : 0}
                />
                <Gauge
                    label="People"
                    big={`${workspace.roles.employees}`}
                    bigColor="success.main"
                    sub={`employees · ${workspace.roles.managers} manager${workspace.roles.managers === 1 ? '' : 's'} · ${workspace.roles.hrAdmins} HR · ${workspace.roles.systemAdmins} system`}
                    barColor="success.main"
                    barPct={workspace.active.length > 0 ? (workspace.roles.employees / workspace.active.length) * 100 : 0}
                />
                <Gauge
                    label="Departments"
                    big={`${workspace.activeDepartments.length}`}
                    bigColor={workspace.withoutManager.length > 0 ? 'warning.main' : 'text.primary'}
                    sub={`${workspace.withoutManager.length} without a manager · ${workspace.emptyDepartments.length} empty`}
                    barColor="warning.main"
                    barPct={workspace.activeDepartments.length > 0 ? ((workspace.activeDepartments.length - workspace.withoutManager.length) / workspace.activeDepartments.length) * 100 : 0}
                />
                <Gauge
                    label="Configuration"
                    big={`${activeProjects}`}
                    bigColor="secondary.main"
                    sub={`projects · ${activeLeaveTypes} leave types · ${activeActivities} activities · ${activeComponents} components · ${activeProjectTypes} project types`}
                    barColor="secondary.main"
                    barPct={projects.length > 0 ? (activeProjects / projects.length) * 100 : 0}
                />
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <ActionCard
                    title="People by department"
                    icon="🏢"
                    countLabel={`${workspace.placed} placed`}
                    action={<OutlineBtn onClick={() => uiStore.navigateToAdminSection('departments')}>Departments</OutlineBtn>}
                >
                    {workspace.departmentRows.length === 0 ? (
                        <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '12px' }}>
                            No active departments yet.
                        </Box>
                    ) : workspace.departmentRows.map((row, i) => (
                        <Box key={row.department.id} sx={{
                            display: 'grid', gridTemplateColumns: '1fr auto', gap: '10px', alignItems: 'center',
                            py: '9px', borderBottom: i === workspace.departmentRows.length - 1 ? 'none' : '1px solid', borderColor: 'divider',
                        }}>
                            <Box sx={{ minWidth: 0 }}>
                                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>
                                    {row.department.name}
                                    <Box component="span" sx={{ ml: '6px', fontSize: 10, fontWeight: 500, color: 'text.secondary' }}>{row.department.code}</Box>
                                </Box>
                                <Box sx={{ fontSize: 11, color: row.managers.length === 0 ? 'warning.dark' : 'text.secondary', mt: '2px' }}>
                                    {row.managers.length === 0 ? '⚠ No manager assigned' : `Managed by ${row.managers.join(', ')}`}
                                </Box>
                            </Box>
                            <Box sx={{
                                fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap', px: '8px', py: '3px', borderRadius: '10px',
                                bgcolor: row.headcount === 0 ? 'action.hover' : softBg('info'), color: row.headcount === 0 ? 'text.secondary' : 'info.dark',
                            }}>{row.headcount} {row.headcount === 1 ? 'person' : 'people'}</Box>
                        </Box>
                    ))}
                </ActionCard>

                <ActionCard
                    title="Needs attention"
                    icon="🔔"
                    countLabel={attention.length > 0 ? `${attention.length} item${attention.length === 1 ? '' : 's'}` : undefined}
                    countTone={attention.some((a) => a.tone === 'urgent') ? 'urgent' : 'normal'}
                    action={<OutlineBtn onClick={() => uiStore.navigateToAdminSection('users')}>Users</OutlineBtn>}
                >
                    {attention.length === 0 ? (
                        <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '12px' }}>
                            ✓ Every account is placed, confirmed and active, and every department has a manager.
                        </Box>
                    ) : attention.map((item, i) => <AttentionRow key={i} item={item} />)}
                </ActionCard>
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <ActionCard title="Configuration" icon="🧩">
                    <QuickActions tiles={[
                        { icon: '🏷️', label: 'Leave types', sub: `${activeLeaveTypes} of ${leaveTypes.length} active`, onClick: () => uiStore.navigateToAdminSection('leave-types') },
                        { icon: '📁', label: 'Projects', sub: `${activeProjects} of ${projects.length} active`, onClick: () => uiStore.navigateToAdminSection('projects') },
                        { icon: '🗂️', label: 'Activities', sub: `${activeActivities} active`, onClick: () => uiStore.navigateToAdminSection('project-activities') },
                        { icon: '🧱', label: 'Components', sub: `${activeComponents} active`, onClick: () => uiStore.navigateToAdminSection('components') },
                        { icon: '🎯', label: 'Project types', sub: `${activeProjectTypes} active`, onClick: () => uiStore.navigateToAdminSection('project-types') },
                        { icon: '🏢', label: 'Departments', sub: `${workspace.activeDepartments.length} active`, onClick: () => uiStore.navigateToAdminSection('departments') },
                    ]} />
                </ActionCard>

                <ActionCard
                    title="System"
                    icon="⚙️"
                    action={(
                        <Box sx={{ display: 'flex', gap: '6px' }}>
                            <OutlineBtn onClick={() => uiStore.navigateToAdminSection('organization')}>Organization</OutlineBtn>
                            <OutlineBtn onClick={() => uiStore.navigateToAdminSection('reminders-notifications')}>Notifications</OutlineBtn>
                        </Box>
                    )}
                >
                    {!settings ? (
                        <Box sx={{ fontSize: 12, color: 'text.secondary' }}>Loading settings…</Box>
                    ) : (
                        <Box>
                            <InfoRow label="Email notifications" value={settings.emailNotificationsEnabled ? `On${settings.emailDailyDigest ? ' · daily digest' : ''}${settings.emailUrgentOnly ? ' · urgent only' : ''}` : 'Off'} tone={settings.emailNotificationsEnabled ? 'ok' : 'warn'} />
                            <InfoRow label="Reminders" value={`${remindersOn} of ${settings.reminders.length} enabled`} tone={remindersOn > 0 ? 'ok' : 'warn'} />
                            <InfoRow label="Public holidays" value={settings.holidayCountryName ?? 'No country set'} tone={settings.holidayCountryName ? 'ok' : 'warn'} />
                            <InfoRow label="Working week" value={`${describeWorkingDays(settings.workingDays, settings.workingDaysCustom)} · ${settings.workingHoursStart}–${settings.workingHoursEnd} · ${settings.weeklyHoursTarget}h target`} />
                            <InfoRow label="Time zone" value={settings.timeZoneId} />
                            <InfoRow label="Leave year starts" value={monthName(settings.leaveYearStartMonth)} last />
                        </Box>
                    )}
                </ActionCard>
            </Box>
        </Box>
    )
}

interface WorkspaceOverview {
    active: AdminUser[]
    deactivated: AdminUser[]
    invitesPending: AdminUser[]
    noDepartment: AdminUser[]
    roles: { systemAdmins: number; hrAdmins: number; managers: number; employees: number }
    activeDepartments: Department[]
    withoutManager: Department[]
    emptyDepartments: Department[]
    departmentRows: { department: Department; headcount: number; managers: string[] }[]
    placed: number
}

/**
 * The System Administrator's numbers, from the same three lists the Users and
 * Departments panels read. Administrators sit outside the department structure, so
 * "no department" is only ever said of an active Employee or Manager, and a
 * department's headcount counts active accounts with a profile in it.
 */
function buildWorkspaceOverview(users: AdminUser[], profiles: EmployeeProfile[], departments: Department[]): WorkspaceOverview {
    const active = users.filter((u) => u.isActive)
    const deactivated = users.filter((u) => !u.isActive)
    const invitesPending = active.filter((u) => !u.emailConfirmed)
    const profileByUserId = new Map(profiles.map((p) => [p.userId, p]))
    const userById = new Map(users.map((u) => [u.id, u]))

    const roles = { systemAdmins: 0, hrAdmins: 0, managers: 0, employees: 0 }
    for (const u of active) {
        if (u.roles.includes('System Administrator')) roles.systemAdmins++
        else if (u.roles.includes('HR Administrator')) roles.hrAdmins++
        else if (u.roles.includes('Manager')) roles.managers++
        else roles.employees++
    }

    const noDepartment = active.filter((u) => !isAdministrator(u.roles) && profileByUserId.get(u.id)?.departmentId == null)

    const activeDepartments = departments.filter((d) => d.isActive).sort((a, b) => a.name.localeCompare(b.name))
    const departmentRows = activeDepartments.map((department) => {
        const members = profiles.filter((p) => p.departmentId === department.id && userById.get(p.userId)?.isActive)
        const managers = members
            .map((p) => userById.get(p.userId))
            .filter((u): u is AdminUser => !!u && u.roles.includes('Manager'))
            .map((u) => u.displayName || u.email)
        return { department, headcount: members.length, managers }
    })

    return {
        active, deactivated, invitesPending, noDepartment, roles, activeDepartments,
        withoutManager: departmentRows.filter((r) => r.managers.length === 0).map((r) => r.department),
        emptyDepartments: departmentRows.filter((r) => r.headcount === 0).map((r) => r.department),
        departmentRows,
        placed: departmentRows.reduce((sum, r) => sum + r.headcount, 0),
    }
}

function buildWorkspaceSummary(w: WorkspaceOverview) {
    const parts: React.ReactNode[] = []
    parts.push(<><strong>{w.active.length} active account{w.active.length === 1 ? '' : 's'}</strong> across <strong>{w.activeDepartments.length} department{w.activeDepartments.length === 1 ? '' : 's'}</strong></>)
    if (w.invitesPending.length > 0) parts.push(<><strong>{w.invitesPending.length}</strong> {w.invitesPending.length === 1 ? 'invite has' : 'invites have'} not been accepted yet</>)
    if (w.withoutManager.length > 0) parts.push(<><strong>{w.withoutManager.length}</strong> department{w.withoutManager.length === 1 ? ' has' : 's have'} no manager</>)
    if (w.invitesPending.length === 0 && w.withoutManager.length === 0 && w.noDepartment.length === 0) parts.push(<>everyone is placed and confirmed</>)
    return joinParts(parts)
}

function InfoRow({ label, value, tone, last }: { label: string; value: string; tone?: 'ok' | 'warn'; last?: boolean }) {
    return (
        <Box sx={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px',
            py: '8px', borderBottom: last ? 'none' : '1px solid', borderColor: 'divider',
        }}>
            <Box sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>{label}</Box>
            <Box sx={{
                fontSize: 12, fontWeight: 600, textAlign: 'right',
                color: tone === 'warn' ? 'warning.dark' : tone === 'ok' ? 'success.dark' : 'text.primary',
            }}>{value}</Box>
        </Box>
    )
}

function monthName(month: number) {
    return new Date(2000, Math.min(12, Math.max(1, month)) - 1, 1).toLocaleDateString('en-GB', { month: 'long' })
}

/* ════════════════════════════════════════════════════════════════════════ */
/* HR ADMINISTRATOR                                                         */
/* ════════════════════════════════════════════════════════════════════════ */

/**
 * The HR Administrator's opening screen. They hold the administrator's reach over
 * leave and time and none of the configuration, so this shows the work that reach
 * exists for: every decision waiting on them (with Approve and Reject to hand, the
 * way the manager's queue does, but across all departments), who is away now and
 * in the next fortnight, balances that need a word with somebody, and today's
 * attendance by department. Nothing here is about the workspace itself.
 */
function HrDashboard({ user }: { user: UserInfo }) {
    const { uiStore } = useStore()
    const queryClient = useQueryClient()
    const today = useMemo(() => { const d = new Date(); d.setHours(0, 0, 0, 0); return d }, [])
    const now = useNow()

    const { data: leaves = [], isLoading: lLoading } = useQuery({ queryKey: ['annualLeaves'], queryFn: getAnnualLeaves })
    const { data: timesheets = [], isLoading: tLoading } = useQuery({ queryKey: ['timesheets'], queryFn: getTimesheets })
    const { data: leaveTypes = [] } = useQuery({ queryKey: ['leaveTypes'], queryFn: getLeaveTypes })
    const { data: profiles = [] } = useQuery({ queryKey: ['employeeProfiles'], queryFn: getEmployeeProfiles })
    const { data: company } = useQuery({ queryKey: ['attendance', 'company'], queryFn: getCompanyAttendance })
    const { data: settings } = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings })

    const leaveTypeById = useMemo(() => new Map(leaveTypes.map((lt) => [lt.id, lt])), [leaveTypes])
    const myProfileId = useMemo(() => profiles.find((p) => p.userId === user.id)?.id, [profiles, user.id])

    // Everybody's, except the HR Administrator's own — nobody approves their own
    // request — and except what is the manager's to decide: a Pending row on a type
    // asking for the manager reaches this queue once the manager has passed it on
    // (isWithManager, mirroring ApprovalStageRule).
    const pendingLeaves = useMemo(
        () => leaves.filter((l) => isOpenStatus(l.status) && l.employeeId !== user.id
                && !isWithManager(l, l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined, HR_VIEWER))
            .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
        [leaves, leaveTypeById, user.id],
    )
    // Likewise the timesheets: a submitted sheet a manager is available to review is
    // the manager's (isTimesheetWithManager); HR's queue holds the ones nobody else
    // can review — a manager's own, a department with no manager, every manager away.
    const pendingTs = useMemo(
        () => timesheets.filter((t) => (t.status === 'Submitted' || t.status === 'Resubmitted') && t.employeeId !== myProfileId
                && !isTimesheetWithManager(t, HR_VIEWER))
            .sort((a, b) => new Date(a.submittedAt ?? a.createdAt).getTime() - new Date(b.submittedAt ?? b.createdAt).getTime()),
        [timesheets, myProfileId],
    )
    const conflictMap = useMemo(() => buildConflictMap(pendingLeaves, leaves), [pendingLeaves, leaves])
    const queue = useMemo(
        () => buildApprovalQueue(pendingLeaves, pendingTs, leaveTypeById, conflictMap, now, HR_VIEWER),
        [pendingLeaves, pendingTs, leaveTypeById, conflictMap, now],
    )
    const awaitingDocument = queue.filter((q) => q.blocked).length
    const oldestPending = queue.length > 0
        ? Math.min(...queue.map((q) => new Date(q.createdAt).getTime()))
        : null

    // Who is away: approved absences that touch today or the next fourteen days.
    const horizon = useMemo(() => { const d = new Date(today); d.setDate(d.getDate() + 14); return d }, [today])
    const upcoming = useMemo(() => leaves
        .filter((l) => l.status === 'Approved')
        .map((l) => ({ leave: l, start: startOfDay(l.startDate), end: startOfDay(l.endDate) }))
        .filter(({ start, end }) => end >= today && start <= horizon)
        .sort((a, b) => a.start.getTime() - b.start.getTime() || a.leave.employeeName.localeCompare(b.leave.employeeName)),
    [leaves, today, horizon])
    const awayToday = new Set(upcoming.filter(({ start, end }) => start <= today && end >= today).map(({ leave }) => leave.employeeId)).size
    const week = useMemo(() => { const d = new Date(today); d.setDate(d.getDate() + 7); return d }, [today])
    const startingThisWeek = new Set(upcoming.filter(({ start }) => start > today && start <= week).map(({ leave }) => leave.employeeId)).size

    const balanceWatch = useMemo(
        () => buildBalanceWatch(profiles, today, settings?.leaveYearStartMonth ?? 1),
        [profiles, today, settings?.leaveYearStartMonth],
    )

    const approveLeaveMut = useMutation({
        mutationFn: (id: string) => updateLeaveStatus(id, 'Approved'),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['annualLeaves'] }),
    })
    const rejectLeaveMut = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => updateLeaveStatus(id, 'Rejected', comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['annualLeaves'] })
            void queryClient.invalidateQueries({ queryKey: ['leaveStatusHistories'] })
        },
    })
    const approveTsMut = useMutation({
        mutationFn: (id: string) => approveTimesheet(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['timesheets'] }),
    })
    const rejectTsMut = useMutation({
        mutationFn: ({ id, comment }: { id: string; comment: string }) => rejectTimesheet(id, comment),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
            void queryClient.invalidateQueries({ queryKey: ['timesheetStatusHistories'] })
        },
    })
    const isMutating = approveLeaveMut.isPending || rejectLeaveMut.isPending || approveTsMut.isPending || rejectTsMut.isPending
    const isRejecting = rejectLeaveMut.isPending || rejectTsMut.isPending

    const [rejectTarget, setRejectTarget] = useState<{ kind: 'leave' | 'timesheet'; id: string; label: string } | null>(null)

    async function confirmReject(reason: string) {
        if (!rejectTarget) return
        if (rejectTarget.kind === 'leave') {
            await rejectLeaveMut.mutateAsync({ id: rejectTarget.id, comment: reason })
        } else {
            await rejectTsMut.mutateAsync({ id: rejectTarget.id, comment: reason })
        }
        setRejectTarget(null)
    }

    if (lLoading || tLoading) return <CenterSpinner />

    const lateTs = pendingTs.filter((t) => {
        if (!t.submittedAt) return false
        const end = new Date(t.periodEnd); end.setHours(23, 59, 59, 999)
        return new Date(t.submittedAt) > end
    }).length
    const tracked = company?.total ?? profiles.filter((p) => p.departmentId != null).length

    return (
        <Box>
            <GreetingHero
                gradient={{
                    light: 'linear-gradient(135deg, #0F766E 0%, #14B8A6 100%)',
                    dark: 'linear-gradient(135deg, #042f2e 0%, #115e59 100%)',
                }}
                hello={`${greetingForHour(new Date().getHours())} · ${formatTodayLong()}`}
                name={`Hi ${firstName(user)} 👋`}
                summary={buildHrSummary({ pendingLeaves: pendingLeaves.length, pendingTs: pendingTs.length, awaitingDocument, awayToday, startingThisWeek })}
                meta={[
                    { l: 'Employees', v: `${tracked} tracked` },
                    { l: 'Away today', v: `${awayToday}` },
                    { l: 'Leave to decide', v: `${pendingLeaves.length}` },
                    { l: 'Timesheets to review', v: `${pendingTs.length}` },
                ]}
            />

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: '12px', mb: '14px' }}>
                <Gauge
                    label="Leave awaiting decision"
                    big={`${pendingLeaves.length}`}
                    bigColor={pendingLeaves.length > 0 ? 'warning.main' : 'success.main'}
                    sub={pendingLeaves.length === 0 ? 'Nothing waiting' : `${awaitingDocument} need a document · oldest ${oldestPending ? formatRelativeAge(new Date(oldestPending)) : '—'}`}
                    barColor="warning.main"
                    barPct={Math.min(100, pendingLeaves.length * 10)}
                />
                <Gauge
                    label="Timesheets to review"
                    big={`${pendingTs.length}`}
                    bigColor={pendingTs.length > 0 ? 'primary.main' : 'success.main'}
                    sub={pendingTs.length === 0 ? 'Nothing waiting' : `${lateTs} submitted late`}
                    barColor="primary.main"
                    barPct={Math.min(100, pendingTs.length * 10)}
                />
                <Gauge
                    label="Away today"
                    big={`${awayToday}`}
                    bigColor="success.main"
                    sub={tracked > 0 ? `of ${tracked} employees · ${startingThisWeek} more start this week` : `${startingThisWeek} more start this week`}
                    barColor="success.main"
                    barPct={tracked > 0 ? (awayToday / tracked) * 100 : 0}
                />
                <Gauge
                    label="Balances to watch"
                    big={`${balanceWatch.length}`}
                    bigColor={balanceWatch.length > 0 ? 'error.main' : 'success.main'}
                    sub={balanceWatch.length === 0 ? 'Everyone on track' : `${balanceWatch.filter((b) => b.flag === 'low').length} running low · ${balanceWatch.filter((b) => b.flag === 'unused').length} barely used`}
                    barColor="error.main"
                    barPct={tracked > 0 ? (balanceWatch.length / tracked) * 100 : 0}
                />
            </Box>

            <ApprovalQueueCard
                queue={queue.slice(0, 6)}
                totalQueue={queue.length}
                onApprove={(item) => item.kind === 'leave' ? approveLeaveMut.mutate(item.id) : approveTsMut.mutate(item.id)}
                onReject={(item) => setRejectTarget({ kind: item.kind, id: item.id, label: item.title })}
                disabled={isMutating}
                onViewAllLeave={() => uiStore.navigateToTeamLeave()}
                onViewAllTs={() => uiStore.navigateToTeamTimesheets()}
            />

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <WhosAwayCard
                    rows={upcoming.map(({ leave, start, end }) => ({ leave, start, end }))}
                    today={today}
                    leaveTypeById={leaveTypeById}
                    onViewAll={() => uiStore.navigateToTeamLeave()}
                />
                <BalanceWatchCard rows={balanceWatch} onViewAll={() => uiStore.navigateToTeamLeave()} />
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px', mb: '14px' }}>
                <DepartmentHealthCard
                    departments={company?.departments ?? []}
                    leaves={leaves}
                    timesheets={timesheets}
                    onLive={() => uiStore.navigateToCompanyAttendance()}
                />
                <TodaysIssuesCard issues={company?.issues ?? []} />
            </Box>

            <Box sx={{ mb: '14px' }}>
                <RecentActivityCard activity={company?.recent ?? []} />
            </Box>

            <RejectDialog
                target={rejectTarget}
                onClose={() => setRejectTarget(null)}
                onConfirm={confirmReject}
                busy={isRejecting}
            />
        </Box>
    )
}

function startOfDay(iso: string) {
    const d = new Date(iso)
    d.setHours(0, 0, 0, 0)
    return d
}

function buildHrSummary({ pendingLeaves, pendingTs, awaitingDocument, awayToday, startingThisWeek }: {
    pendingLeaves: number; pendingTs: number; awaitingDocument: number; awayToday: number; startingThisWeek: number
}) {
    const parts: React.ReactNode[] = []
    const total = pendingLeaves + pendingTs
    if (total === 0) {
        parts.push(<>Nothing is waiting for a decision</>)
    } else {
        parts.push(<><strong>{pendingLeaves} leave request{pendingLeaves === 1 ? '' : 's'}</strong> and <strong>{pendingTs} timesheet{pendingTs === 1 ? '' : 's'}</strong> {total === 1 ? 'is' : 'are'} waiting for your decision across all departments</>)
        if (awaitingDocument > 0) parts.push(<>{awaitingDocument} of the leave request{awaitingDocument === 1 ? ' needs' : 's need'} a document before {awaitingDocument === 1 ? 'it' : 'they'} can be approved</>)
    }
    parts.push(<><strong>{awayToday}</strong> {awayToday === 1 ? 'person is' : 'people are'} away today{startingThisWeek > 0 ? <>, and <strong>{startingThisWeek} more</strong> start leave in the next seven days</> : ''}</>)
    return joinParts(parts)
}

interface BalanceWatchRow {
    profile: EmployeeProfile
    entitlement: number
    balance: number
    /** `low`: two days or fewer left. `unused`: under a quarter used with half the leave year gone. */
    flag: 'low' | 'unused'
}

/**
 * Two things HR wants a word about, for everyone inside the department structure
 * (an administrator's own profile is outside it and is skipped). "Running low" is
 * two days or fewer of the year's pooled entitlement left. "Barely used" is under a
 * quarter of it taken once the leave year is half gone — the people who lose days
 * to the carryover cap in December if nobody says anything in September.
 */
function buildBalanceWatch(profiles: EmployeeProfile[], today: Date, leaveYearStartMonth: number): BalanceWatchRow[] {
    // The leave year that contains today, from the configured start month.
    const startMonth = Math.min(12, Math.max(1, leaveYearStartMonth)) - 1
    const yearStart = new Date(today.getFullYear(), startMonth, 1)
    if (yearStart > today) yearStart.setFullYear(yearStart.getFullYear() - 1)
    const yearEnd = new Date(yearStart); yearEnd.setFullYear(yearEnd.getFullYear() + 1)
    const progress = (today.getTime() - yearStart.getTime()) / (yearEnd.getTime() - yearStart.getTime())

    const rows: BalanceWatchRow[] = []
    for (const p of profiles) {
        if (p.departmentId == null) continue
        const entitlement = currentYearEntitlement(p)
        if (entitlement <= 0) continue
        const balance = Number(p.leaveBalance)
        const used = entitlement - balance
        if (balance <= 2) rows.push({ profile: p, entitlement, balance, flag: 'low' })
        else if (progress >= 0.5 && used / entitlement < 0.25) rows.push({ profile: p, entitlement, balance, flag: 'unused' })
    }
    // The ones about to run out first, then the least used.
    rows.sort((a, b) => {
        if (a.flag !== b.flag) return a.flag === 'low' ? -1 : 1
        return a.flag === 'low' ? a.balance - b.balance : b.balance - a.balance
    })
    return rows
}

function dayLabel(d: Date, today: Date) {
    const diff = Math.round((d.getTime() - today.getTime()) / 86_400_000)
    if (diff <= 0) return 'Today'
    if (diff === 1) return 'Tomorrow'
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

function WhosAwayCard({ rows, today, leaveTypeById, onViewAll }: {
    rows: { leave: AnnualLeave; start: Date; end: Date }[]
    today: Date
    leaveTypeById: Map<number, LeaveType>
    onViewAll: () => void
}) {
    const visible = rows.slice(0, 8)
    const remaining = rows.length - visible.length
    return (
        <ActionCard
            title="Who's away"
            icon="🌴"
            countLabel={rows.length > 0 ? `${rows.length} in the next 14 days` : undefined}
            action={<OutlineBtn onClick={onViewAll}>Leave calendar</OutlineBtn>}
        >
            {visible.length === 0 ? (
                <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '12px' }}>
                    Nobody is away today or in the next two weeks.
                </Box>
            ) : (
                <Box>
                    {visible.map(({ leave, start, end }, i) => {
                        const lt = leave.leaveTypeId != null ? leaveTypeById.get(leave.leaveTypeId) : undefined
                        const isNow = start <= today && end >= today
                        const label = isNow ? (end.getTime() === today.getTime() ? 'Back tomorrow' : `Back ${formatDateShort(nextWorkingDay(leave.endDate))}`) : dayLabel(start, today)
                        return (
                            <Box key={leave.id} sx={{
                                display: 'grid', gridTemplateColumns: '32px 1fr auto', gap: '10px', alignItems: 'center',
                                py: '9px', borderBottom: i === visible.length - 1 ? 'none' : '1px solid', borderColor: 'divider',
                            }}>
                                <Box sx={{
                                    width: 32, height: 32, borderRadius: '50%', bgcolor: avatarBgFor(leave.employeeName), color: '#fff',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 600,
                                }}>{initials(leave.employeeName)}</Box>
                                <Box sx={{ minWidth: 0 }}>
                                    <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                        {leave.employeeName}
                                        {leave.departmentName && (
                                            <Box component="span" sx={{ fontSize: 10, fontWeight: 500, color: 'info.dark', bgcolor: softBg('info'), px: '6px', py: '1px', borderRadius: '4px' }}>
                                                {leave.departmentName}
                                            </Box>
                                        )}
                                    </Box>
                                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '2px' }}>
                                        {iconForLeaveType(lt?.name)} {lt?.name ?? 'Leave'} · {formatRange(leave.startDate, leave.endDate)} · {leave.totalDays} day{leave.totalDays === 1 ? '' : 's'}
                                    </Box>
                                </Box>
                                <Box sx={{
                                    fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap',
                                    color: isNow ? 'success.dark' : 'text.secondary',
                                    bgcolor: isNow ? softBg('success') : 'action.hover',
                                    px: '8px', py: '3px', borderRadius: '10px',
                                }}>{isNow ? `Away · ${label}` : label}</Box>
                            </Box>
                        )
                    })}
                    {remaining > 0 && (
                        <Box sx={{ fontSize: 11, color: 'text.secondary', pt: '10px', textAlign: 'center' }}>
                            +{remaining} more in the next two weeks
                        </Box>
                    )}
                </Box>
            )}
        </ActionCard>
    )
}

function BalanceWatchCard({ rows, onViewAll }: { rows: BalanceWatchRow[]; onViewAll: () => void }) {
    const visible = rows.slice(0, 6)
    return (
        <ActionCard
            title="Balances to watch"
            icon="⚖️"
            countLabel={rows.length > 0 ? `${rows.length} to talk to` : undefined}
            countTone={rows.some((r) => r.flag === 'low') ? 'urgent' : 'normal'}
            action={<OutlineBtn onClick={onViewAll}>All leave</OutlineBtn>}
        >
            {visible.length === 0 ? (
                <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '12px' }}>
                    Nobody is running out of leave, and nobody is sitting on an unused year.
                </Box>
            ) : (
                <Box>
                    {visible.map((r, i) => {
                        const usedPct = Math.max(0, Math.min(100, ((r.entitlement - r.balance) / r.entitlement) * 100))
                        const low = r.flag === 'low'
                        return (
                            <Box key={r.profile.id} sx={{ py: '9px', borderBottom: i === visible.length - 1 ? 'none' : '1px solid', borderColor: 'divider' }}>
                                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '10px', mb: '6px' }}>
                                    <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                        {r.profile.displayName}
                                    </Box>
                                    <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                                        <Box component="span" sx={{
                                            fontSize: 10, fontWeight: 600, px: '6px', py: '2px', borderRadius: '4px',
                                            bgcolor: low ? softBg('error') : softBg('warning'), color: low ? 'error.dark' : 'warning.dark',
                                        }}>{low ? 'Running low' : 'Barely used'}</Box>
                                        <Box sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>
                                            <strong style={{ color: 'inherit' }}>{r.balance}</strong> of {r.entitlement} left
                                        </Box>
                                    </Box>
                                </Box>
                                <Box sx={{ height: 5, bgcolor: 'action.hover', borderRadius: '3px', overflow: 'hidden' }}>
                                    <Box sx={{ height: '100%', width: `${usedPct}%`, bgcolor: low ? 'error.main' : 'warning.main', borderRadius: '3px' }} />
                                </Box>
                            </Box>
                        )
                    })}
                    {rows.length > visible.length && (
                        <Box sx={{ fontSize: 11, color: 'text.secondary', pt: '10px', textAlign: 'center' }}>
                            +{rows.length - visible.length} more
                        </Box>
                    )}
                </Box>
            )}
        </ActionCard>
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
/** The HR dashboard's viewer, hoisted so the memoised queue does not see a fresh object every render. */
const HR_VIEWER: ApprovalViewer = { isHrAdministrator: true }

interface QueueItem {
    kind: 'leave' | 'timesheet'
    id: string
    name: string
    title: string
    // A node, not a string: the leave icon is an SVG for the sensitive types.
    meta: React.ReactNode
    tags: QueueTag[]
    createdAt: string
    urgent: boolean
    /** Why Approve is not offered — a document the leave type insists on is still missing. */
    blocked?: string
    /** False when the row is with HR and this viewer is not HR — shown, but with no buttons. */
    decidable: boolean
    /** "Approve" or "Approve & send to HR", following `approveOutcome`. Always "Approve" for a timesheet. */
    approveLabel: string
}

/**
 * Pending leave requests that overlap another pending or approved absence in the
 * same department — leave id → the colleagues it overlaps with.
 */
function buildConflictMap(pendingLeaves: AnnualLeave[], leaves: AnnualLeave[]) {
    const result = new Map<string, string[]>()
    for (const a of pendingLeaves) {
        const overlapping = leaves.filter((b) =>
            b.id !== a.id
            && b.departmentName === a.departmentName
            && (isOpenStatus(b.status) || b.status === 'Approved')
            && b.startDate <= a.endDate && b.endDate >= a.startDate
        )
        if (overlapping.length > 0) {
            result.set(a.id, overlapping.map((b) => b.employeeName))
        }
    }
    return result
}

/**
 * The approval queue both the manager's and the HR Administrator's dashboards
 * show: pending leave and submitted timesheets merged, urgent first, then oldest.
 * `leaveTypeById` also decides whether a request is short a document its type
 * requires — such a row is shown but its Approve is held, since the API would
 * refuse it (`AttachmentPolicyRule`).
 */
function buildApprovalQueue(
    pendingLeaves: AnnualLeave[],
    pendingTs: Timesheet[],
    leaveTypeById: Map<number, { name: string; attachmentPolicy: LeaveType['attachmentPolicy']; requiresManagerApproval: boolean; requiresHrApproval?: boolean }>,
    conflictMap: Map<string, string[]>,
    now: number,
    viewer: ApprovalViewer,
): QueueItem[] {
    const items: QueueItem[] = []
    for (const l of pendingLeaves) {
        const lt = l.leaveTypeId != null ? leaveTypeById.get(l.leaveTypeId) : undefined
        const startD = new Date(l.startDate)
        const daysNotice = Math.round((startD.getTime() - now) / 86_400_000)
        const tags: QueueTag[] = []
        if (daysNotice >= 0 && daysNotice < 1) tags.push({ label: '⚠ < 1 day notice', tone: 'urgent' })
        const awaitingDocument = isAwaitingDocument(lt, l.evidenceUrl)
        if (awaitingDocument) tags.push({ label: '📎 Document needed', tone: 'warning' })
        else if (l.evidenceUrl) tags.push({ label: '📎 Document attached', tone: 'info' })
        const conflicts = conflictMap.get(l.id)
        if (conflicts && conflicts.length > 0) tags.push({ label: `⚠ Overlaps with ${conflicts[0]}`, tone: 'conflict' })
        const decidable = canDecide(l, viewer, lt)
        if (!decidable) tags.push({ label: 'With HR', tone: 'info' })
        items.push({
            kind: 'leave',
            id: l.id,
            name: l.employeeName,
            title: `${l.employeeName} · ${lt?.name ?? 'Leave'}`,
            meta: <>{iconForLeaveType(lt?.name)} {l.totalDays} day{l.totalDays === 1 ? '' : 's'} · {formatRange(l.startDate, l.endDate)}</>,
            tags,
            createdAt: l.createdAt,
            urgent: daysNotice >= 0 && daysNotice < 1,
            blocked: awaitingDocument ? 'Document needed before approval' : undefined,
            decidable,
            approveLabel: approveButtonLabel(approveOutcome(l, lt, viewer)),
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
            decidable: true,
            approveLabel: 'Approve',
        })
    }
    items.sort((a, b) => {
        if (a.urgent !== b.urgent) return a.urgent ? -1 : 1
        return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
    })
    return items
}

/**
 * One reject flow for both approval queues: a required reason, sent as the
 * status comment the employee reads. `target` null closes it.
 */
function RejectDialog({ target, onClose, onConfirm, busy }: {
    target: { kind: 'leave' | 'timesheet'; id: string; label: string } | null
    onClose: () => void
    onConfirm: (reason: string) => Promise<void>
    busy: boolean
}) {
    return (
        <Dialog open={target !== null} onClose={() => { if (!busy) onClose() }} maxWidth="xs" fullWidth>
            <DialogTitle sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary', pb: 1 }}>
                Reject {target?.kind === 'leave' ? 'leave request' : 'timesheet'}
            </DialogTitle>
            {/* Keyed by the request, so every one opens with an empty box rather than
                the last request's reason. */}
            {target && <RejectDialogBody key={`${target.kind}-${target.id}`} target={target} onClose={onClose} onConfirm={onConfirm} busy={busy} />}
        </Dialog>
    )
}

function RejectDialogBody({ target, onClose, onConfirm, busy }: {
    target: { kind: 'leave' | 'timesheet'; id: string; label: string }
    onClose: () => void
    onConfirm: (reason: string) => Promise<void>
    busy: boolean
}) {
    const [reason, setReason] = useState('')
    const [error, setError] = useState('')

    async function confirm() {
        const trimmed = reason.trim()
        if (trimmed.length === 0) {
            setError('Please provide a reason for rejecting.')
            return
        }
        try {
            await onConfirm(trimmed)
        } catch { /* mutation error is surfaced elsewhere */ }
    }

    return (
        <>
            <DialogContent sx={{ px: 3, py: 2 }}>
                <Stack spacing={1.5}>
                    <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>
                        Please provide a reason. The employee will see this message.
                    </Typography>
                    <Box sx={{ fontSize: 12, color: 'text.secondary' }}>{target.label}</Box>
                    <TextField
                        autoFocus
                        multiline
                        minRows={3}
                        maxRows={6}
                        fullWidth
                        placeholder="Reason for rejection (required)"
                        value={reason}
                        onChange={(e) => {
                            setReason(e.target.value)
                            if (error) setError('')
                        }}
                        error={!!error}
                        helperText={error || `${reason.trim().length}/500`}
                        inputProps={{ maxLength: 500 }}
                        sx={{ '& .MuiInputBase-input': { fontSize: 13 } }}
                    />
                </Stack>
            </DialogContent>
            <DialogActions sx={{ px: 3, py: 1.75, gap: 1 }}>
                <Button
                    size="small"
                    onClick={onClose}
                    disabled={busy}
                    sx={{ textTransform: 'none', color: 'text.secondary' }}
                >
                    Cancel
                </Button>
                <Button
                    size="small"
                    variant="contained"
                    disabled={busy || reason.trim().length === 0}
                    onClick={() => void confirm()}
                    sx={{ textTransform: 'none', bgcolor: 'error.main', '&:hover': { bgcolor: 'error.dark' }, boxShadow: 'none' }}
                >
                    {busy ? 'Rejecting…' : 'Confirm Reject'}
                </Button>
            </DialogActions>
        </>
    )
}

function CenterSpinner() {
    return (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
            <CircularProgress size={28} />
        </Box>
    )
}

// Each hero call site supplies a {light, dark} gradient pair. Light is the
// original bright brand gradient; dark is a desaturated variant so the hero
// doesn't shout against a dark page background.
type HeroGradient = { light: string; dark: string }

function GreetingHero({ gradient, hello, name, summary, meta }: {
    gradient: HeroGradient
    hello: string
    name: string
    summary: React.ReactNode
    meta: { l: string; v: string }[]
}) {
    return (
        <Box sx={(theme) => ({
            background: theme.palette.mode === 'dark' ? gradient.dark : gradient.light,
            color: '#fff', borderRadius: '14px',
            p: { xs: '20px', md: '24px 28px' }, mb: '14px',
            position: 'relative', overflow: 'hidden',
        })}>
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
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px',
            overflow: 'hidden', mb: '14px',
        }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: '1px solid', borderColor: 'divider',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                    {icon && <Box component="span">{icon}</Box>}
                    {title}
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    {countLabel && (
                        <Box component="span" sx={{
                            fontSize: 11, fontWeight: 600, px: '8px', py: '3px',
                            borderRadius: '12px',
                            bgcolor: countTone === 'urgent' ? softBg('error') : 'action.hover',
                            color: countTone === 'urgent' ? 'error.dark' : 'text.secondary',
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
                bgcolor: item.tone === 'urgent' ? softBg('warning') : 'action.hover',
                border: '1px solid',
                borderColor: item.tone === 'urgent' ? 'warning.light' : 'divider',
                mb: '8px', transition: 'all 0.15s',
                '&:last-child': { mb: 0 },
                '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary') },
            }}
        >
            <Box sx={{ fontSize: 20 }}>{item.icon}</Box>
            <Box sx={{ flex: 1, minWidth: 0 }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '2px' }}>{item.label}</Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>{item.sub}</Box>
            </Box>
            <Box sx={{ color: 'text.disabled', fontSize: 18 }}>›</Box>
        </Box>
    )
}

function OutlineBtn({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                bgcolor: 'background.paper', color: 'primary.main', border: '1px solid', borderColor: 'primary.main',
                px: '12px', py: '5px', borderRadius: '6px', fontSize: 12, fontWeight: 500,
                cursor: 'pointer', fontFamily: 'inherit',
                '&:hover': { bgcolor: softBg('primary') },
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
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', overflow: 'hidden' }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: '1px solid', borderColor: 'divider',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                    <Box component="span">⏱</Box>
                    This week · {formatRange(ts.periodStart, ts.periodEnd)}
                </Box>
                <Box
                    component="button"
                    onClick={onContinue}
                    sx={{
                        bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    Continue →
                </Box>
            </Box>
            <Box sx={{ p: '14px 18px' }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '6px' }}>
                    <Box sx={{ fontSize: 24, fontWeight: 700, color: 'text.primary' }}>
                        {hours.toFixed(1)}
                        <Box component="span" sx={{ fontSize: 14, color: 'text.secondary', fontWeight: 500 }}>{` / ${WEEKLY_TARGET}h`}</Box>
                    </Box>
                    <Box sx={{ fontSize: 11, color: hoursRemaining > 0 ? 'warning.main' : 'success.main', fontWeight: 600 }}>
                        {hoursRemaining > 0 ? `${hoursRemaining.toFixed(1)}h remaining` : '✓ Complete'}
                    </Box>
                </Box>
                <Box sx={{ height: 8, bgcolor: 'action.hover', borderRadius: '4px', overflow: 'hidden', mb: '12px' }}>
                    <Box sx={{ height: '100%', bgcolor: 'primary.main', borderRadius: '4px', width: `${pct}%` }} />
                </Box>
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: '4px' }}>
                    {DAY_LABELS.map((d, i) => {
                        const h = daily[i] ?? 0
                        const filled = h > 0
                        const isToday = i === todayIdx
                        return (
                            <Box key={d} sx={{
                                p: '6px 8px', borderRadius: '5px', textAlign: 'center', fontSize: 11,
                                bgcolor: isToday ? softBg('primary') : filled ? softBg('success') : 'action.hover',
                                boxShadow: isToday ? (theme) => `inset 0 0 0 1px ${theme.palette.primary.main}` : 'none',
                            }}>
                                <Box sx={{ fontSize: 9, color: isToday ? 'info.dark' : 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    {d}{isToday ? ' · Today' : ''}
                                </Box>
                                <Box sx={{
                                    fontWeight: 600, fontVariantNumeric: 'tabular-nums', mt: '2px',
                                    color: isToday ? 'info.dark' : filled ? 'success.dark' : 'text.disabled',
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
            bgcolor: 'action.hover', border: '1px dashed', borderColor: 'divider', borderRadius: '12px',
            p: '24px', textAlign: 'center',
        }}>
            <Box sx={{ fontSize: 28, mb: '8px' }}>📝</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>No timesheet for this week</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', mb: '12px' }}>Start tracking your hours.</Box>
            <Box
                component="button"
                onClick={onOpen}
                sx={{
                    bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                    px: '14px', py: '6px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                    fontFamily: 'inherit', '&:hover': { bgcolor: 'primary.dark' },
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
    const isPending = isOpenStatus(leave.status)
    const countdown = until === 0 ? 'Today' : until === 1 ? 'Tomorrow' : `In ${until} days`
    const sameDay = leave.startDate.slice(0, 10) === leave.endDate.slice(0, 10)

    return (
        <Box sx={(theme) => ({
            background: theme.palette.mode === 'dark'
                ? 'linear-gradient(135deg, #1e3a8a 0%, #0f172a 100%)'
                : 'linear-gradient(135deg, #4F8EF7 0%, #3A7AE4 100%)',
            color: '#fff', borderRadius: '12px', p: '20px 22px',
            position: 'relative', overflow: 'hidden',
            '&::before': {
                content: '"🌴"', position: 'absolute', right: -10, bottom: -20,
                fontSize: 110, opacity: 0.15, transform: 'rotate(-12deg)',
            },
        })}>
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
            bgcolor: 'action.hover', border: '1px dashed', borderColor: 'divider', borderRadius: '12px',
            p: '24px', textAlign: 'center', color: 'text.secondary',
        }}>
            <Box sx={{ fontSize: 28, mb: '8px' }}>🏖️</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>No upcoming leave</Box>
            <Box sx={{ fontSize: 12, mb: '12px' }}>Time to plan your next break?</Box>
            <Box
                component="button"
                onClick={onApply}
                sx={{
                    bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                    px: '14px', py: '6px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                    fontFamily: 'inherit', '&:hover': { bgcolor: 'primary.dark' },
                }}
            >
                + Apply for leave
            </Box>
        </Box>
    )
}

function LeaveBalanceList({ rows }: { rows: LeaveBalanceRow[] }) {
    if (rows.length === 0) {
        return <Box sx={{ fontSize: 12, color: 'text.secondary', py: '8px' }}>No active leave types.</Box>
    }

    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            {rows.map((r) => {
                const pct = r.total > 0 ? Math.min(100, (r.used / r.total) * 100) : 0
                const fillColor = !r.tracked ? 'text.disabled' : pct >= 90 ? 'error.main' : pct >= 70 ? 'warning.main' : 'success.main'
                return (
                    <Box key={r.id} sx={{ display: 'grid', gridTemplateColumns: '28px 1fr auto', gap: '10px', alignItems: 'center' }}>
                        <Box sx={{ fontSize: 18 }}>{iconForLeaveType(r.name)}</Box>
                        <Box>
                            <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>{r.name}</Box>
                            <Box sx={{ height: 5, bgcolor: 'action.hover', borderRadius: '3px', mt: '5px', overflow: 'hidden' }}>
                                <Box sx={{ height: '100%', borderRadius: '3px', bgcolor: fillColor, width: `${pct}%` }} />
                            </Box>
                        </Box>
                        <Box sx={{ fontSize: 13, color: 'text.secondary', fontVariantNumeric: 'tabular-nums', textAlign: 'right', minWidth: 50 }}>
                            {r.tracked && r.total > 0 ? (
                                <>
                                    <Box component="strong" sx={{ fontSize: 14, color: 'text.primary', fontWeight: 700 }}>{r.remaining}</Box>
                                    /{r.total}
                                </>
                            ) : (
                                <Box component="strong" sx={{ fontSize: 14, color: 'text.primary', fontWeight: 700 }}>{r.used}</Box>
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
                        bgcolor: 'action.hover', border: '1px solid', borderColor: 'divider', borderRadius: '8px',
                        cursor: 'pointer', textAlign: 'left', fontFamily: 'inherit',
                        transition: 'all 0.15s',
                        '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary'), transform: 'translateY(-1px)' },
                    }}
                >
                    <Box sx={{ fontSize: 20 }}>{t.icon}</Box>
                    <Box>
                        <Box sx={{ fontSize: 12, fontWeight: 600, color: 'text.primary' }}>{t.label}</Box>
                        <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '1px' }}>{t.sub}</Box>
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
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px',
            overflow: 'hidden', mb: '14px',
        }}>
            <Box sx={{
                px: '18px', py: '14px', borderBottom: '1px solid', borderColor: 'divider',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap',
            }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <Box component="span" sx={{ fontSize: 16 }}>⚡</Box>
                    <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>Approval queue</Box>
                    {totalQueue > 0 && (
                        <Box component="span" sx={{
                            bgcolor: softBg('error'), color: 'error.dark',
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
                <Box sx={{ p: '24px', textAlign: 'center', color: 'text.secondary', fontSize: 13 }}>
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
            borderBottom: isLast ? 'none' : (theme: Theme) => `1px solid ${theme.palette.divider}`,
            '&:hover': { bgcolor: 'action.hover' },
        }}>
            <Box sx={{
                width: 36, height: 36, borderRadius: '50%',
                bgcolor: item.urgent ? 'error.main' : avatarBgFor(item.name),
                color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, fontWeight: 600,
            }}>{initials(item.name)}</Box>
            <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary', mb: '2px' }}>{item.title}</Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary', display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                    <Box component="span">{item.meta}</Box>
                    {item.tags.map((t, j) => <QueueTagPill key={j} tag={t} />)}
                </Box>
            </Box>
            <Box sx={{
                fontSize: 11, color: item.urgent ? 'error.dark' : 'text.secondary', fontWeight: item.urgent ? 600 : 400,
                whiteSpace: 'nowrap', display: { xs: 'none', md: 'block' },
            }}>{age}</Box>
            <Box sx={{ display: 'flex', gap: '6px', gridColumn: { xs: '1 / -1', md: 'auto' }, mt: { xs: '8px', md: 0 } }}>
                {item.decidable ? (
                    <>
                        <Box
                            component="button"
                            onClick={onApprove}
                            disabled={disabled || !!item.blocked}
                            title={item.blocked}
                            sx={{
                                bgcolor: 'success.main', color: '#fff', border: 'none', borderRadius: '6px',
                                px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                                fontFamily: 'inherit',
                                '&:hover:not(:disabled)': { bgcolor: 'success.dark' },
                                '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                            }}
                        >
                            {item.approveLabel}
                        </Box>
                        <Box
                            component="button"
                            onClick={onReject}
                            disabled={disabled}
                            sx={{
                                bgcolor: 'error.main', color: '#fff', border: 'none', borderRadius: '6px',
                                px: '12px', py: '5px', fontSize: 12, fontWeight: 500, cursor: 'pointer',
                                fontFamily: 'inherit',
                                '&:hover:not(:disabled)': { bgcolor: 'error.dark' },
                                '&:disabled': { opacity: 0.5, cursor: 'not-allowed' },
                            }}
                        >
                            Reject
                        </Box>
                    </>
                ) : null}
            </Box>
        </Box>
    )
}

function QueueTagPill({ tag }: { tag: QueueTag }) {
    const styles =
        tag.tone === 'urgent'   ? { bg: softBg('error'), color: 'error.dark' } :
        tag.tone === 'warning'  ? { bg: softBg('warning'), color: 'warning.dark' } :
        tag.tone === 'conflict' ? { bg: softBg('warning'), color: 'warning.dark' } :
                                  { bg: softBg('info'), color: 'info.dark' }
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
    const colors = ['primary.main', 'success.main', 'warning.main', '#8B5CF6', '#EC4899', '#06B6D4', '#84CC16']
    let hash = 0
    for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) | 0
    return colors[Math.abs(hash) % colors.length]
}

function TeamStatusNowCard({ team }: { team: TeamAttendance | null }) {
    if (!team) {
        return (
            <ActionCard title="Team status now" icon="👥">
                <Box sx={{ fontSize: 12, color: 'text.secondary' }}>Loading team attendance…</Box>
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
                <Box component="span" sx={{ fontSize: 11, color: 'success.main', display: 'flex', alignItems: 'center', gap: '5px' }}>
                    <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
                    Live
                </Box>
            }
        >
            <Box sx={{ display: 'flex', gap: '14px', mb: '14px', fontSize: 11, color: 'text.secondary', flexWrap: 'wrap' }}>
                <Box><Box component="strong" sx={{ color: 'success.main', fontSize: 14 }}>{inCount}</Box> working</Box>
                <Box><Box component="strong" sx={{ color: 'warning.main', fontSize: 14 }}>{brkCount}</Box> on break</Box>
                <Box><Box component="strong" sx={{ color: 'primary.main', fontSize: 14 }}>{leaveCount}</Box> on leave</Box>
                <Box><Box component="strong" sx={{ color: 'text.disabled', fontSize: 14 }}>{outCount}</Box> not in</Box>
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: '8px' }}>
                {team.members.slice(0, 12).map((m) => <TeamMemberTile key={m.employeeId} member={m} />)}
            </Box>
        </ActionCard>
    )
}

function TeamMemberTile({ member }: { member: TeamMemberAttendance }) {
    const tone =
        member.status === 'in'    ? { border: 'success.main', sub: 'In', subColor: 'success.main' } :
        member.status === 'break' ? { border: 'warning.main', sub: '☕ Break', subColor: 'warning.main' } :
        member.status === 'leave' ? { border: 'primary.main', sub: '🌴 On leave', subColor: 'primary.main' } :
                                    { border: 'text.disabled', sub: 'Not in', subColor: 'text.disabled' }
    const detail =
        member.status === 'in' && member.checkInAt
            ? `In since ${new Date(member.checkInAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
            : member.status === 'break' && member.onBreakSince
                ? `Since ${new Date(member.onBreakSince).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
                : tone.sub
    return (
        <Box sx={{
            display: 'flex', alignItems: 'center', gap: '8px', p: '8px 10px',
            bgcolor: 'action.hover', border: '1px solid', borderColor: 'divider', borderRadius: '8px',
            borderLeft: `3px solid ${tone.border}`,
        }}>
            <Box sx={{
                width: 28, height: 28, borderRadius: '50%',
                bgcolor: avatarBgFor(member.employeeName), color: '#fff',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 10, fontWeight: 600, flexShrink: 0,
            }}>{initials(member.employeeName)}</Box>
            <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
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
                    <Box sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary' }}>
                        {submitted}
                        <Box component="span" sx={{ fontSize: 14, color: 'text.secondary', fontWeight: 500 }}>
                            {` / ${total} submitted`}
                        </Box>
                    </Box>
                    {outstanding > 0 && (
                        <Box sx={{ fontSize: 11, color: 'warning.main', fontWeight: 600 }}>{outstanding} outstanding</Box>
                    )}
                </Box>
                <Box sx={{ height: 8, bgcolor: 'action.hover', borderRadius: '4px', overflow: 'hidden' }}>
                    <Box sx={{ height: '100%', bgcolor: 'success.main', borderRadius: '4px', width: `${pct}%` }} />
                </Box>
            </Box>
            {missing.length === 0 ? (
                <Box sx={{ fontSize: 12, color: 'success.main', textAlign: 'center', py: '6px' }}>
                    ✓ Everyone has submitted for this week.
                </Box>
            ) : (
                <>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '8px', fontWeight: 600 }}>
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
                                    <Box sx={{ fontSize: 12, fontWeight: 500, color: 'text.primary' }}>{m.name}</Box>
                                    <Box sx={{ fontSize: 10, color: 'text.secondary' }}>{m.note}</Box>
                                </Box>
                            </Box>
                        ))}
                    </Box>
                </>
            )}
        </ActionCard>
    )
}

function TeamHealthCard({ leaves, teamHistory }: {
    leaves: AnnualLeave[]
    teamHistory: TeamHistory | null
}) {
    const theme = useTheme()
    const deptPalette = [
        theme.palette.primary.main,
        theme.palette.success.main,
        theme.palette.warning.main,
        theme.palette.info.main,
        theme.palette.secondary.main,
        theme.palette.error.main,
    ]
    // Bar chart: total approved leave days per department over the last 6 months.
    const leaveByDept = useMemo(() => {
        const cutoff = new Date()
        cutoff.setMonth(cutoff.getMonth() - 6)
        cutoff.setHours(0, 0, 0, 0)
        const map = new Map<string, number>()
        for (const l of leaves) {
            if (l.status !== 'Approved') continue
            if (new Date(l.startDate) < cutoff) continue
            const dept = l.departmentName || 'Unassigned'
            map.set(dept, (map.get(dept) ?? 0) + l.totalDays)
        }
        return [...map.entries()]
            .map(([name, days]) => ({ name, days }))
            .sort((a, b) => b.days - a.days)
    }, [leaves])

    // Line chart: per-member check-in hour over last 30 days. Each member
    // gets a series; null check-in days (weekend / leave / absent) render as
    // gaps so a "drift later" pattern is easy to spot visually.
    const lineSeries = useMemo(() => {
        if (!teamHistory?.members?.length) return { dates: [], series: [] }
        const dates = teamHistory.members[0]?.days.map((d) => d.date) ?? []
        const series = teamHistory.members.map((m) => ({
            label: m.employeeName,
            data: m.days.map((d) => d.checkInMinutesFromMidnight),
            showMark: false,
            connectNulls: false,
        }))
        return { dates, series }
    }, [teamHistory])

    const noLeaveData = leaveByDept.length === 0
    const noCheckInData = lineSeries.series.length === 0
        || lineSeries.series.every((s) => s.data.every((v) => v == null))

    // y-axis bounds for the check-in chart. A fixed 06:00–12:00 window silently
    // dropped anyone checking in outside it — the line vanished while the axis
    // and legend still drew — so bracket the real values to the whole hour
    // either side, with a two-hour floor on the span so a tight cluster of
    // check-ins still gets a readable axis.
    const checkInBounds = useMemo(() => {
        const values = lineSeries.series
            .flatMap((s) => s.data)
            .filter((v): v is number => v != null)
        if (values.length === 0) return { min: 6 * 60, max: 12 * 60 }
        let min = Math.max(0, (Math.floor(Math.min(...values) / 60) - 1) * 60)
        let max = Math.min(24 * 60, (Math.ceil(Math.max(...values) / 60) + 1) * 60)
        if (max - min < 120) {
            max = Math.min(24 * 60, min + 120)
            min = Math.max(0, max - 120)
        }
        return { min, max }
    }, [lineSeries])

    // y-axis tick labels for the line chart: minutes-from-midnight → "HH:mm".
    const formatMinutes = (mins: number | null) => {
        if (mins == null) return ''
        const h = Math.floor(mins / 60)
        const m = Math.floor(mins % 60)
        return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
    }

    const shortDate = (iso: string) => {
        const d = new Date(iso)
        return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    }

    return (
        <ActionCard title="Team Health" icon="📊">
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: '14px' }}>
                <Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, mb: '4px' }}>
                        Leave taken by department · last 6 months
                    </Box>
                    {noLeaveData ? (
                        <Box sx={{ fontSize: 12, color: 'text.disabled', textAlign: 'center', py: '40px' }}>
                            No approved leave in the last 6 months.
                        </Box>
                    ) : (
                        <Box sx={{
                            '& .MuiChartsAxis-tick, & .MuiChartsAxis-line': { stroke: theme.palette.divider },
                            '& .MuiChartsAxis-tickLabel, & .MuiChartsAxis-label': { fill: theme.palette.text.secondary },
                            '& .MuiBarLabel-root': { fill: theme.palette.text.primary, fontSize: 11, fontWeight: 600 },
                        }}>
                            <BarChart
                                height={240}
                                xAxis={[{
                                    scaleType: 'band',
                                    data: leaveByDept.map((d) => d.name),
                                    categoryGapRatio: leaveByDept.length === 1 ? 0.7 : 0.4,
                                    tickLabelStyle: { fontSize: 11 },
                                }]}
                                yAxis={[{
                                    label: 'Days',
                                    tickLabelStyle: { fontSize: 10 },
                                    labelStyle: { fontSize: 11 },
                                }]}
                                series={[{
                                    data: leaveByDept.map((d) => d.days),
                                    label: 'Days taken',
                                    valueFormatter: (v) => v == null ? '' : `${v} ${v === 1 ? 'day' : 'days'}`,
                                }]}
                                colors={deptPalette}
                                borderRadius={6}
                                barLabel={(item) => {
                                    const v = item.value
                                    return v == null || v === 0 ? '' : String(v)
                                }}
                                grid={{ horizontal: true }}
                                margin={{ top: 16, bottom: 36, left: 44, right: 12 }}
                                slotProps={{ legend: { sx: { display: 'none' } } }}
                                sx={{ '& .MuiChartsGrid-line': { stroke: alpha(theme.palette.divider, 0.5), strokeDasharray: '3 3' } }}
                            />
                        </Box>
                    )}
                </Box>

                <Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, mb: '4px' }}>
                        Check-in time per member · last 30 days
                    </Box>
                    {noCheckInData ? (
                        <Box sx={{ fontSize: 12, color: 'text.disabled', textAlign: 'center', py: '40px' }}>
                            Not enough attendance data yet.
                        </Box>
                    ) : (
                        <LineChart
                            height={240}
                            xAxis={[{
                                scaleType: 'point',
                                data: lineSeries.dates,
                                valueFormatter: shortDate,
                            }]}
                            yAxis={[{
                                min: checkInBounds.min,
                                max: checkInBounds.max,
                                valueFormatter: (v: number) => formatMinutes(v),
                                label: 'Check-in',
                            }]}
                            series={lineSeries.series}
                            margin={{ top: 12, bottom: 40, left: 56, right: 12 }}
                        />
                    )}
                </Box>
            </Box>
        </ActionCard>
    )
}

/* ── System Administrator-only ───────────────────────────────────────────────────────── */

function Gauge({ label, big, bigColor, sub, barColor, barPct }: {
    label: string; big: string; bigColor?: string; sub: string; barColor: string; barPct: number
}) {
    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', p: '14px 16px',
        }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 26, fontWeight: 700, color: bigColor ?? 'text.primary', lineHeight: 1, mb: '6px' }}>{big}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mb: '8px' }}>{sub}</Box>
            <Box sx={{ height: 4, bgcolor: 'action.hover', borderRadius: '2px', overflow: 'hidden' }}>
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
            if (isOpenStatus(l.status)) {
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
                <Box sx={{ fontSize: 12, color: 'text.secondary', py: '8px' }}>No attendance data.</Box>
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
                                    <Box sx={{ fontWeight: 600, color: 'text.primary' }}>
                                        {d.name}
                                        <Box component="span" sx={{ color: 'text.disabled', fontWeight: 400, fontSize: 11, ml: '4px' }}>
                                            ({d.total})
                                        </Box>
                                    </Box>
                                    <Box sx={{ display: 'flex', height: 12, bgcolor: 'action.hover', borderRadius: '4px', overflow: 'hidden' }}>
                                        <Box title={`${d.in} working`} sx={{ width: `${inPct}%`, bgcolor: 'success.main' }} />
                                        <Box title={`${d.break} on break`} sx={{ width: `${brkPct}%`, bgcolor: 'warning.main' }} />
                                        <Box title={`${d.leave} on leave`} sx={{ width: `${leavePct}%`, bgcolor: 'primary.main' }} />
                                        <Box title={`${d.out} not in`} sx={{ width: `${outPct}%`, bgcolor: 'divider' }} />
                                    </Box>
                                    <Box sx={{ color: 'text.secondary', textAlign: { xs: 'left', sm: 'right' } }}>
                                        {pending > 0 && <>{pending} pending · </>}
                                        <Box component="strong" sx={{ color: 'text.primary' }}>{Math.round(inPct)}% in</Box>
                                    </Box>
                                </Box>
                            )
                        })}
                    </Box>
                    <Box sx={{ mt: '12px', pt: '10px', borderTop: (theme: Theme) => `1px solid ${theme.palette.divider}`, display: 'flex', gap: '14px', fontSize: 10, color: 'text.secondary', flexWrap: 'wrap' }}>
                        <LegendDot color="success.main" label="Working" />
                        <LegendDot color="warning.main" label="Break" />
                        <LegendDot color="primary.main" label="Leave" />
                        <LegendDot color="divider" label="Not in" />
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
    type Tone = { bg: string | ((theme: Theme) => string); bd: string; head: string; body: string }
    const tones: Record<string, Tone> = {
        danger:  { bg: softBg('error'),   bd: 'error.main',   head: 'error.dark',   body: 'error.dark' },
        warning: { bg: softBg('warning'), bd: 'warning.main', head: 'warning.dark', body: 'warning.dark' },
        info:    { bg: softBg('info'),    bd: 'info.main',    head: 'info.dark',    body: 'info.dark' },
        success: { bg: softBg('success'), bd: 'success.main', head: 'success.dark', body: 'success.dark' },
    }
    return (
        <ActionCard title="Today's issues" icon="⚠️" action={<Box component="span" sx={{ fontSize: 11, color: 'text.disabled' }}>Updated just now</Box>}>
            {issues.length === 0 ? (
                <Box sx={{
                    bgcolor: softBg('success'), borderLeft: '3px solid', borderColor: 'success.main', borderRadius: '6px',
                    p: '10px 12px',
                }}>
                    <Box sx={{ fontSize: 12, fontWeight: 600, color: 'success.dark' }}>✓ All systems quiet</Box>
                    <Box sx={{ fontSize: 11, color: 'success.dark' }}>No active attendance issues.</Box>
                </Box>
            ) : (
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {issues.map((it, i) => {
                        const t = tones[it.severity] ?? tones.info
                        return (
                            <Box key={i} sx={{
                                bgcolor: t.bg, borderLeft: '3px solid', borderColor: t.bd, borderRadius: '6px',
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
                <Box sx={{ fontSize: 12, color: 'text.secondary', textAlign: 'center', py: '12px' }}>
                    No recent activity.
                </Box>
            ) : (
                <Box sx={{ display: 'flex', flexDirection: 'column' }}>
                    {activity.slice(0, 8).map((a, i) => (
                        <Box key={i} sx={{
                            display: 'grid', gridTemplateColumns: '24px 1fr auto', gap: '10px',
                            alignItems: 'center', py: '8px',
                            borderBottom: i === Math.min(activity.length, 8) - 1 ? 'none' : (theme: Theme) => `1px solid ${theme.palette.divider}`,
                        }}>
                            <Box sx={{ fontSize: 14 }}>{activityIcon(a.action)}</Box>
                            <Box sx={{ fontSize: 12, color: 'text.primary' }}>
                                <Box component="strong" sx={{ color: 'text.primary', fontWeight: 600 }}>{a.employeeName}</Box>{' '}
                                {a.action}
                                {/* The server's verdict on a break's end (over only), as Company Attendance shows it. */}
                                {(a.breakVarianceMinutes ?? 0) > 0 && (
                                    <Box component="span" sx={{ color: 'warning.dark', fontWeight: 600 }}> · {describeBreakVariance(a.breakVarianceMinutes)}</Box>
                                )}
                                {a.departmentName && (
                                    <Box component="span" sx={{ color: 'text.disabled', ml: '4px', fontSize: 11 }}>· {a.departmentName}</Box>
                                )}
                            </Box>
                            <Box sx={{ fontSize: 11, color: 'text.disabled', whiteSpace: 'nowrap' }}>
                                {a.minutesAgo != null ? formatMinutesAgo(a.minutesAgo) : '—'}
                            </Box>
                        </Box>
                    ))}
                </Box>
            )}
        </ActionCard>
    )
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
