import { useMemo, useState } from 'react'
import { observer } from 'mobx-react-lite'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import { deleteWorkTask, getWorkTaskDepartments, getWorkTasks, updateWorkTaskStatus } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from '../../lib/types'
import {
    PRIORITY_LABELS, STATUS_LABELS, filterTasks, isOpenTask, nextStatusAction, openCount, overdueDays, taskStats, todayIso,
    type StatusFilter, type TaskTab,
} from '../../lib/work-tasks'
import { SweetAlert } from '../ui'
import { CardStat, OutlineBtn, SectionLabel, SelectFilter, StatCard } from '../ui/CardKit'
import { CODE_COLORS, avatarBg, initials } from '../../lib/card-kit'
import TaskDialog from './TaskDialog'
import { PRIORITY_COLORS, STATUS_COLORS } from './statusStyles'

/* ─── tokens ─────────────────────────────────────────────────────────────── */

const VIEWS: { value: TaskTab; label: string; empty: string }[] = [
    { value: 'assigned', label: 'Assigned to me', empty: 'Nothing assigned to you.' },
    { value: 'created', label: 'Created by me', empty: "You haven't created any tasks." },
    { value: 'all', label: 'All in my departments', empty: 'No tasks in your departments.' },
]

/* ─── helpers ────────────────────────────────────────────────────────────── */

function formatDate(iso: string) {
    const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

function plural(n: number, one: string, many = `${one}s`) {
    return `${n} ${n === 1 ? one : many}`
}

/* ════════════════════════════════════════════════════════════════════════ */

const TasksPage = observer(function TasksPage() {
    const { authStore } = useStore()
    const userId = authStore.user?.id ?? ''
    const queryClient = useQueryClient()

    const [view, setView] = useState<TaskTab>('assigned')
    const [status, setStatus] = useState<StatusFilter>('open')
    const [departmentId, setDepartmentId] = useState<number | null>(null)
    const [priority, setPriority] = useState<WorkTaskPriority | 'any'>('any')
    const [search, setSearch] = useState('')
    const [dialogTask, setDialogTask] = useState<WorkTask | null | undefined>(undefined) // undefined = closed

    const tasks = useQuery({ queryKey: ['work-tasks'], queryFn: getWorkTasks })
    const departments = useQuery({ queryKey: ['work-tasks', 'departments'], queryFn: getWorkTaskDepartments })

    const refresh = () => queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
    const moveStatus = useMutation({
        mutationFn: ({ id, next }: { id: number; next: WorkTaskStatus }) => updateWorkTaskStatus(id, next),
        onSuccess: refresh,
    })
    const remove = useMutation({ mutationFn: (id: number) => deleteWorkTask(id), onSuccess: refresh })

    const today = todayIso()
    const all = useMemo(() => tasks.data ?? [], [tasks.data])
    const stats = useMemo(() => taskStats(all, userId, today), [all, userId, today])
    const visible = useMemo(
        () => filterTasks(all, { tab: view, status, departmentId, userId, priority, search }),
        [all, view, status, departmentId, userId, priority, search],
    )
    const mutationError = moveStatus.error ?? remove.error

    const confirmDelete = async (task: WorkTask) => {
        const result = await SweetAlert.fire({
            title: `Delete "${task.title}"?`,
            text: 'The task and its assignees are removed for everyone.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, delete',
            cancelButtonText: 'Cancel',
            confirmButtonColor: '#EF4444',
            reverseButtons: true,
        })
        if (result.isConfirmed) remove.mutate(task.id)
    }

    if (tasks.isLoading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress size={28} /></Box>
    }
    if (tasks.isError) {
        return <Box sx={{ p: 2 }}><Alert severity="error">{getApiErrorMessage(tasks.error, 'Tasks could not be loaded.')}</Alert></Box>
    }

    return (
        <Box sx={{ p: { xs: 2, md: 3 } }}>
            {mutationError && (
                <Alert severity="error" sx={{ mb: 2 }}>{getApiErrorMessage(mutationError, 'The task could not be updated.')}</Alert>
            )}

            {/* Stats row */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' },
                gap: '12px', mb: '14px',
            }}>
                <Box data-testid="stat-open">
                    <StatCard
                        label="📋 Open Tasks"
                        value={String(stats.open)}
                        sub={`of ${stats.total} total · ${stats.inProgress} in progress`}
                    />
                </Box>
                <Box data-testid="stat-overdue">
                    <StatCard
                        label="⏰ Overdue"
                        value={String(stats.overdue)}
                        valueColor={stats.overdue > 0 ? 'warning.main' : 'success.main'}
                        sub={stats.overdue === 0 ? 'all on schedule' : `${stats.overdue === 1 ? 'task' : 'tasks'} past the due date`}
                    />
                </Box>
                <Box data-testid="stat-done">
                    <StatCard
                        label="✅ Done This Month"
                        value={String(stats.doneThisMonth)}
                        valueColor="success.main"
                        sub="completed since the 1st"
                    />
                </Box>
                <Box data-testid="stat-mine">
                    <StatCard
                        label="👤 Assigned To Me"
                        value={String(stats.assignedToMeOpen)}
                        valueColor="primary.main"
                        sub="open tasks on your plate"
                    />
                </Box>
            </Box>

            {/* Toolbar */}
            <Box sx={{
                bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                p: '10px 12px', display: 'flex', gap: '10px', flexWrap: 'wrap',
                alignItems: 'center', mb: '14px',
            }}>
                <Box sx={{ flex: 1, minWidth: 200, maxWidth: 320 }}>
                    <Box
                        component="input"
                        type="search"
                        placeholder="Search tasks…"
                        value={search}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearch(e.target.value)}
                        sx={{
                            width: '100%', p: '7px 10px', fontSize: 13, fontFamily: 'inherit',
                            border: '1px solid', borderColor: 'divider', borderRadius: '6px', outline: 'none',
                            bgcolor: 'background.paper', color: 'text.primary',
                            '&::placeholder': { color: 'text.disabled' },
                            '&:focus': { borderColor: 'primary.main' },
                        }}
                    />
                </Box>
                <SelectFilter
                    ariaLabel="View"
                    value={view}
                    onChange={(v) => setView(v as TaskTab)}
                    options={VIEWS.map((v) => ({ value: v.value, label: `${v.label} (${openCount(all, v.value, userId)})` }))}
                />
                <SelectFilter
                    ariaLabel="Status filter"
                    value={status}
                    onChange={(v) => setStatus(v as StatusFilter)}
                    options={[
                        { value: 'open', label: 'Open' },
                        ...(Object.keys(STATUS_LABELS) as WorkTaskStatus[]).map((s) => ({ value: s, label: STATUS_LABELS[s] })),
                        { value: 'any', label: 'Everything' },
                    ]}
                />
                {(departments.data?.length ?? 0) > 1 && (
                    <SelectFilter
                        ariaLabel="Department filter"
                        value={departmentId == null ? 'all' : String(departmentId)}
                        onChange={(v) => setDepartmentId(v === 'all' ? null : Number(v))}
                        options={[
                            { value: 'all', label: 'All departments' },
                            ...departments.data!.map((d) => ({ value: String(d.id), label: d.name })),
                        ]}
                    />
                )}
                <SelectFilter
                    ariaLabel="Priority filter"
                    value={priority}
                    onChange={(v) => setPriority(v as WorkTaskPriority | 'any')}
                    options={[
                        { value: 'any', label: 'Any priority' },
                        ...(['High', 'Normal', 'Low'] as WorkTaskPriority[]).map((p) => ({ value: p, label: PRIORITY_LABELS[p] })),
                    ]}
                />
                <Box sx={{ flex: 1 }} />
                <Box
                    component="button"
                    type="button"
                    onClick={() => setDialogTask(null)}
                    sx={{
                        bgcolor: 'primary.main', color: '#fff', border: 'none', borderRadius: '6px',
                        px: '14px', py: '7px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                        fontFamily: 'inherit', whiteSpace: 'nowrap',
                        '&:hover': { bgcolor: 'primary.dark' },
                    }}
                >
                    + New task
                </Box>
            </Box>

            {/* Grid */}
            {visible.length === 0 ? (
                <Box sx={{
                    bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
                    py: 6, textAlign: 'center', color: 'text.secondary', fontSize: 13,
                }}>
                    {all.length === 0 || (search.trim() === '' && status === 'open' && priority === 'any' && departmentId == null)
                        ? VIEWS.find((v) => v.value === view)!.empty
                        : 'No tasks match the current filters.'}
                </Box>
            ) : (
                <Box sx={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))',
                    gap: '14px',
                }}>
                    {visible.map((task) => (
                        <TaskCard
                            key={task.id}
                            task={task}
                            today={today}
                            statusPending={moveStatus.isPending}
                            onStatus={(next) => moveStatus.mutate({ id: task.id, next })}
                            onEdit={() => setDialogTask(task)}
                            onDelete={() => void confirmDelete(task)}
                        />
                    ))}
                    <AddCard onClick={() => setDialogTask(null)} />
                </Box>
            )}

            <TaskDialog
                open={dialogTask !== undefined}
                task={dialogTask ?? null}
                onClose={() => setDialogTask(undefined)}
                onSaved={() => { setDialogTask(undefined); void refresh() }}
            />
        </Box>
    )
})

export default TasksPage

/* ════════════════════════════════════════════════════════════════════════ */
/* Card                                                                     */
/* ════════════════════════════════════════════════════════════════════════ */

function TaskCard({ task, today, statusPending, onStatus, onEdit, onDelete }: {
    task: WorkTask
    today: string
    statusPending: boolean
    onStatus: (next: WorkTaskStatus) => void
    onEdit: () => void
    onDelete: () => void
}) {
    const closed = !isOpenTask(task)
    const late = overdueDays(task, today)
    const status = STATUS_COLORS[task.status]
    const priority = PRIORITY_COLORS[task.priority]
    const codeColor = closed ? 'text.disabled' : (CODE_COLORS[task.projectColorKey ?? ''] ?? CODE_COLORS.p1)

    const visibleTeam = task.assignees.slice(0, 6)
    const remaining = task.assignees.length - visibleTeam.length

    return (
        <Box data-testid="task-card" sx={{
            bgcolor: closed ? 'action.hover' : 'background.paper',
            border: '1px solid', borderColor: late > 0 ? 'warning.main' : 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'all 0.15s',
            display: 'flex', flexDirection: 'column',
            opacity: closed ? 0.75 : 1,
            '&:hover': { transform: 'translateY(-2px)' },
        }}>
            {/* Header */}
            <Box sx={{
                p: '16px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '12px',
            }}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    {task.projectCode && (
                        <Box sx={{
                            display: 'inline-block', bgcolor: codeColor, color: '#fff',
                            fontSize: 11, fontWeight: 700, px: '8px', py: '3px',
                            borderRadius: '6px', letterSpacing: '0.02em', mb: '6px',
                        }}>{task.projectCode}</Box>
                    )}
                    <Box sx={{ fontSize: 16, fontWeight: 700, color: 'text.primary', lineHeight: 1.3, mb: '6px', wordBreak: 'break-word' }}>
                        {task.title}
                    </Box>
                    <Box sx={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
                        <Box sx={{
                            fontSize: 11, px: '8px', py: '2px', borderRadius: '10px',
                            bgcolor: 'action.hover', color: 'text.secondary', fontWeight: 500,
                        }}>{task.departmentName}</Box>
                        {task.projectName && (
                            <Box sx={{ fontSize: 11, color: 'text.secondary' }}>{task.projectName}</Box>
                        )}
                    </Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '6px' }}>
                        From: <Box component="strong" sx={{ color: 'text.primary', fontWeight: 600 }}>{task.createdByName}</Box>
                    </Box>
                </Box>
                <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '6px' }}>
                    <Box sx={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        px: '10px', py: '4px', borderRadius: '12px',
                        fontSize: 11, fontWeight: 600,
                        bgcolor: status.bg, color: status.fg, whiteSpace: 'nowrap',
                    }}>
                        <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: status.dot }} />
                        {STATUS_LABELS[task.status]}
                    </Box>
                    <Box sx={{
                        px: '8px', py: '2px', borderRadius: '10px',
                        fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap',
                        bgcolor: priority.bg, color: priority.fg,
                    }}>{PRIORITY_LABELS[task.priority]} priority</Box>
                </Box>
            </Box>

            {/* Description */}
            {task.description && (
                <Box sx={{ p: '12px 18px', borderBottom: '1px solid', borderBottomColor: 'divider' }}>
                    <Box sx={{
                        fontSize: 12, color: 'text.secondary', lineHeight: 1.5, whiteSpace: 'pre-wrap',
                        display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical', overflow: 'hidden',
                    }}>{task.description}</Box>
                </Box>
            )}

            {/* Assignees */}
            <Box sx={{ p: '12px 18px', borderBottom: '1px solid', borderBottomColor: 'divider' }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '8px' }}>
                    <SectionLabel>Assignees</SectionLabel>
                    <Box sx={{ fontSize: 13, fontWeight: 700, color: 'text.primary' }}>
                        {task.assignees.length}
                        <Box component="span" sx={{ fontSize: 11, color: 'text.secondary', fontWeight: 500, ml: '4px' }}>
                            {task.assignees.length === 1 ? 'person' : 'people'}
                        </Box>
                    </Box>
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                    {visibleTeam.map((m) => (
                        <Box
                            key={m.userId}
                            title={m.displayName}
                            sx={{
                                width: 32, height: 32, borderRadius: '50%',
                                bgcolor: avatarBg(m.displayName || m.userId), color: '#fff',
                                display: 'flex', alignItems: 'center', justifyContent: 'center',
                                fontSize: 11, fontWeight: 600,
                                border: '2px solid', borderColor: 'background.paper',
                                marginLeft: '-6px', '&:first-of-type': { marginLeft: 0 },
                            }}
                        >{initials(m.displayName)}</Box>
                    ))}
                    {remaining > 0 && (
                        <Box sx={{
                            width: 32, height: 32, borderRadius: '50%',
                            bgcolor: 'action.hover', color: 'text.secondary',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            fontSize: 10, fontWeight: 600,
                            border: '2px solid', borderColor: 'background.paper', marginLeft: '-6px',
                        }}>+{remaining}</Box>
                    )}
                    {task.assignees.length === 1 && (
                        <Box sx={{ fontSize: 12, color: 'text.primary', fontWeight: 500, ml: '10px' }}>
                            {task.assignees[0].displayName}
                        </Box>
                    )}
                </Box>
            </Box>

            {/* Overdue banner */}
            {late > 0 && (
                <Box sx={{
                    p: '8px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: softBg('warning'), borderLeft: '3px solid', borderLeftColor: 'warning.main',
                    fontSize: 11, color: 'warning.dark', fontWeight: 600,
                }}>
                    ⚠ Overdue by {plural(late, 'day')}
                </Box>
            )}

            {/* Stats triplet */}
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '1px', bgcolor: 'divider', mt: 'auto' }}>
                <CardStat
                    label="Due"
                    value={task.dueDate ? formatDate(task.dueDate) : '—'}
                    sub={task.dueDate ? (late > 0 ? 'past due' : closed ? 'closed' : 'on track') : 'no date set'}
                    valueColor={late > 0 ? 'error.main' : undefined}
                />
                <CardStat label="Priority" value={PRIORITY_LABELS[task.priority]} sub="as set" valueColor={priority.fg} />
                <CardStat
                    label={task.status === 'Done' && task.completedAtUtc ? 'Completed' : 'Created'}
                    value={formatDate(task.status === 'Done' && task.completedAtUtc ? task.completedAtUtc : task.createdAtUtc)}
                    sub={task.status === 'Done' ? 'finished' : 'opened'}
                />
            </Box>

            {/* Footer */}
            <Box sx={{ display: 'flex', gap: '6px', p: '10px 14px', bgcolor: 'action.hover', alignItems: 'center' }}>
                {task.canChangeStatus ? (
                    <StatusControls status={task.status} pending={statusPending} onStatus={onStatus} />
                ) : (
                    <Box sx={{ fontSize: 11, color: 'text.disabled' }}>Only the creator and assignees change the status</Box>
                )}
                <Box sx={{ flex: 1 }} />
                {task.canEdit && (
                    <>
                        <OutlineBtn onClick={onEdit}>✏️ Edit</OutlineBtn>
                        <OutlineBtn danger onClick={onDelete}>🗑 Delete</OutlineBtn>
                    </>
                )}
            </Box>
        </Box>
    )
}

/**
 * The card's status controls: the obvious next step as a filled button, and every
 * status in a menu beside it for the rest (cancelling, stepping back).
 */
function StatusControls({ status, pending, onStatus }: {
    status: WorkTaskStatus
    pending: boolean
    onStatus: (next: WorkTaskStatus) => void
}) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null)
    const next = nextStatusAction(status)
    return (
        <>
            <Box
                component="button"
                type="button"
                disabled={pending}
                onClick={() => onStatus(next.to)}
                sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                    bgcolor: next.to === 'Done' ? 'success.main' : 'primary.main', color: '#fff',
                    border: 'none', borderRadius: '6px', px: '12px', py: '6px',
                    fontSize: 12, fontWeight: 600, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                    '&:hover': { bgcolor: next.to === 'Done' ? 'success.dark' : 'primary.dark' },
                    '&:disabled': { opacity: 0.6, cursor: 'default' },
                }}
            >
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>{next.icon}</Box>
                {next.label}
            </Box>
            <Box
                component="button"
                type="button"
                aria-haspopup="menu"
                aria-expanded={anchor != null}
                disabled={pending}
                onClick={(e: React.MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)}
                sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                    bgcolor: 'background.paper', color: 'text.primary',
                    border: '1px solid', borderColor: 'divider', borderRadius: '6px', px: '10px', py: '6px',
                    fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                    '&:hover': { borderColor: 'primary.main', color: 'primary.main' },
                    '&:disabled': { opacity: 0.6, cursor: 'default' },
                }}
            >
                Status
                <Box component="span" aria-hidden sx={{ fontSize: 9, color: 'text.secondary' }}>▼</Box>
            </Box>
            <Menu
                anchorEl={anchor}
                open={anchor != null}
                onClose={() => setAnchor(null)}
                slotProps={{ paper: { sx: { minWidth: 170, borderRadius: '10px', mt: '4px' } } }}
            >
                {(Object.keys(STATUS_LABELS) as WorkTaskStatus[]).map((s) => {
                    const current = s === status
                    return (
                        <MenuItem
                            key={s}
                            aria-current={current ? 'true' : undefined}
                            selected={current}
                            onClick={() => {
                                setAnchor(null)
                                if (!current) onStatus(s)
                            }}
                            sx={{ fontSize: 13, gap: '10px' }}
                        >
                            <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: STATUS_COLORS[s].dot, flexShrink: 0 }} />
                            <Box component="span" sx={{ flex: 1 }}>{STATUS_LABELS[s]}</Box>
                            {current && <Box component="span" aria-hidden sx={{ fontSize: 12, color: 'primary.main' }}>✓</Box>}
                        </MenuItem>
                    )
                })}
            </Menu>
        </>
    )
}

function AddCard({ onClick }: { onClick: () => void }) {
    return (
        <Box
            component="button"
            type="button"
            onClick={onClick}
            sx={{
                bgcolor: 'action.hover', border: '2px dashed', borderColor: 'divider',
                borderRadius: '12px', p: '40px 20px', minHeight: 280,
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                cursor: 'pointer', fontFamily: 'inherit', textAlign: 'center',
                color: 'text.secondary', transition: 'all 0.15s',
                '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary'), transform: 'translateY(-2px)' },
            }}
        >
            <Box sx={{
                width: 56, height: 56, borderRadius: '50%',
                bgcolor: 'background.paper', border: '2px dashed', borderColor: 'divider',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 24, color: 'text.secondary', mb: '12px',
            }}>+</Box>
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>Create a new task</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                Pick a project, assign the people,<br />and track it to done
            </Box>
        </Box>
    )
}
