import { useMemo, useState } from 'react'
import { observer } from 'mobx-react-lite'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    Alert, Box, Button, Chip, CircularProgress, FormControl, IconButton, InputLabel, Menu, MenuItem,
    Paper, Select, Stack, Tab, Tabs, Typography,
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import MoreVertRoundedIcon from '@mui/icons-material/MoreVertRounded'
import { deleteWorkTask, getWorkTaskDepartments, getWorkTasks, updateWorkTaskStatus } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import type { WorkTask, WorkTaskStatus } from '../../lib/types'
import {
    PRIORITY_LABELS, STATUS_LABELS, filterTasks, isOverdue, openCount, todayIso,
    type StatusFilter, type TaskTab,
} from '../../lib/work-tasks'
import TaskDialog from './TaskDialog'

const TABS: { value: TaskTab; label: string; empty: string }[] = [
    { value: 'assigned', label: 'Assigned to me', empty: 'Nothing assigned to you.' },
    { value: 'created', label: 'Created by me', empty: "You haven't created any tasks." },
    { value: 'all', label: 'All in my departments', empty: 'No tasks in your departments.' },
]

const PRIORITY_COLOR = { Low: 'default', Normal: 'info', High: 'error' } as const

function formatDate(iso: string) {
    const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

const TasksPage = observer(function TasksPage() {
    const { authStore } = useStore()
    const userId = authStore.user?.id ?? ''
    const queryClient = useQueryClient()

    const [tab, setTab] = useState<TaskTab>('assigned')
    const [status, setStatus] = useState<StatusFilter>('open')
    const [departmentId, setDepartmentId] = useState<number | null>(null)
    const [dialogTask, setDialogTask] = useState<WorkTask | null | undefined>(undefined) // undefined = closed
    const [menu, setMenu] = useState<{ anchor: HTMLElement; task: WorkTask } | null>(null)

    const tasks = useQuery({ queryKey: ['work-tasks'], queryFn: getWorkTasks })
    const departments = useQuery({ queryKey: ['work-tasks', 'departments'], queryFn: getWorkTaskDepartments })

    const refresh = () => queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
    const moveStatus = useMutation({
        mutationFn: ({ id, next }: { id: number; next: WorkTaskStatus }) => updateWorkTaskStatus(id, next),
        onSuccess: refresh,
    })
    const remove = useMutation({ mutationFn: deleteWorkTask, onSuccess: refresh })

    const today = todayIso()
    const all = tasks.data ?? []
    const visible = useMemo(
        () => filterTasks(tasks.data ?? [], { tab, status, departmentId, userId }),
        [tasks.data, tab, status, departmentId, userId],
    )
    const mutationError = moveStatus.error ?? remove.error

    return (
        <Box sx={{ p: 3, maxWidth: 1100, mx: 'auto' }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
                <Typography variant="h5" fontWeight={700}>Tasks</Typography>
                <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDialogTask(null)}>
                    New task
                </Button>
            </Stack>

            <Tabs value={tab} onChange={(_, v: TaskTab) => setTab(v)} sx={{ mb: 2 }}>
                {TABS.map((t) => (
                    <Tab
                        key={t.value}
                        value={t.value}
                        label={<span>{t.label} <Chip size="small" label={openCount(all, t.value, userId)} sx={{ ml: 0.5 }} /></span>}
                    />
                ))}
            </Tabs>

            <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
                <FormControl size="small" sx={{ minWidth: 160 }}>
                    <InputLabel id="task-status-filter">Show</InputLabel>
                    <Select labelId="task-status-filter" label="Show" value={status} onChange={(e) => setStatus(e.target.value as StatusFilter)}>
                        <MenuItem value="open">Open</MenuItem>
                        {(Object.keys(STATUS_LABELS) as WorkTaskStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>{STATUS_LABELS[s]}</MenuItem>
                        ))}
                        <MenuItem value="any">Everything</MenuItem>
                    </Select>
                </FormControl>
                {(departments.data?.length ?? 0) > 1 && (
                    <FormControl size="small" sx={{ minWidth: 180 }}>
                        <InputLabel id="task-department-filter">Department</InputLabel>
                        <Select
                            labelId="task-department-filter"
                            label="Department"
                            value={departmentId ?? ''}
                            onChange={(e) => {
                                const value = String(e.target.value)
                                setDepartmentId(value === '' ? null : Number(value))
                            }}
                        >
                            <MenuItem value="">All departments</MenuItem>
                            {departments.data!.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                        </Select>
                    </FormControl>
                )}
            </Stack>

            {mutationError && <Alert severity="error" sx={{ mb: 2 }}>{getApiErrorMessage(mutationError, 'The task could not be updated.')}</Alert>}

            {tasks.isLoading ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}><CircularProgress /></Box>
            ) : tasks.isError ? (
                <Alert severity="error">{getApiErrorMessage(tasks.error, 'Tasks could not be loaded.')}</Alert>
            ) : visible.length === 0 ? (
                <Paper variant="outlined" sx={{ p: 4, textAlign: 'center', color: 'text.secondary' }}>
                    {TABS.find((t) => t.value === tab)!.empty}
                </Paper>
            ) : (
                <Stack spacing={1}>
                    {visible.map((task) => {
                        const overdue = isOverdue(task, today)
                        return (
                            <Paper key={task.id} variant="outlined" data-testid="task-row" sx={{ p: 2 }}>
                                <Stack direction="row" spacing={2} alignItems="center">
                                    <Box sx={{ flex: 1, minWidth: 0 }}>
                                        <Typography fontWeight={600} noWrap>{task.title}</Typography>
                                        <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.5, flexWrap: 'wrap', color: 'text.secondary', fontSize: 13 }}>
                                            <Chip size="small" label={task.departmentName} />
                                            {task.projectName && <Chip size="small" variant="outlined" label={task.projectName} />}
                                            <Chip size="small" color={PRIORITY_COLOR[task.priority]} label={PRIORITY_LABELS[task.priority]} />
                                            <span>{task.assigneeName}</span>
                                            <span>· from {task.createdByName}</span>
                                            {task.dueDate && (
                                                <Box component="span" sx={{ color: overdue ? 'error.main' : undefined, fontWeight: overdue ? 600 : undefined }}>
                                                    · Due {formatDate(task.dueDate)}{overdue ? ' · Overdue' : ''}
                                                </Box>
                                            )}
                                        </Stack>
                                        {task.description && (
                                            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, whiteSpace: 'pre-wrap' }}>
                                                {task.description}
                                            </Typography>
                                        )}
                                    </Box>
                                    <FormControl size="small" sx={{ minWidth: 150 }}>
                                        <Select
                                            value={task.status}
                                            disabled={!task.canChangeStatus || moveStatus.isPending}
                                            onChange={(e) => moveStatus.mutate({ id: task.id, next: e.target.value as WorkTaskStatus })}
                                            inputProps={{ 'aria-label': 'Status' }}
                                            SelectDisplayProps={{ 'aria-label': 'Status' } as React.HTMLAttributes<HTMLDivElement>}
                                        >
                                            {(Object.keys(STATUS_LABELS) as WorkTaskStatus[]).map((s) => (
                                                <MenuItem key={s} value={s}>{STATUS_LABELS[s]}</MenuItem>
                                            ))}
                                        </Select>
                                    </FormControl>
                                    {task.canEdit && (
                                        <IconButton aria-label="Task actions" onClick={(e) => setMenu({ anchor: e.currentTarget, task })}>
                                            <MoreVertRoundedIcon />
                                        </IconButton>
                                    )}
                                </Stack>
                            </Paper>
                        )
                    })}
                </Stack>
            )}

            <Menu open={menu != null} anchorEl={menu?.anchor} onClose={() => setMenu(null)}>
                <MenuItem onClick={() => { setDialogTask(menu!.task); setMenu(null) }}>Edit</MenuItem>
                <MenuItem
                    sx={{ color: 'error.main' }}
                    onClick={() => {
                        const t = menu!.task
                        setMenu(null)
                        if (window.confirm(`Delete "${t.title}"?`)) remove.mutate(t.id)
                    }}
                >
                    Delete
                </MenuItem>
            </Menu>

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
