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
import { canManageTasks, canUseTasks, isHrAdministrator } from '../../lib/roles'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from '../../lib/types'
import {
    PRIORITY_LABELS, SETTABLE_STATUSES, STATUS_LABELS, describeTaskProgress, filterTasks, formatTaskDate as formatDate, isAwaitingConfirmation, isOpenTask, nextStatusAction, openCount, overdueDays, taskStats, tasksToCsv, todayIso,
    type StatusFilter, type TaskTab,
} from '../../lib/work-tasks'
import { SweetAlert } from '../ui'
import { CardStat, OutlineBtn, SectionLabel, SelectFilter, StatCard } from '../ui/CardKit'
import { CODE_COLORS, avatarBg, initials } from '../../lib/card-kit'
import IdlePeoplePanel from './IdlePeoplePanel'
import SendBackDialog from './SendBackDialog'
import TaskDetailsDialog from './TaskDetailsDialog'
import TaskDialog from './TaskDialog'
import { PRIORITY_COLORS, STATUS_COLORS } from './statusStyles'

/* ─── tokens ─────────────────────────────────────────────────────────────── */

const VIEWS: { value: TaskTab; label: string; empty: string }[] = [
    { value: 'assigned', label: 'Assigned to me', empty: 'Nothing assigned to you.' },
    { value: 'created', label: 'Created by me', empty: "You haven't created any tasks." },
    { value: 'all', label: 'All in my departments', empty: 'No tasks in your departments.' },
]

/* ─── helpers ────────────────────────────────────────────────────────────── */

function plural(n: number, one: string, many = `${one}s`) {
    return `${n} ${n === 1 ? one : many}`
}

function downloadTasksCsv(tasks: readonly WorkTask[]) {
    // The BOM makes Excel read the file as UTF-8, so names with accents survive.
    const blob = new Blob(['\uFEFF', tasksToCsv(tasks)], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `tasks-${todayIso()}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
}

/* ════════════════════════════════════════════════════════════════════════ */

const TasksPage = observer(function TasksPage() {
    const { authStore } = useStore()
    const userId = authStore.user?.id ?? ''
    // An Employee works the tasks they are given and creates their own, always
    // assigned to themselves: no views to pick between, since the server sends them
    // only the tasks they are on, and no assignee picker in the dialog.
    const manages = canManageTasks(authStore.user?.roles)
    const creates = canUseTasks(authStore.user?.roles)
    // An HR Administrator is never handed a task, so "Assigned to me" would always be
    // empty: they see everything in their departments, with no view picker, and
    // narrow it with the department filter instead.
    const isHr = isHrAdministrator(authStore.user?.roles)
    const queryClient = useQueryClient()

    const [view, setView] = useState<TaskTab>(isHr ? 'all' : 'assigned')
    const [status, setStatus] = useState<StatusFilter>('open')
    const [departmentId, setDepartmentId] = useState<number | null>(null)
    const [priority, setPriority] = useState<WorkTaskPriority | 'any'>('any')
    const [search, setSearch] = useState('')
    const [dialogTask, setDialogTask] = useState<WorkTask | null | undefined>(undefined) // undefined = closed
    // The task whose details are open, by id so the dialog follows the list as it refreshes.
    const [detailsId, setDetailsId] = useState<number | null>(null)
    // A task saved without some of the files picked for it.
    const [saveWarning, setSaveWarning] = useState<string | null>(null)

    const tasks = useQuery({ queryKey: ['work-tasks'], queryFn: getWorkTasks })
    const departments = useQuery({ queryKey: ['work-tasks', 'departments'], queryFn: getWorkTaskDepartments, enabled: creates })

    const refresh = () => queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
    const moveStatus = useMutation({
        mutationFn: ({ id, next, reason }: { id: number; next: WorkTaskStatus; reason?: string }) =>
            reason === undefined ? updateWorkTaskStatus(id, next) : updateWorkTaskStatus(id, next, reason),
        onSuccess: refresh,
    })
    // The waiting task a reviewer is sending back, while its reason dialog is open.
    const [sendingBack, setSendingBack] = useState<WorkTask | null>(null)
    const remove = useMutation({ mutationFn: (id: number) => deleteWorkTask(id), onSuccess: refresh })

    const today = todayIso()
    const all = useMemo(() => tasks.data ?? [], [tasks.data])
    const stats = useMemo(() => taskStats(all, userId, today), [all, userId, today])
    const visible = useMemo(
        () => filterTasks(all, { tab: view, status, departmentId, userId, priority, search }),
        [all, view, status, departmentId, userId, priority, search],
    )
    // A send-back's own error shows in its dialog, not twice.
    const mutationError = (sendingBack ? null : moveStatus.error) ?? remove.error
    // An Employee's page puts the work handed to them first and keeps their own
    // tasks (always assigned to themselves) apart underneath.
    const handedToMe = useMemo(() => visible.filter((t) => t.createdById !== userId), [visible, userId])
    const myOwn = useMemo(() => visible.filter((t) => t.createdById === userId), [visible, userId])
    const handedToMeOpen = useMemo(
        () => all.filter((t) => t.createdById !== userId && isOpenTask(t)).length,
        [all, userId],
    )

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
            {saveWarning && (
                <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setSaveWarning(null)}>{saveWarning}</Alert>
            )}

            {/* Stats row */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr 1fr', md: `repeat(${(isHr ? 3 : 4) + (manages ? 1 : 0)}, 1fr)` },
                gap: '12px', mb: '14px',
            }}>
                <Box data-testid="stat-open">
                    <StatCard
                        label="📋 Open Tasks"
                        value={String(stats.open)}
                        sub={`of ${stats.total} total · ${stats.inProgress} in progress`}
                    />
                </Box>
                {manages && <Box data-testid="stat-confirm">
                    <StatCard
                        label="🕓 To Confirm"
                        value={String(stats.awaitingMyConfirmation)}
                        valueColor={stats.awaitingMyConfirmation > 0 ? 'warning.main' : undefined}
                        sub="done, waiting for you"
                    />
                </Box>}
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
                {!isHr && <Box data-testid="stat-mine">
                    {manages ? (
                        <StatCard
                            label="👤 Assigned To Me"
                            value={String(stats.assignedToMeOpen)}
                            valueColor="primary.main"
                            sub="open tasks on your plate"
                        />
                    ) : (
                        <StatCard
                            label="📥 From Manager & HR"
                            value={String(handedToMeOpen)}
                            valueColor="primary.main"
                            sub="open tasks handed to you"
                        />
                    )}
                </Box>}
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
                {manages && !isHr && (
                    <SelectFilter
                        ariaLabel="View"
                        value={view}
                        onChange={(v) => setView(v as TaskTab)}
                        options={VIEWS.map((v) => ({ value: v.value, label: `${v.label} (${openCount(all, v.value, userId)})` }))}
                    />
                )}
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
                {(departments.data?.length ?? 0) > (isHr ? 0 : 1) && (
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
                {/* HR reports on the tasks they run; the file is what the filters show. */}
                {isHr && (
                    <Box
                        component="button"
                        type="button"
                        onClick={() => downloadTasksCsv(visible)}
                        disabled={visible.length === 0}
                        sx={{
                            bgcolor: 'background.paper', color: 'text.primary',
                            border: '1px solid', borderColor: 'divider', borderRadius: '6px',
                            px: '14px', py: '7px', fontSize: 13, fontWeight: 500, cursor: 'pointer',
                            fontFamily: 'inherit', whiteSpace: 'nowrap',
                            '&:hover:not(:disabled)': { bgcolor: 'action.hover', borderColor: 'primary.main', color: 'primary.main' },
                            '&:disabled': { cursor: 'default', color: 'text.disabled' },
                        }}
                    >
                        ⤓ Export CSV
                    </Box>
                )}
                {creates && <Box
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
                </Box>}
            </Box>

            {manages && <IdlePeoplePanel departmentId={departmentId} />}

            {/* Grid */}
            {!manages ? (
                <>
                    <TaskSection
                        title="From your manager & HR"
                        subtitle="Work handed to you comes first."
                        count={handedToMe.length}
                        accent
                    >
                        {handedToMe.length === 0 ? (
                            <SectionEmpty>
                                {all.some((t) => t.createdById !== userId) ? 'No tasks match the current filters.' : 'Nothing has been handed to you yet.'}
                            </SectionEmpty>
                        ) : (
                            <CardGrid>
                                {handedToMe.map((task) => (
                                    <TaskCard
                                    key={task.id}
                                    task={task}
                                    today={today}
                                    statusPending={moveStatus.isPending}
                                    onStatus={(next) => moveStatus.mutate({ id: task.id, next })}
                                    onOpen={() => setDetailsId(task.id)}
                                    onEdit={() => setDialogTask(task)}
                                    onDelete={() => void confirmDelete(task)}
                                    onSendBack={() => { moveStatus.reset(); setSendingBack(task) }}
                                />
                                ))}
                            </CardGrid>
                        )}
                    </TaskSection>
                    <TaskSection title="My own tasks" subtitle="Tasks you created for yourself." count={myOwn.length}>
                        <CardGrid>
                            {myOwn.map((task) => (
                                <TaskCard
                                    key={task.id}
                                    task={task}
                                    today={today}
                                    statusPending={moveStatus.isPending}
                                    onStatus={(next) => moveStatus.mutate({ id: task.id, next })}
                                    onOpen={() => setDetailsId(task.id)}
                                    onEdit={() => setDialogTask(task)}
                                    onDelete={() => void confirmDelete(task)}
                                    onSendBack={() => { moveStatus.reset(); setSendingBack(task) }}
                                />
                            ))}
                            <AddCard onClick={() => setDialogTask(null)} personal />
                        </CardGrid>
                    </TaskSection>
                </>
            ) : visible.length === 0 ? (
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
                            onOpen={() => setDetailsId(task.id)}
                            onEdit={() => setDialogTask(task)}
                            onDelete={() => void confirmDelete(task)}
                            onSendBack={() => { moveStatus.reset(); setSendingBack(task) }}
                        />
                    ))}
                    {creates && <AddCard onClick={() => setDialogTask(null)} />}
                </Box>
            )}

            <SendBackDialog
                open={sendingBack != null}
                taskTitle={sendingBack?.title ?? ''}
                pending={moveStatus.isPending}
                error={sendingBack && moveStatus.error ? getApiErrorMessage(moveStatus.error, 'The task could not be sent back.') : null}
                onCancel={() => { setSendingBack(null); moveStatus.reset() }}
                onSubmit={(reason) => moveStatus.mutate(
                    { id: sendingBack!.id, next: 'InProgress', reason },
                    { onSuccess: () => setSendingBack(null) },
                )}
            />

            <TaskDetailsDialog
                task={detailsId == null ? null : all.find((t) => t.id === detailsId) ?? null}
                today={today}
                onClose={() => setDetailsId(null)}
                onEdit={(task) => { setDetailsId(null); setDialogTask(task) }}
            />

            <TaskDialog
                open={dialogTask !== undefined}
                task={dialogTask ?? null}
                personal={!manages}
                onClose={() => setDialogTask(undefined)}
                onSaved={(warning) => { setDialogTask(undefined); setSaveWarning(warning ?? null); void refresh() }}
            />
        </Box>
    )
})

export default TasksPage

/* ════════════════════════════════════════════════════════════════════════ */
/* Card                                                                     */
/* ════════════════════════════════════════════════════════════════════════ */

function TaskCard({ task, today, statusPending, onStatus, onOpen, onEdit, onDelete, onSendBack }: {
    task: WorkTask
    today: string
    statusPending: boolean
    onStatus: (next: WorkTaskStatus) => void
    /** The details dialog: the whole description and the files, which the card only summarises. */
    onOpen: () => void
    onEdit: () => void
    onDelete: () => void
    /** A reviewer returning a waiting task: opens the reason dialog. */
    onSendBack: () => void
}) {
    const closed = !isOpenTask(task)
    const late = overdueDays(task, today)
    const progress = describeTaskProgress(task.targetHours, task.loggedHours)
    const status = STATUS_COLORS[task.status]
    const priority = PRIORITY_COLORS[task.priority]
    const codeColor = closed ? 'text.disabled' : (CODE_COLORS[task.projectColorKey ?? ''] ?? CODE_COLORS.p1)

    const attachmentCount = task.attachments?.length ?? 0
    const visibleTeam = task.assignees.slice(0, 6)
    const remaining = task.assignees.length - visibleTeam.length

    return (
        <Box data-testid="task-card" onClick={onOpen} sx={{
            bgcolor: closed ? 'action.hover' : 'background.paper',
            border: '1px solid', borderColor: late > 0 ? 'warning.main' : 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'all 0.15s', cursor: 'pointer',
            display: 'flex', flexDirection: 'column',
            opacity: closed ? 0.75 : 1,
            '&:hover': { transform: 'translateY(-2px)', borderColor: late > 0 ? 'warning.main' : 'primary.main' },
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
                    {/* A real button, so the details open from the keyboard too; one line, so a long title never grows the card. */}
                    <Box
                        component="button"
                        type="button"
                        title={task.title}
                        onClick={(e: React.MouseEvent) => { e.stopPropagation(); onOpen() }}
                        sx={{
                            display: 'block', maxWidth: '100%', p: 0, border: 'none', bgcolor: 'transparent', textAlign: 'left',
                            fontFamily: 'inherit', cursor: 'pointer',
                            fontSize: 16, fontWeight: 700, color: 'text.primary', lineHeight: 1.3, mb: '6px',
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            '&:hover': { color: 'primary.main' },
                        }}
                    >
                        {task.title}
                    </Box>
                    <Box sx={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
                        <Box sx={{
                            fontSize: 11, px: '8px', py: '2px', borderRadius: '10px',
                            bgcolor: 'action.hover', color: 'text.secondary', fontWeight: 500,
                        }}>{task.departmentName}</Box>
                        {task.isBillable != null && (
                            <Box sx={{
                                fontSize: 11, px: '8px', py: '2px', borderRadius: '10px', fontWeight: 600,
                                bgcolor: task.isBillable ? softBg('success') : 'action.hover',
                                color: task.isBillable ? 'success.dark' : 'text.secondary',
                            }}>{task.isBillable ? 'Billable' : 'Non-billable'}</Box>
                        )}
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

            {/* Summary: always the same height, whatever the description or the files, so
                every card in the grid lines up. The whole of both is in the details dialog. */}
            <Box data-testid="task-summary" sx={{ p: '12px 18px', borderBottom: '1px solid', borderBottomColor: 'divider' }}>
                <Box sx={{
                    fontSize: 12, lineHeight: 1.5, height: '3em', whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                    color: task.description ? 'text.secondary' : 'text.disabled', fontStyle: task.description ? 'normal' : 'italic',
                    display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden',
                }}>{task.description || 'No description'}</Box>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: '8px', fontSize: 11 }}>
                    <Box sx={{ color: attachmentCount > 0 ? 'text.primary' : 'text.disabled', fontWeight: attachmentCount > 0 ? 600 : 400 }}>
                        📎 {attachmentCount === 0 ? 'No attachments' : plural(attachmentCount, 'attachment')}
                    </Box>
                    <Box sx={{ color: 'primary.main', fontWeight: 600 }}>View details →</Box>
                </Box>
            </Box>

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
            {isAwaitingConfirmation(task) && !task.canConfirm && (
                <Box sx={{
                    p: '8px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: softBg('warning'), borderLeft: '3px solid', borderLeftColor: 'warning.main',
                    fontSize: 11, color: 'warning.dark', fontWeight: 600,
                }}>
                    Waiting for {task.createdByName} to confirm
                </Box>
            )}
            {isOpenTask(task) && !isAwaitingConfirmation(task) && task.sentBackReason && (
                <Box sx={{
                    p: '8px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: softBg('error'), borderLeft: '3px solid', borderLeftColor: 'error.main',
                    fontSize: 11, color: 'error.dark', fontWeight: 600, whiteSpace: 'pre-wrap',
                }}>
                    Sent back: {task.sentBackReason}
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
                <CardStat
                    label="Target"
                    value={task.targetHours != null ? `${task.targetHours}h` : '—'}
                    sub={task.targetHours == null && progress.text === 'nothing logged' ? 'no target set' : progress.text}
                    valueColor={progress.over ? 'error.main' : undefined}
                />
                <CardStat
                    label={task.status === 'Done' && task.completedAtUtc ? 'Completed' : 'Created'}
                    value={formatDate(task.status === 'Done' && task.completedAtUtc ? task.completedAtUtc : task.createdAtUtc)}
                    sub={task.status === 'Done' ? 'finished' : 'opened'}
                />
            </Box>

            {/* Footer. Its own clicks (the status menu's included, which bubble through
                the portal) are not a click on the card. */}
            <Box onClick={(e) => e.stopPropagation()} sx={{ display: 'flex', gap: '6px', p: '10px 14px', bgcolor: 'action.hover', alignItems: 'center', cursor: 'default' }}>
                {task.canConfirm ? (
                    <ReviewControls pending={statusPending} onConfirm={() => onStatus('Done')} onSendBack={onSendBack} />
                ) : task.canChangeStatus ? (
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

/** A reviewer's two answers to a task marked done. */
function ReviewControls({ pending, onConfirm, onSendBack }: { pending: boolean; onConfirm: () => void; onSendBack: () => void }) {
    const btn = {
        display: 'inline-flex', alignItems: 'center', gap: '6px', borderRadius: '6px', px: '12px', py: '6px',
        fontSize: 12, fontWeight: 600, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
        '&:disabled': { opacity: 0.6, cursor: 'default' },
    } as const
    return (
        <>
            <Box component="button" type="button" disabled={pending} onClick={onConfirm}
                sx={{ ...btn, bgcolor: 'success.main', color: '#fff', border: 'none', '&:hover': { bgcolor: 'success.dark' } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>✓</Box>Confirm
            </Box>
            <Box component="button" type="button" disabled={pending} onClick={onSendBack}
                sx={{ ...btn, bgcolor: 'background.paper', color: 'warning.dark', border: '1px solid', borderColor: 'warning.main', '&:hover': { bgcolor: softBg('warning') } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>↩</Box>Send back
            </Box>
        </>
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
    // An assignee's one move on a task waiting for confirmation is Withdraw; the rest is the reviewer's.
    const waiting = status === 'AwaitingConfirmation'
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
            {!waiting && (
                <>
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
                    {SETTABLE_STATUSES.map((s) => {
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
            )}
        </>
    )
}

function AddCard({ onClick, personal = false }: { onClick: () => void; personal?: boolean }) {
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
                ...(personal && { minHeight: 180, p: '28px 20px' }),
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
            <Box sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary', mb: '4px' }}>{personal ? 'Add a task of your own' : 'Create a new task'}</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary', lineHeight: 1.5 }}>
                {personal ? <>Pick a project and track<br />your own work to done</> : <>Pick a project, assign the people,<br />and track it to done</>}
            </Box>
        </Box>
    )
}

function CardGrid({ children }: { children: React.ReactNode }) {
    return (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))', gap: '14px' }}>
            {children}
        </Box>
    )
}

/** One of an Employee's two groups: the work handed to them (accented), and their own. */
function TaskSection({ title, subtitle, count, accent = false, children }: {
    title: string
    subtitle: string
    count: number
    accent?: boolean
    children: React.ReactNode
}) {
    return (
        <Box component="section" aria-label={title} sx={{ mb: '22px' }}>
            <Box sx={{
                display: 'flex', alignItems: 'baseline', gap: '10px', flexWrap: 'wrap', mb: '10px', pl: '10px',
                borderLeft: '3px solid', borderLeftColor: accent ? 'primary.main' : 'divider',
            }}>
                <Box component="h2" sx={{ m: 0, fontSize: accent ? 16 : 14, fontWeight: 700, color: accent ? 'text.primary' : 'text.secondary' }}>
                    {title}
                </Box>
                <Box data-testid="section-count" sx={{
                    fontSize: 11, fontWeight: 700, px: '8px', py: '1px', borderRadius: '10px',
                    bgcolor: accent ? softBg('primary') : 'action.hover', color: accent ? 'primary.main' : 'text.secondary',
                }}>
                    {count}
                </Box>
                <Box sx={{ fontSize: 12, color: 'text.secondary' }}>{subtitle}</Box>
            </Box>
            {children}
        </Box>
    )
}

function SectionEmpty({ children }: { children: React.ReactNode }) {
    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px',
            py: 4, textAlign: 'center', color: 'text.secondary', fontSize: 13,
        }}>
            {children}
        </Box>
    )
}
