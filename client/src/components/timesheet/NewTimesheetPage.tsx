import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import MenuItem from '@mui/material/MenuItem'
import Select from '@mui/material/Select'
import Snackbar from '@mui/material/Snackbar'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import ChevronLeftRoundedIcon from '@mui/icons-material/ChevronLeftRounded'
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded'
import CloseRoundedIcon from '@mui/icons-material/CloseRounded'
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded'
import LockRoundedIcon from '@mui/icons-material/LockRounded'
import {
    createTimesheet,
    createTimesheetEntry,
    deleteTimesheetEntry,
    getMyTimesheets,
    getProjects,
    getProjectActivityTypes,
    getTimesheet,
    submitTimesheet,
    updateTimesheetEntry,
} from '../../lib/api'
import { useStore } from '../../lib/mobx'
import { formatElapsed, formatTime12, useAttendanceToday, useLiveElapsedMinutes } from '../../lib/hooks/useAttendance'
import type { Project, ProjectActivityType, UserInfo } from '../../lib/types'
import type { Timesheet, TimesheetStatus } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import { softBg, type SxColor } from '../../lib/theme-tokens'

const BLUE = 'primary.main'
const GREEN = 'success.main'
const AMBER = 'warning.main'

const STATUS_BADGE: Record<TimesheetStatus, { bg: SxColor; color: string; label: string }> = {
    Draft: { bg: softBg('info'), color: 'info.dark', label: 'Draft' },
    Submitted: { bg: softBg('warning'), color: 'warning.dark', label: 'Submitted' },
    Approved: { bg: softBg('success'), color: 'success.dark', label: 'Approved' },
    Rejected: { bg: softBg('error'), color: 'error.dark', label: 'Rejected' },
    Resubmitted: { bg: softBg('primary'), color: 'primary.dark', label: 'Resubmitted' },
}

const DOW = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
const FULL_DOW = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

type Task = {
    _id: string
    serverId?: string
    projectId: string
    activityTypeId: string
    hours: string
    notes: string
}
type DayBucket = { tasks: Task[]; open: boolean }

function startOfWeek(date: Date) {
    const d = new Date(date)
    d.setHours(0, 0, 0, 0)
    const day = d.getDay()
    const diff = day === 0 ? -6 : 1 - day
    d.setDate(d.getDate() + diff)
    return d
}

function isoDate(d: Date) {
    const y = d.getFullYear()
    const m = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    return `${y}-${m}-${day}`
}

function addDays(d: Date, n: number) {
    const c = new Date(d)
    c.setDate(c.getDate() + n)
    return c
}

function newTask(): Task {
    return { _id: Math.random().toString(36).slice(2, 11), projectId: '', activityTypeId: '', hours: '', notes: '' }
}

function newDayBucket(open: boolean): DayBucket {
    return { tasks: [newTask()], open }
}

function isoDateOnly(value: string) {
    return value.split('T')[0]
}

function formatWeekRange(weekStart: Date): string {
    const sunday = addDays(weekStart, 6)
    const sameMonth = weekStart.getMonth() === sunday.getMonth() && weekStart.getFullYear() === sunday.getFullYear()
    if (sameMonth) {
        const tail = sunday.toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })
        return `Week of ${weekStart.getDate()} – ${sunday.getDate()} ${tail}`
    }
    const fmt = (d: Date) => d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    return `Week of ${fmt(weekStart)} – ${fmt(sunday)} ${sunday.getFullYear()}`
}

function buildEmptyBuckets(weekStart: Date, todayIso: string): Record<string, DayBucket> {
    const out: Record<string, DayBucket> = {}
    const keys = Array.from({ length: 7 }, (_, i) => isoDate(addDays(weekStart, i)))
    const weekHasToday = keys.includes(todayIso)
    for (let i = 0; i < 7; i++) {
        const key = keys[i]
        const isFuture = key > todayIso
        const open = isFuture ? false : weekHasToday ? key === todayIso : i === 0
        out[key] = newDayBucket(open)
    }
    return out
}

function buildBucketsFromEntries(
    weekStart: Date,
    todayIso: string,
    entries: TimesheetEntry[],
): Record<string, DayBucket> {
    const out = buildEmptyBuckets(weekStart, todayIso)
    Object.keys(out).forEach((k) => { out[k].tasks = [] })
    for (const entry of entries) {
        const key = isoDateOnly(entry.date)
        if (!out[key]) continue
        out[key].tasks.push({
            _id: entry.id,
            serverId: entry.id,
            projectId: String(entry.projectId),
            activityTypeId: entry.activityTypeId != null ? String(entry.activityTypeId) : '',
            hours: String(entry.hoursWorked),
            notes: entry.notes ?? '',
        })
    }
    for (const k of Object.keys(out)) {
        if (out[k].tasks.length === 0) out[k].tasks.push(newTask())
        else out[k].open = true
    }
    return out
}

export default function NewTimesheetPage({ user: _user }: { user: UserInfo }) {
    const queryClient = useQueryClient()
    const { uiStore } = useStore()

    const todayIso = useMemo(() => {
        const d = new Date()
        d.setHours(0, 0, 0, 0)
        return isoDate(d)
    }, [])

    const [weekStart, setWeekStart] = useState<Date>(() => {
        const pending = uiStore.consumePendingWeekStart()
        if (pending) {
            const parsed = new Date(pending)
            if (!isNaN(parsed.getTime())) return startOfWeek(parsed)
        }
        return startOfWeek(new Date())
    })
    const periodStartIso = isoDate(weekStart)
    const periodEndIso = isoDate(addDays(weekStart, 6))

    const dayDates = useMemo(
        () => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)),
        [weekStart],
    )

    const { data: myTimesheets = [] } = useQuery({
        queryKey: ['timesheets', 'mine'],
        queryFn: getMyTimesheets,
    })

    const currentTs: Timesheet | undefined = useMemo(() => {
        const matches = myTimesheets.filter((t) => isoDateOnly(t.periodStart) === periodStartIso)
        if (matches.length === 0) return undefined
        const draft = matches.find((t) => t.status === 'Draft' || t.status === 'Rejected')
        return draft ?? matches.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())[0]
    }, [myTimesheets, periodStartIso])

    const { data: tsDetail, isLoading: isLoadingDetail } = useQuery({
        queryKey: ['timesheet', currentTs?.id],
        queryFn: () => getTimesheet(currentTs!.id),
        enabled: !!currentTs?.id,
    })

    const [buckets, setBuckets] = useState<Record<string, DayBucket>>(() =>
        buildEmptyBuckets(startOfWeek(new Date()), todayIso),
    )

    const lastLoadedKeyRef = useRef<string>('')

    useEffect(() => {
        const cacheKey = currentTs
            ? `ts:${currentTs.id}:${tsDetail ? 'loaded' : 'pending'}:${(tsDetail?.entries?.length ?? 0)}`
            : `empty:${periodStartIso}`

        if (cacheKey === lastLoadedKeyRef.current) return

        if (currentTs && tsDetail && tsDetail.id === currentTs.id) {
            const entries = (tsDetail.entries ?? []) as TimesheetEntry[]
            setBuckets(buildBucketsFromEntries(weekStart, todayIso, entries))
            lastLoadedKeyRef.current = cacheKey
        } else if (!currentTs) {
            setBuckets(buildEmptyBuckets(weekStart, todayIso))
            lastLoadedKeyRef.current = cacheKey
        }
    }, [currentTs, tsDetail, weekStart, todayIso, periodStartIso])

    const [error, setError] = useState('')
    const [pendingMode, setPendingMode] = useState<'draft' | 'submit' | null>(null)
    const [savedSnack, setSavedSnack] = useState(false)

    const { data: projects = [] } = useQuery({ queryKey: ['projects'], queryFn: getProjects })
    const activeProjects = projects.filter((p) => p.isActive)
    const { data: activityTypes = [] } = useQuery({ queryKey: ['projectActivityTypes'], queryFn: getProjectActivityTypes })
    const activeActivityTypes = activityTypes.filter((a) => a.isActive)

    const { data: attendanceToday } = useAttendanceToday()
    const attendanceMinutes = useLiveElapsedMinutes(attendanceToday)
    const attendanceActive = attendanceToday && (attendanceToday.status === 'in' || attendanceToday.status === 'break' || attendanceToday.status === 'done')

    const autoFillToday = () => {
        if (!attendanceToday || !attendanceActive) return
        const key = todayIso
        const bucket = buckets[key]
        if (!bucket) return
        const hours = (attendanceMinutes / 60).toFixed(1)
        setBuckets((b) => {
            const tasks = b[key].tasks
            const targetIdx = tasks.findIndex((t) => !t.hours.trim())
            if (targetIdx === -1) {
                return { ...b, [key]: { ...b[key], tasks: [...tasks, { _id: Math.random().toString(36).slice(2, 11), projectId: '', activityTypeId: '', hours, notes: '' }], open: true } }
            }
            const updated = tasks.map((t, i) => (i === targetIdx ? { ...t, hours } : t))
            return { ...b, [key]: { ...b[key], tasks: updated, open: true } }
        })
    }

    const status: TimesheetStatus = currentTs?.status ?? 'Draft'
    const isEditable = status === 'Draft' || status === 'Rejected' || !currentTs
    const badge = STATUS_BADGE[status]

    const weekTotal = dayDates.reduce((sum, d) => {
        const bucket = buckets[isoDate(d)]
        if (!bucket) return sum
        return sum + bucket.tasks.reduce((s, t) => {
            const h = parseFloat(t.hours)
            return s + (isNaN(h) ? 0 : h)
        }, 0)
    }, 0)

    const daysLogged = dayDates.slice(0, 5).filter((d) => {
        const bucket = buckets[isoDate(d)]
        if (!bucket) return false
        return bucket.tasks.some((t) => {
            const h = parseFloat(t.hours)
            return !isNaN(h) && h > 0
        })
    }).length

    const remaining = Math.max(0, 40 - weekTotal)
    const progressPct = Math.min(100, (weekTotal / 40) * 100)

    const toggleDay = (key: string) =>
        setBuckets((b) => ({ ...b, [key]: { ...b[key], open: !b[key].open } }))

    const addTask = (key: string) =>
        setBuckets((b) => ({ ...b, [key]: { ...b[key], tasks: [...b[key].tasks, newTask()] } }))

    const removeTask = (key: string, taskId: string) =>
        setBuckets((b) => {
            const tasks = b[key].tasks
            if (tasks.length === 1) {
                return { ...b, [key]: { ...b[key], tasks: [newTask()] } }
            }
            return { ...b, [key]: { ...b[key], tasks: tasks.filter((t) => t._id !== taskId) } }
        })

    const updateTask = (key: string, taskId: string, field: keyof Omit<Task, '_id' | 'serverId'>, value: string) =>
        setBuckets((b) => ({
            ...b,
            [key]: {
                ...b[key],
                tasks: b[key].tasks.map((t) => (t._id === taskId ? { ...t, [field]: value } : t)),
            },
        }))

    const validatePartial = (): string | null => {
        for (const d of dayDates) {
            const key = isoDate(d)
            const bucket = buckets[key]
            if (!bucket) continue
            for (const t of bucket.tasks) {
                const hasProject = t.projectId !== ''
                const hasHours = t.hours.trim() !== ''
                if (hasProject && !hasHours) return 'Some tasks have a project but no hours. Please complete or remove them.'
                if (!hasProject && hasHours) return 'Some tasks have hours but no project. Please complete or remove them.'
                if (hasHours) {
                    const h = parseFloat(t.hours)
                    if (isNaN(h) || h < 0.5 || h > 24) return 'Hours must be between 0.5 and 24 for each task.'
                }
            }
        }
        return null
    }

    const collectValidTasks = (): { task: Task; date: string }[] => {
        const out: { task: Task; date: string }[] = []
        for (const d of dayDates) {
            const key = isoDate(d)
            const bucket = buckets[key]
            if (!bucket) continue
            for (const t of bucket.tasks) {
                if (!t.projectId) continue
                const h = parseFloat(t.hours)
                if (isNaN(h) || h <= 0) continue
                out.push({ task: t, date: key })
            }
        }
        return out
    }

    const handleSave = async (mode: 'draft' | 'submit') => {
        if (!isEditable) return
        const partialErr = validatePartial()
        if (partialErr) { setError(partialErr); return }

        const valid = collectValidTasks()
        if (mode === 'submit' && valid.length === 0) {
            setError('Add at least one task before submitting.')
            return
        }

        setError('')
        setPendingMode(mode)
        try {
            let ts: Timesheet | undefined = currentTs
            if (!ts) {
                ts = await createTimesheet({ periodStart: periodStartIso, periodEnd: periodEndIso })
            }
            const tsId = ts.id

            const existingEntries = ((tsDetail?.entries ?? []) as TimesheetEntry[])
                .filter((e) => e.timesheetId === tsId)

            const uiServerIds = new Set(
                valid.map((x) => x.task.serverId).filter((id): id is string => !!id),
            )
            const toDelete = existingEntries.filter((e) => !uiServerIds.has(e.id))
            const toCreate = valid.filter((x) => !x.task.serverId)
            const toUpdate = valid.filter((x) => !!x.task.serverId)

            for (const e of toDelete) {
                await deleteTimesheetEntry(tsId, e.id)
            }
            const createdEntries: TimesheetEntry[] = []
            for (const x of toCreate) {
                const created = await createTimesheetEntry(tsId, {
                    timesheetId: tsId,
                    projectId: Number(x.task.projectId),
                    activityTypeId: x.task.activityTypeId ? Number(x.task.activityTypeId) : null,
                    date: x.date,
                    hoursWorked: parseFloat(x.task.hours),
                    notes: x.task.notes.trim() || null,
                })
                createdEntries.push(created)
            }
            for (const x of toUpdate) {
                const existing = existingEntries.find((e) => e.id === x.task.serverId)
                if (!existing) continue
                const same =
                    String(existing.projectId) === x.task.projectId
                    && String(existing.activityTypeId ?? '') === x.task.activityTypeId
                    && Math.abs(Number(existing.hoursWorked) - parseFloat(x.task.hours)) < 0.001
                    && (existing.notes ?? '') === x.task.notes.trim()
                    && isoDateOnly(existing.date) === x.date
                if (same) continue
                await updateTimesheetEntry(tsId, x.task.serverId!, {
                    id: x.task.serverId!,
                    timesheetId: tsId,
                    projectId: Number(x.task.projectId),
                    activityTypeId: x.task.activityTypeId ? Number(x.task.activityTypeId) : null,
                    date: x.date,
                    hoursWorked: parseFloat(x.task.hours),
                    notes: x.task.notes.trim() || null,
                })
            }

            if (mode === 'submit') {
                await submitTimesheet(tsId)
            }

            await Promise.all([
                queryClient.invalidateQueries({ queryKey: ['timesheets'] }),
                queryClient.invalidateQueries({ queryKey: ['timesheets', 'mine'] }),
                queryClient.invalidateQueries({ queryKey: ['timesheet', tsId] }),
            ])

            // Reset cache key so the next effect run repopulates from fresh entries.
            lastLoadedKeyRef.current = ''

            if (mode === 'submit') {
                uiStore.navigateToTimesheets()
            } else {
                setSavedSnack(true)
            }
        } catch {
            setError('Failed to save timesheet. Please try again.')
        } finally {
            setPendingMode(null)
        }
    }

    const isBusy = pendingMode !== null
    const showLoading = !!currentTs?.id && isLoadingDetail && !tsDetail

    return (
        <Box sx={{ maxWidth: 960 }}>
            {/* Week navigation bar */}
            <Box sx={{
                bgcolor: 'background.paper',
                border: '1px solid', borderColor: 'divider',
                borderRadius: '10px',
                p: '14px 18px',
                mb: 1.75,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1.5,
                flexWrap: 'wrap',
            }}>
                <Stack direction="row" alignItems="center" spacing={1.25}>
                    <NavBtn onClick={() => setWeekStart(addDays(weekStart, -7))} title="Previous week" disabled={isBusy}>
                        <ChevronLeftRoundedIcon sx={{ fontSize: 18 }} />
                    </NavBtn>
                    <Box>
                        <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.primary' }}>
                            {formatWeekRange(weekStart)}
                        </Typography>
                        <Typography sx={{ fontSize: 11, color: 'text.secondary', mt: '1px' }}>
                            Monday – Friday · Submit by Friday 6pm
                        </Typography>
                    </Box>
                    <NavBtn onClick={() => setWeekStart(addDays(weekStart, 7))} title="Next week" disabled={isBusy}>
                        <ChevronRightRoundedIcon sx={{ fontSize: 18 }} />
                    </NavBtn>
                </Stack>

                <Stack direction="row" alignItems="center" spacing={1.75}>
                    <Stack direction="row" alignItems="center" spacing={1.25}>
                        <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                            <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700, fontSize: 14 }}>
                                {weekTotal.toFixed(1)}
                            </Box>
                            {' '}/ 40 hrs
                        </Typography>
                        <Box sx={{ width: 100, height: 6, bgcolor: 'divider', borderRadius: 3, overflow: 'hidden' }}>
                            <Box sx={{
                                height: '100%',
                                width: `${progressPct}%`,
                                bgcolor: GREEN,
                                borderRadius: 3,
                                transition: 'width 0.25s',
                            }} />
                        </Box>
                    </Stack>
                    <Box sx={{
                        display: 'inline-flex', alignItems: 'center',
                        px: 1.1, py: '3px',
                        borderRadius: 20,
                        fontSize: 11, fontWeight: 500,
                        bgcolor: badge.bg, color: badge.color,
                    }}>
                        {badge.label}
                    </Box>
                </Stack>
            </Box>

            {showLoading ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                    <CircularProgress size={24} />
                </Box>
            ) : (
                <Stack spacing={1.25} sx={{ mb: 1.75 }}>
                    {dayDates.map((d, i) => {
                        const key = isoDate(d)
                        const bucket = buckets[key]
                        if (!bucket) return null
                        const isWeekend = i >= 5
                        const isToday = key === todayIso
                        const isFuture = key > todayIso
                        const dayTotal = bucket.tasks.reduce((s, t) => {
                            const h = parseFloat(t.hours)
                            return s + (isNaN(h) ? 0 : h)
                        }, 0)
                        const taskCount = bucket.tasks.filter((t) => {
                            const h = parseFloat(t.hours)
                            return !isNaN(h) && h > 0
                        }).length

                        const showBanner = isToday && attendanceActive && isEditable && attendanceToday?.checkInAt
                        const bannerSuffix = attendanceToday?.status === 'done'
                            ? `${formatElapsed(attendanceMinutes)} worked today`
                            : `${formatElapsed(attendanceMinutes)} so far`
                        return (
                            <DayCard
                                key={key}
                                dow={DOW[i]}
                                dom={d.getDate()}
                                dayName={FULL_DOW[i]}
                                isToday={isToday}
                                isWeekend={isWeekend}
                                isFuture={isFuture}
                                isOpen={bucket.open && !isFuture}
                                tasks={bucket.tasks}
                                dayTotal={dayTotal}
                                taskCount={taskCount}
                                activeProjects={activeProjects}
                                activeActivityTypes={activeActivityTypes}
                                onToggle={() => { if (!isFuture) toggleDay(key) }}
                                onAddTask={() => addTask(key)}
                                onRemoveTask={(taskId) => removeTask(key, taskId)}
                                onUpdateTask={(taskId, field, value) => updateTask(key, taskId, field, value)}
                                disabled={isBusy || !isEditable || isFuture}
                                readOnly={!isEditable || isFuture}
                                banner={showBanner ? (
                                    <AutoFillBanner
                                        text={`Hours from your check-in at ${formatTime12(attendanceToday!.checkInAt!)} — ${bannerSuffix}.`}
                                        onAutoFill={autoFillToday}
                                    />
                                ) : undefined}
                            />
                        )
                    })}
                </Stack>
            )}

            {/* Footer */}
            <Box sx={{
                bgcolor: 'background.paper',
                border: '1px solid', borderColor: 'divider',
                borderRadius: '10px',
                p: '16px 20px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1.75,
                flexWrap: 'wrap',
            }}>
                <Stack direction="row" spacing={3.25} flexWrap="wrap">
                    <FooterStat label="Total this week" value={`${weekTotal.toFixed(1)} hrs`} />
                    <FooterStat label="Days logged" value={`${daysLogged} / 5`} valueColor={'text.secondary'} />
                    <FooterStat
                        label="Remaining"
                        value={`${remaining.toFixed(1)} hrs`}
                        valueColor={remaining > 0 ? AMBER : 'text.primary'}
                    />
                    <FooterStat label="Target" value="40 hrs" valueColor={'text.secondary'} />
                </Stack>
                {isEditable && (
                    <Stack direction="row" spacing={1}>
                        <Button
                            variant="outlined"
                            onClick={() => void handleSave('draft')}
                            disabled={isBusy}
                            startIcon={pendingMode === 'draft' ? <CircularProgress size={14} color="inherit" /> : null}
                            sx={{
                                textTransform: 'none',
                                fontSize: 13,
                                borderColor: 'divider',
                                color: 'text.secondary',
                                bgcolor: 'transparent',
                                '&:hover': { bgcolor: 'action.hover', borderColor: 'divider' },
                            }}
                        >
                            {pendingMode === 'draft' ? 'Saving…' : 'Save draft'}
                        </Button>
                        <Button
                            variant="contained"
                            onClick={() => void handleSave('submit')}
                            disabled={isBusy}
                            startIcon={pendingMode === 'submit' ? <CircularProgress size={14} color="inherit" /> : null}
                            sx={{
                                textTransform: 'none',
                                fontSize: 13,
                                bgcolor: BLUE,
                                '&:hover': { bgcolor: 'primary.dark' },
                                boxShadow: 'none',
                            }}
                        >
                            {pendingMode === 'submit'
                                ? 'Submitting…'
                                : status === 'Rejected'
                                    ? 'Resubmit'
                                    : 'Submit for approval'}
                        </Button>
                    </Stack>
                )}
            </Box>

            {error && <Alert severity="error" sx={{ mt: 1.5, fontSize: 13 }}>{error}</Alert>}

            <Stack direction="row" alignItems="center" spacing={0.75} sx={{ mt: 1.25, px: 0.5 }}>
                <Typography sx={{ fontSize: 14, lineHeight: 1 }}>💡</Typography>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    Click a day to expand it. Add tasks as you go — saving updates this week's timesheet without creating a new one.
                </Typography>
            </Stack>

            <Snackbar
                open={savedSnack}
                autoHideDuration={2200}
                onClose={() => setSavedSnack(false)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            >
                <Alert severity="success" variant="filled" onClose={() => setSavedSnack(false)} sx={{ fontSize: 13 }}>
                    Draft saved
                </Alert>
            </Snackbar>
        </Box>
    )
}

function NavBtn({
    onClick,
    title,
    disabled,
    children,
}: {
    onClick: () => void
    title: string
    disabled?: boolean
    children: React.ReactNode
}) {
    return (
        <Tooltip title={title}>
            <Box
                component="button"
                onClick={onClick}
                disabled={disabled}
                sx={{
                    width: 30, height: 30,
                    border: '1px solid', borderColor: 'divider',
                    borderRadius: '6px',
                    bgcolor: 'background.paper',
                    cursor: disabled ? 'not-allowed' : 'pointer',
                    color: 'text.primary',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    p: 0,
                    opacity: disabled ? 0.55 : 1,
                    '&:hover': { bgcolor: disabled ? '#fff' : 'action.hover' },
                }}
            >
                {children}
            </Box>
        </Tooltip>
    )
}

function FooterStat({
    label,
    value,
    valueColor,
}: {
    label: string
    value: string
    valueColor?: string
}) {
    return (
        <Box>
            <Typography sx={{
                fontSize: 11, color: 'text.secondary',
                textTransform: 'uppercase', letterSpacing: '0.05em', mb: '2px',
            }}>
                {label}
            </Typography>
            <Typography sx={{ fontSize: 20, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>
                {value}
            </Typography>
        </Box>
    )
}

type DayCardProps = {
    dow: string
    dom: number
    dayName: string
    isToday: boolean
    isWeekend: boolean
    isFuture: boolean
    isOpen: boolean
    tasks: Task[]
    dayTotal: number
    taskCount: number
    activeProjects: Project[]
    activeActivityTypes: ProjectActivityType[]
    onToggle: () => void
    onAddTask: () => void
    onRemoveTask: (taskId: string) => void
    onUpdateTask: (taskId: string, field: keyof Omit<Task, '_id' | 'serverId'>, value: string) => void
    disabled: boolean
    readOnly: boolean
    banner?: React.ReactNode
}

function AutoFillBanner({ text, onAutoFill }: { text: string; onAutoFill: () => void }) {
    return (
        <Box sx={{
            bgcolor: softBg('info'),
            border: '1px solid',
            borderColor: 'info.main',
            borderRadius: '6px',
            p: '8px 12px',
            mb: 1.25,
            display: 'flex', alignItems: 'center',
            gap: 1,
            fontSize: 12, color: 'info.dark',
        }}>
            <Box sx={{
                bgcolor: softBg('info'), color: 'info.dark',
                px: '7px', py: '2px',
                borderRadius: 10,
                fontSize: 10, fontWeight: 600,
                letterSpacing: '0.04em',
            }}>
                AUTO
            </Box>
            <Box sx={{ flex: 1 }}>{text}</Box>
            <Button
                onClick={onAutoFill}
                size="small"
                sx={{
                    textTransform: 'none',
                    fontSize: 11,
                    fontWeight: 600,
                    color: 'info.dark',
                    bgcolor: 'background.paper',
                    border: '1px solid',
                    borderColor: 'info.main',
                    px: 1.25, py: 0.5,
                    minWidth: 'unset',
                    '&:hover': { bgcolor: softBg('info'), borderColor: 'info.main' },
                }}
            >
                Auto-fill
            </Button>
        </Box>
    )
}

function DayCard({
    dow, dom, dayName, isToday, isWeekend, isFuture, isOpen,
    tasks, dayTotal, taskCount,
    activeProjects,
    activeActivityTypes,
    onToggle, onAddTask, onRemoveTask, onUpdateTask,
    disabled, readOnly, banner,
}: DayCardProps) {
    const hasEntries = dayTotal > 0
    const isEmptyWeekday = !isWeekend && !isFuture && dayTotal === 0

    const leftAccent = hasEntries ? GREEN : isEmptyWeekday ? AMBER : null
    const metaText = isFuture
        ? 'Locked · future date'
        : isWeekend
            ? 'Weekend · optional'
            : taskCount === 0 ? 'Nothing logged yet'
                : taskCount === 1 ? '1 task' : `${taskCount} tasks`
    const metaColor = isEmptyWeekday ? 'warning.dark' : 'text.secondary'

    return (
        <Box sx={{
            bgcolor: isFuture || isWeekend ? 'action.hover' : 'background.paper',
            border: '1px solid',
            borderColor: isToday ? 'primary.main' : 'divider',
            borderLeftWidth: leftAccent ? 3 : 1,
            borderLeftColor: leftAccent ?? (isToday ? 'primary.main' : 'divider'),
            borderRadius: '10px',
            overflow: 'hidden',
            transition: 'border-color 0.15s',
        }}>
            <Box
                onClick={isFuture ? undefined : onToggle}
                sx={{
                    p: '12px 16px',
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    cursor: isFuture ? 'not-allowed' : 'pointer',
                    userSelect: 'none',
                    '&:hover': isFuture ? {} : { bgcolor: 'action.hover' },
                }}
            >
                <Stack direction="row" alignItems="center" spacing={1.75}>
                    <Box sx={{
                        width: 44, height: 44, borderRadius: '8px',
                        bgcolor: isToday ? 'primary.main' : 'action.hover',
                        opacity: (isWeekend || isFuture) && !isToday ? 0.7 : 1,
                        display: 'flex', flexDirection: 'column',
                        alignItems: 'center', justifyContent: 'center',
                        flexShrink: 0,
                    }}>
                        <Typography sx={{
                            fontSize: 10, fontWeight: 600,
                            color: isToday ? '#fff' : 'text.secondary',
                            textTransform: 'uppercase', letterSpacing: '0.05em',
                            lineHeight: 1,
                        }}>
                            {dow}
                        </Typography>
                        <Typography sx={{
                            fontSize: 16, fontWeight: 700,
                            color: isToday ? '#fff' : 'text.primary',
                            lineHeight: 1.1, mt: '2px',
                        }}>
                            {dom}
                        </Typography>
                    </Box>
                    <Box>
                        <Stack direction="row" alignItems="center" spacing={0.75}>
                            <Typography sx={{
                                fontSize: 13, fontWeight: 600,
                                color: (isWeekend || isFuture) ? 'text.disabled' : 'text.primary',
                            }}>
                                {dayName}
                            </Typography>
                            {isToday && (
                                <Box sx={{
                                    bgcolor: softBg('info'), color: 'info.dark',
                                    fontSize: 10, fontWeight: 500,
                                    px: 0.75, py: '1px',
                                    borderRadius: 10,
                                }}>
                                    Today
                                </Box>
                            )}
                        </Stack>
                        <Typography sx={{ fontSize: 11, color: metaColor, mt: '2px' }}>
                            {metaText}
                        </Typography>
                    </Box>
                </Stack>
                <Stack direction="row" alignItems="center" spacing={1.5}>
                    <Typography sx={{
                        fontSize: 15,
                        fontWeight: dayTotal === 0 ? 500 : 700,
                        color: dayTotal === 0 ? 'text.disabled' : 'text.primary',
                    }}>
                        {dayTotal.toFixed(1)} hrs
                    </Typography>
                    {isFuture ? (
                        <LockRoundedIcon sx={{ fontSize: 16, color: 'text.disabled' }} />
                    ) : (
                        <ExpandMoreRoundedIcon sx={{
                            fontSize: 18,
                            color: 'text.disabled',
                            transition: 'transform 0.2s',
                            transform: isOpen ? 'rotate(180deg)' : 'rotate(0deg)',
                        }} />
                    )}
                </Stack>
            </Box>

            {isOpen && (
                <Box sx={{ px: 2, pb: 1.75, borderTop: '1px solid', borderTopColor: 'divider' }}>
                    {banner && <Box sx={{ pt: 1.25 }}>{banner}</Box>}
                    <Box sx={{
                        display: 'grid',
                        gridTemplateColumns: '1.6fr 1.4fr 70px 1.7fr 30px',
                        gap: 1.25,
                        p: '12px 4px 6px',
                    }}>
                        {['Project', 'Activity', 'Hours', 'What you worked on', ''].map((h, i) => (
                            <Typography key={i} sx={{
                                fontSize: 10, fontWeight: 600,
                                color: 'text.disabled',
                                textTransform: 'uppercase', letterSpacing: '0.05em',
                            }}>
                                {h}
                            </Typography>
                        ))}
                    </Box>

                    <Stack spacing={1}>
                        {tasks.map((t) => (
                            <Box key={t._id} sx={{
                                display: 'grid',
                                gridTemplateColumns: '1.6fr 1.4fr 70px 1.7fr 30px',
                                gap: 1.25,
                                alignItems: 'center',
                            }}>
                                <Select
                                    size="small"
                                    displayEmpty
                                    value={t.projectId}
                                    onChange={(e) => onUpdateTask(t._id, 'projectId', e.target.value)}
                                    disabled={disabled}
                                    sx={{
                                        fontSize: 12,
                                        '& .MuiSelect-select': { py: '7px', px: '10px' },
                                        '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                                    }}
                                >
                                    <MenuItem value="" disabled>
                                        <Box component="em" sx={{ color: 'text.disabled' }}>Select project…</Box>
                                    </MenuItem>
                                    {activeProjects.map((p) => (
                                        <MenuItem key={p.id} value={String(p.id)}>
                                            {p.code ? `${p.code} — ${p.name}` : p.name}
                                        </MenuItem>
                                    ))}
                                </Select>
                                <Select
                                    size="small"
                                    displayEmpty
                                    value={t.activityTypeId}
                                    onChange={(e) => onUpdateTask(t._id, 'activityTypeId', e.target.value)}
                                    disabled={disabled}
                                    sx={{
                                        fontSize: 12,
                                        '& .MuiSelect-select': { py: '7px', px: '10px' },
                                        '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                                    }}
                                >
                                    <MenuItem value="">
                                        <Box component="em" sx={{ color: 'text.disabled' }}>Activity…</Box>
                                    </MenuItem>
                                    {activeActivityTypes.map((a) => (
                                        <MenuItem key={a.id} value={String(a.id)}>
                                            {a.icon ? `${a.icon} ${a.name}` : a.name}
                                        </MenuItem>
                                    ))}
                                </Select>
                                <TextField
                                    type="number"
                                    size="small"
                                    value={t.hours}
                                    onChange={(e) => onUpdateTask(t._id, 'hours', e.target.value)}
                                    placeholder="0"
                                    inputProps={{
                                        step: 0.5, min: 0, max: 24,
                                        style: { textAlign: 'center', fontWeight: 600 },
                                    }}
                                    disabled={disabled}
                                    sx={{
                                        '& .MuiInputBase-input': { fontSize: 12, py: '7px' },
                                        '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                                    }}
                                />
                                <TextField
                                    size="small"
                                    value={t.notes}
                                    onChange={(e) => onUpdateTask(t._id, 'notes', e.target.value)}
                                    placeholder="What did you work on?"
                                    disabled={disabled}
                                    sx={{
                                        '& .MuiInputBase-input': { fontSize: 12, py: '7px' },
                                        '& fieldset': { borderColor: 'divider', borderRadius: '6px' },
                                    }}
                                />
                                <Box
                                    component="button"
                                    onClick={() => onRemoveTask(t._id)}
                                    disabled={disabled}
                                    title="Remove"
                                    sx={{
                                        width: 28, height: 28,
                                        borderRadius: '5px',
                                        bgcolor: 'transparent',
                                        border: '1px solid', borderColor: 'divider',
                                        color: 'text.disabled',
                                        cursor: disabled ? 'not-allowed' : 'pointer',
                                        fontSize: 11,
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        p: 0,
                                        '&:hover': disabled ? {} : {
                                            bgcolor: softBg('error'),
                                            color: 'error.dark',
                                            borderColor: 'error.main',
                                        },
                                    }}
                                >
                                    <CloseRoundedIcon sx={{ fontSize: 14 }} />
                                </Box>
                            </Box>
                        ))}
                    </Stack>

                    {!readOnly && (
                        <Button
                            onClick={onAddTask}
                            disabled={disabled}
                            fullWidth
                            sx={{
                                mt: 1.25,
                                px: 1.5, py: '7px',
                                fontSize: 12,
                                textTransform: 'none',
                                color: BLUE,
                                bgcolor: 'transparent',
                                border: '1px dashed',
                                borderColor: 'primary.main',
                                borderRadius: '6px',
                                '&:hover': {
                                    bgcolor: softBg('primary'),
                                    borderStyle: 'solid',
                                    borderColor: 'primary.main',
                                },
                            }}
                        >
                            + Add task
                        </Button>
                    )}
                </Box>
            )}
        </Box>
    )
}
