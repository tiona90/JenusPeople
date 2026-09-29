import { useMemo, useState } from 'react'
import { observer } from 'mobx-react-lite'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import { deleteWorkTask, getWorkTaskDepartments, getWorkTasks, updateWorkTaskStatus } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { WorkTaskSettings } from '../../lib/api/work-task-settings'
import { useStore } from '../../lib/mobx'
import { canManageTasks, canUseTasks, isHrAdministrator } from '../../lib/roles'
import { isShown } from '../../lib/task-settings'
import { useWorkTaskSettings } from '../../lib/task-settings-query'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTask, WorkTaskPriority, WorkTaskStatus } from '../../lib/types'
import {
    PRIORITY_LABELS, STATUS_LABELS, boardStatuses, filterTasks, isOpenTask, taskStats, tasksToCsv, todayIso,
    type StatusFilter, type TaskLayout,
} from '../../lib/work-tasks'
import { SweetAlert } from '../ui'
import { SelectFilter, StatCard } from '../ui/CardKit'
import IdlePeoplePanel from './IdlePeoplePanel'
import SendBackDialog from './SendBackDialog'
import TaskBoard from './TaskBoard'
import { TaskCard, type TaskItemProps } from './TaskCard'
import TaskDetailsDialog from './TaskDetailsDialog'
import TaskDialog from './TaskDialog'
import TaskList from './TaskList'

/* ─── tokens ─────────────────────────────────────────────────────────────── */

type Audience = 'employee' | 'manager' | 'hr'

interface Section {
    title: string
    subtitle: string
    accent: boolean
    tasks: WorkTask[]
    /** The one-line strip shown in place of the cards when the group is empty. */
    empty: string
    /** Where the "create" card sits: the group of tasks the viewer runs themselves. */
    add: boolean
}

/* ─── helpers ────────────────────────────────────────────────────────────── */

// Per-viewer conveniences, so a reload keeps the layout and the folded groups. Storage
// can be missing or throw (a private window, blocked site data): the page then just
// opens on the defaults.
const LAYOUT_KEY = 'tasks-layout'
const collapsedKey = (userId: string) => `tasks-collapsed-sections:${userId}`

function readStored<T>(key: string, parse: (raw: string) => T | null, fallback: T): T {
    try {
        const raw = window.localStorage.getItem(key)
        return raw == null ? fallback : (parse(raw) ?? fallback)
    } catch {
        return fallback
    }
}

function writeStored(key: string, value: string) {
    try { window.localStorage.setItem(key, value) } catch { /* nothing to keep it in */ }
}

const parseLayout = (raw: string): TaskLayout | null => (raw === 'cards' || raw === 'list' || raw === 'board' ? raw : null)
const parseTitles = (raw: string): string[] | null => {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed.filter((t): t is string => typeof t === 'string') : null
}

function downloadTasksCsv(tasks: readonly WorkTask[], settings: WorkTaskSettings) {
    // The BOM makes Excel read the file as UTF-8, so names with accents survive.
    const blob = new Blob(['\uFEFF', tasksToCsv(tasks, settings)], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `tasks-${todayIso()}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
}

/**
 * The groups in the order the page draws them: those with something to show first,
 * so an empty group — an HR Administrator's "Created by you", say — is a one-line
 * strip under the work rather than a gap above it. Otherwise the order is kept.
 */
function orderSections(sections: Section[]): Section[] {
    return [...sections.filter((s) => s.tasks.length > 0), ...sections.filter((s) => s.tasks.length === 0)]
}

/**
 * The page's groups, each over the filtered tasks. A task lands in the first group
 * that claims it, so a Manager's task on their own plate shows once, under
 * "Assigned to you", even when they created it. `all` only tells an empty group
 * apart from one the filters emptied, and hides a trailing group with nothing in it.
 */
function buildSections(all: readonly WorkTask[], visible: readonly WorkTask[], userId: string, audience: Audience): Section[] {
    const assigned = (t: WorkTask) => t.assignees.some((a) => a.userId === userId)
    const created = (t: WorkTask) => t.createdById === userId
    const noMatch = 'No tasks match the current filters.'
    const empty = (claims: (t: WorkTask) => boolean, never: string) => (all.some(claims) ? noMatch : never)

    if (audience === 'employee') {
        const handed = (t: WorkTask) => !created(t)
        return [
            { title: 'From your manager & HR', subtitle: 'Work handed to you comes first.', accent: true, tasks: visible.filter(handed), empty: empty(handed, 'Nothing has been handed to you yet.'), add: false },
            { title: 'My own tasks', subtitle: 'Tasks you created for yourself.', accent: false, tasks: visible.filter(created), empty: empty(created, "You haven't added a task of your own yet."), add: true },
        ]
    }

    if (audience === 'hr') {
        const others = (t: WorkTask) => !created(t)
        return [
            { title: 'Created by you', subtitle: 'Tasks you handed out.', accent: true, tasks: visible.filter(created), empty: empty(created, "You haven't handed out a task yet."), add: true },
            ...(all.some(others)
                ? [{ title: 'Created by others', subtitle: 'Run by managers, other HR and employees in your departments.', accent: false, tasks: visible.filter(others), empty: noMatch, add: false }]
                : []),
        ]
    }

    const handedOut = (t: WorkTask) => created(t) && !assigned(t)
    const others = (t: WorkTask) => !created(t) && !assigned(t)
    return [
        { title: 'Assigned to you', subtitle: 'Work on your plate comes first.', accent: true, tasks: visible.filter(assigned), empty: empty(assigned, 'Nothing assigned to you.'), add: false },
        { title: 'Created by you', subtitle: 'Tasks you handed to your team.', accent: false, tasks: visible.filter(handedOut), empty: empty(handedOut, "You haven't handed out a task yet."), add: true },
        ...(all.some(others)
            ? [{ title: 'Others in your departments', subtitle: 'Run by other managers, HR and employees.', accent: false, tasks: visible.filter(others), empty: noMatch, add: false }]
            : []),
    ]
}

/* ════════════════════════════════════════════════════════════════════════ */

const TasksPage = observer(function TasksPage() {
    const { authStore } = useStore()
    const userId = authStore.user?.id ?? ''
    // An Employee works the tasks they are given and creates their own, always
    // assigned to themselves: the server sends them only the tasks they are on, and
    // the dialog has no assignee picker. Every role sees the grid split into groups
    // (`buildSections`) rather than picking a view.
    const manages = canManageTasks(authStore.user?.roles)
    const creates = canUseTasks(authStore.user?.roles)
    // An HR Administrator is never handed a task, so an "Assigned to you" group would
    // always be empty: theirs are what they created and what others run, narrowed
    // with the department filter.
    const isHr = isHrAdministrator(authStore.user?.roles)
    const settings = useWorkTaskSettings()
    const queryClient = useQueryClient()

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
    // With Due date hidden, nothing is overdue — there is no date left to judge.
    const showOverdueTile = isShown(settings, 'dueDate')
    const all = useMemo(() => tasks.data ?? [], [tasks.data])
    const stats = useMemo(() => taskStats(all, userId, today, showOverdueTile), [all, userId, today, showOverdueTile])
    // A priority filter left on when the Task Settings hide priority has no control
    // to clear it — treat it as 'any' rather than silently keep it applied.
    const effectivePriority = isShown(settings, 'priority') ? priority : 'any'
    const visible = useMemo(
        () => filterTasks(all, { tab: 'all', status, departmentId, userId, priority: effectivePriority, search }),
        [all, status, departmentId, userId, effectivePriority, search],
    )
    // A send-back's own error shows in its dialog, not twice.
    const mutationError = (sendingBack ? null : moveStatus.error) ?? remove.error
    const handedToMeOpen = useMemo(
        () => all.filter((t) => t.createdById !== userId && isOpenTask(t)).length,
        [all, userId],
    )
    const sections = useMemo(
        () => orderSections(buildSections(all, visible, userId, manages ? (isHr ? 'hr' : 'manager') : 'employee')),
        [all, visible, userId, manages, isHr],
    )

    const [layout, setLayoutState] = useState<TaskLayout>(() => readStored(LAYOUT_KEY, parseLayout, 'cards'))
    const setLayout = (next: TaskLayout) => { setLayoutState(next); writeStored(LAYOUT_KEY, next) }
    // Folded groups, by title, per viewer.
    const [collapsed, setCollapsed] = useState<string[]>(() => readStored(collapsedKey(userId), parseTitles, []))
    const toggleSection = (title: string) => {
        const next = collapsed.includes(title) ? collapsed.filter((t) => t !== title) : [...collapsed, title]
        setCollapsed(next)
        writeStored(collapsedKey(userId), JSON.stringify(next))
    }
    const columns = useMemo(
        () => boardStatuses(status, settings.requireCompletionConfirmation, visible),
        [status, settings.requireCompletionConfirmation, visible],
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

    // What every card, board card and list row is handed besides the task.
    const itemProps = (task: WorkTask): TaskItemProps => ({
        task,
        today,
        settings,
        statusPending: moveStatus.isPending,
        onStatus: (next) => moveStatus.mutate({ id: task.id, next }),
        onOpen: () => setDetailsId(task.id),
        onEdit: () => setDialogTask(task),
        onDelete: () => void confirmDelete(task),
        onSendBack: () => { moveStatus.reset(); setSendingBack(task) },
    })

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
                gridTemplateColumns: {
                    xs: '1fr 1fr',
                    md: `repeat(${(isHr ? 3 : 4) - (showOverdueTile ? 0 : 1) + (manages && settings.requireCompletionConfirmation ? 1 : 0)}, 1fr)`,
                },
                gap: '12px', mb: '14px',
            }}>
                <Box data-testid="stat-open">
                    <StatCard
                        label="📋 Open Tasks"
                        value={String(stats.open)}
                        sub={`of ${stats.total} total · ${stats.inProgress} in progress`}
                    />
                </Box>
                {manages && settings.requireCompletionConfirmation && <Box data-testid="stat-confirm">
                    <StatCard
                        label="🕓 To Confirm"
                        value={String(stats.awaitingMyConfirmation)}
                        valueColor={stats.awaitingMyConfirmation > 0 ? 'warning.main' : undefined}
                        sub="done, waiting for you"
                    />
                </Box>}
                {showOverdueTile && <Box data-testid="stat-overdue">
                    <StatCard
                        label="⏰ Overdue"
                        value={String(stats.overdue)}
                        valueColor={stats.overdue > 0 ? 'warning.main' : 'success.main'}
                        sub={stats.overdue === 0 ? 'all on schedule' : `${stats.overdue === 1 ? 'task' : 'tasks'} past the due date`}
                    />
                </Box>}
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
                {isShown(settings, 'priority') && (
                    <SelectFilter
                        ariaLabel="Priority filter"
                        value={priority}
                        onChange={(v) => setPriority(v as WorkTaskPriority | 'any')}
                        options={[
                            { value: 'any', label: 'Any priority' },
                            ...(['High', 'Normal', 'Low'] as WorkTaskPriority[]).map((p) => ({ value: p, label: PRIORITY_LABELS[p] })),
                        ]}
                    />
                )}
                <Box sx={{ flex: 1 }} />
                <LayoutToggle value={layout} onChange={setLayout} />
                {/* HR reports on the tasks they run; the file is what the filters show. */}
                {isHr && (
                    <Box
                        component="button"
                        type="button"
                        onClick={() => downloadTasksCsv(visible, settings)}
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

            {/* The board lays every visible task out by status; the cards and the list
                keep the groups: an Employee's work handed to them and their own; a
                Manager's work on their plate, what they handed out, and the rest of their
                departments; an HR Administrator's own tasks and everybody else's. */}
            {layout === 'board' ? (
                <TaskBoard tasks={visible} statuses={columns} renderProps={itemProps} />
            ) : sections.map((section) => {
                const isCollapsed = collapsed.includes(section.title)
                return (
                    <TaskSection
                        key={section.title}
                        title={section.title}
                        subtitle={section.subtitle}
                        count={section.tasks.length}
                        accent={section.accent}
                        collapsed={isCollapsed}
                        onToggle={() => toggleSection(section.title)}
                    >
                        {section.tasks.length === 0 ? (
                            <SectionEmpty
                                action={section.add && creates ? { label: manages ? 'Create a new task' : 'Add a task of your own', onClick: () => setDialogTask(null) } : undefined}
                            >
                                {section.empty}
                            </SectionEmpty>
                        ) : layout === 'list' ? (
                            <TaskList tasks={section.tasks} settings={settings} renderProps={itemProps} />
                        ) : (
                            <CardGrid>
                                {section.tasks.map((task) => <TaskCard key={task.id} {...itemProps(task)} />)}
                                {section.add && <AddCard onClick={() => setDialogTask(null)} personal={!manages} />}
                            </CardGrid>
                        )}
                    </TaskSection>
                )
            })}

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
                currentUserId={userId}
                onClose={() => setDialogTask(undefined)}
                onSaved={(warning) => { setDialogTask(undefined); setSaveWarning(warning ?? null); void refresh() }}
            />
        </Box>
    )
})

export default TasksPage

/** Cards, list or board: three buttons, the current one pressed. */
function LayoutToggle({ value, onChange }: { value: TaskLayout; onChange: (next: TaskLayout) => void }) {
    const options: { value: TaskLayout; label: string; icon: string }[] = [
        { value: 'cards', label: 'Cards', icon: '▦' },
        { value: 'list', label: 'List', icon: '☰' },
        { value: 'board', label: 'Board', icon: '▥' },
    ]
    return (
        <Box role="group" aria-label="Layout" sx={{ display: 'inline-flex', border: '1px solid', borderColor: 'divider', borderRadius: '6px', overflow: 'hidden' }}>
            {options.map((o, i) => {
                const pressed = o.value === value
                return (
                    <Box
                        key={o.value}
                        component="button"
                        type="button"
                        aria-pressed={pressed}
                        title={`${o.label} view`}
                        onClick={() => onChange(o.value)}
                        sx={{
                            display: 'inline-flex', alignItems: 'center', gap: '5px',
                            px: '10px', py: '6px', border: 'none', fontFamily: 'inherit', fontSize: 12, cursor: 'pointer',
                            borderLeft: i === 0 ? 'none' : '1px solid', borderLeftColor: 'divider',
                            bgcolor: pressed ? softBg('primary') : 'background.paper',
                            color: pressed ? 'primary.main' : 'text.secondary', fontWeight: pressed ? 700 : 500,
                            '&:hover': { color: 'primary.main' },
                        }}
                    >
                        <Box component="span" aria-hidden sx={{ fontSize: 13, lineHeight: 1 }}>{o.icon}</Box>
                        {o.label}
                    </Box>
                )
            })}
        </Box>
    )
}

/** The grid's last cell in a group the viewer runs: sized like the cards beside it, not taller. */
function AddCard({ onClick, personal = false }: { onClick: () => void; personal?: boolean }) {
    return (
        <Box
            component="button"
            type="button"
            onClick={onClick}
            sx={{
                bgcolor: 'transparent', border: '2px dashed', borderColor: 'divider',
                borderRadius: '12px', p: '20px', minHeight: 140,
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '6px',
                cursor: 'pointer', fontFamily: 'inherit', textAlign: 'center',
                color: 'text.secondary', transition: 'border-color 0.15s, background-color 0.15s',
                '&:hover': { borderColor: 'primary.main', bgcolor: softBg('primary') },
            }}
        >
            <Box sx={{ fontSize: 22, lineHeight: 1, color: 'primary.main' }}>+</Box>
            <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>{personal ? 'Add a task of your own' : 'Create a new task'}</Box>
            <Box sx={{ fontSize: 12, color: 'text.secondary' }}>
                {personal ? 'Pick a project and track your own work to done' : 'Pick a project, assign the people, track it to done'}
            </Box>
        </Box>
    )
}

function CardGrid({ children }: { children: React.ReactNode }) {
    return (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 320px), 1fr))', gap: '14px' }}>
            {children}
        </Box>
    )
}

/**
 * One of the page's groups; the first, accented, is the viewer's own work. The
 * header folds it away, and the fold is remembered per viewer.
 */
function TaskSection({ title, subtitle, count, accent = false, collapsed, onToggle, children }: {
    title: string
    subtitle: string
    count: number
    accent?: boolean
    collapsed: boolean
    onToggle: () => void
    children: React.ReactNode
}) {
    return (
        <Box component="section" aria-label={title} sx={{ mb: collapsed ? '12px' : '22px' }}>
            <Box
                component="button"
                type="button"
                aria-expanded={!collapsed}
                onClick={onToggle}
                sx={{
                    display: 'flex', alignItems: 'baseline', gap: '10px', flexWrap: 'wrap', width: '100%',
                    mb: collapsed ? 0 : '10px', pl: '10px', py: '2px', pr: 0,
                    border: 'none', borderLeft: '3px solid', borderLeftColor: accent ? 'primary.main' : 'divider',
                    bgcolor: 'transparent', fontFamily: 'inherit', textAlign: 'left', cursor: 'pointer',
                    '&:hover .section-chevron': { color: 'primary.main' },
                }}
            >
                <Box component="span" className="section-chevron" aria-hidden sx={{
                    fontSize: 10, color: 'text.secondary', display: 'inline-block', width: 10,
                    transform: collapsed ? 'rotate(-90deg)' : 'none', transition: 'transform 0.15s',
                }}>▼</Box>
                <Box component="h2" sx={{ m: 0, fontSize: accent ? 16 : 14, fontWeight: 700, color: accent ? 'text.primary' : 'text.secondary' }}>
                    {title}
                </Box>
                <Box data-testid="section-count" sx={{
                    fontSize: 11, fontWeight: 700, px: '8px', py: '1px', borderRadius: '10px',
                    bgcolor: accent ? softBg('primary') : 'action.hover', color: accent ? 'primary.main' : 'text.secondary',
                }}>
                    {count}
                </Box>
                <Box component="span" sx={{ fontSize: 12, color: 'text.secondary' }}>{subtitle}</Box>
            </Box>
            {!collapsed && children}
        </Box>
    )
}

/** An empty group: one line, with the way to fill it when there is one. */
function SectionEmpty({ children, action }: { children: React.ReactNode; action?: { label: string; onClick: () => void } }) {
    return (
        <Box sx={{
            bgcolor: 'background.paper', border: '1px dashed', borderColor: 'divider', borderRadius: '10px',
            px: '16px', py: '12px', display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap',
            color: 'text.secondary', fontSize: 13,
        }}>
            <Box sx={{ flex: 1, minWidth: 0 }}>{children}</Box>
            {action && (
                <Box
                    component="button"
                    type="button"
                    onClick={action.onClick}
                    sx={{
                        bgcolor: 'transparent', color: 'primary.main', border: '1px solid', borderColor: 'primary.main',
                        borderRadius: '6px', px: '12px', py: '5px', fontSize: 12, fontWeight: 600, cursor: 'pointer',
                        fontFamily: 'inherit', whiteSpace: 'nowrap',
                        '&:hover': { bgcolor: softBg('primary') },
                    }}
                >
                    + {action.label}
                </Box>
            )}
        </Box>
    )
}
