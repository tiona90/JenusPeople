import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
    Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, FormHelperText, InputLabel,
    MenuItem, Select, Stack, TextField,
} from '@mui/material'
import { createWorkTask, getWorkTaskAssignees, getWorkTaskDepartments, getWorkTaskProjects, updateWorkTask, updateWorkTaskStatus } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { UpsertWorkTaskRequest, WorkTask, WorkTaskPriority, WorkTaskStatus } from '../../lib/types'
import { PRIORITY_LABELS, STATUS_LABELS } from '../../lib/work-tasks'
import { STATUS_COLORS } from './statusStyles'

const TITLE_MAX = 200
const DESCRIPTION_MAX = 2000

interface Props {
    open: boolean
    /** Null creates; a task edits it (creator only — the page never opens this otherwise). */
    task: WorkTask | null
    onClose: () => void
    onSaved: () => void
}

export default function TaskDialog({ open, task, onClose, onSaved }: Props) {
    const [title, setTitle] = useState('')
    const [description, setDescription] = useState('')
    const [departmentId, setDepartmentId] = useState<number | ''>('')
    const [projectId, setProjectId] = useState<number | ''>('')
    const [assigneeIds, setAssigneeIds] = useState<string[]>([])
    const [dueDate, setDueDate] = useState('')
    const [priority, setPriority] = useState<WorkTaskPriority>('Normal')
    const [status, setStatus] = useState<WorkTaskStatus>('ToDo')

    useEffect(() => {
        if (!open) return
        setTitle(task?.title ?? '')
        setDescription(task?.description ?? '')
        setDepartmentId(task?.departmentId ?? '')
        setProjectId(task?.projectId ?? '')
        setAssigneeIds(task?.assignees.map((a) => a.userId) ?? [])
        setDueDate(task?.dueDate?.slice(0, 10) ?? '')
        setPriority(task?.priority ?? 'Normal')
        setStatus(task?.status ?? 'ToDo')
    }, [open, task])

    const departments = useQuery({ queryKey: ['work-tasks', 'departments'], queryFn: getWorkTaskDepartments, enabled: open })

    // One department in scope: nothing to choose.
    useEffect(() => {
        if (open && !task && departmentId === '' && departments.data?.length === 1) setDepartmentId(departments.data[0].id)
    }, [open, task, departmentId, departments.data])

    const projects = useQuery({
        queryKey: ['work-tasks', 'projects', departmentId],
        queryFn: () => getWorkTaskProjects(departmentId as number),
        enabled: open && departmentId !== '',
    })

    // Same shape as the assignee below: a project outside the chosen department is
    // dropped once the list says so, but an edited task keeps its own project while
    // its department is unchanged — the server re-checks only when either moves.
    useEffect(() => {
        if (!projects.data || projectId === '') return
        const unchangedOnEdit = task != null && departmentId === task.departmentId && projectId === task.projectId
        if (!unchangedOnEdit && !projects.data.some((p) => p.id === projectId)) setProjectId('')
    }, [projects.data, projectId, departmentId, task])

    const assignees = useQuery({
        queryKey: ['work-tasks', 'assignees', departmentId],
        queryFn: () => getWorkTaskAssignees(departmentId as number),
        enabled: open && departmentId !== '',
    })

    // A department change can leave some of the chosen people outside it. Keep
    // them while the list loads; drop only those it says are not there. Editing a
    // task in its own department keeps the people already on it even if they have
    // since left — the server checks only the newcomers unless the department moves.
    useEffect(() => {
        if (!assignees.data || assigneeIds.length === 0) return
        const listed = new Set(assignees.data.map((a) => a.userId))
        const kept = new Set(task != null && departmentId === task.departmentId ? task.assignees.map((a) => a.userId) : [])
        const next = assigneeIds.filter((id) => listed.has(id) || kept.has(id))
        if (next.length !== assigneeIds.length) setAssigneeIds(next)
    }, [assignees.data, assigneeIds, departmentId, task])

    const save = useMutation({
        // Status has its own endpoint (an assignee may move it without editing
        // anything else), so an edit that changes it is two calls: the details,
        // then the status — in that order, so a refused edit moves nothing.
        mutationFn: async (request: UpsertWorkTaskRequest) => {
            if (!task) return createWorkTask(request)
            const saved = await updateWorkTask(task.id, request)
            return status !== task.status ? updateWorkTaskStatus(task.id, status) : saved
        },
        onSuccess: onSaved,
    })

    // The dialog stays mounted between uses, so a refused save from last time
    // would otherwise greet the next, blank form.
    const { reset: resetSave } = save
    useEffect(() => {
        if (open) resetSave()
    }, [open, resetSave])

    const trimmedTitle = title.trim()
    const canSave =
        trimmedTitle.length > 0 &&
        trimmedTitle.length <= TITLE_MAX &&
        description.length <= DESCRIPTION_MAX &&
        departmentId !== '' &&
        projectId !== '' &&
        assigneeIds.length > 0 &&
        !save.isPending

    const submit = () => {
        if (!canSave) return
        save.mutate({
            title: trimmedTitle,
            description: description.trim() === '' ? null : description.trim(),
            departmentId: departmentId as number,
            projectId: projectId as number,
            assigneeIds,
            dueDate: dueDate === '' ? null : dueDate,
            priority,
        })
    }

    // Keep an edited task's own project selectable after it was switched off.
    const projectOptions = [...(projects.data ?? [])]
    if (task?.projectId != null && projectId === task.projectId && !projectOptions.some((p) => p.id === projectId))
        projectOptions.push({ id: task.projectId, name: task.projectName ?? `Project ${task.projectId}`, code: '' })
    const noProjects = departmentId !== '' && projects.data?.length === 0 && projectOptions.length === 0

    // Keep an edited task's own people selectable while the list loads, or after they left scope.
    const assigneeOptions = [...(assignees.data ?? [])]
    for (const existing of task?.assignees ?? [])
        if (assigneeIds.includes(existing.userId) && !assigneeOptions.some((a) => a.userId === existing.userId))
            assigneeOptions.push(existing)
    const nameOf = (id: string) => assigneeOptions.find((a) => a.userId === id)?.displayName ?? id

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{task ? 'Edit task' : 'New task'}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ mt: 1 }}>
                    {save.isError && <Alert severity="error">{getApiErrorMessage(save.error, 'The task could not be saved.')}</Alert>}
                    <TextField
                        label="Title"
                        value={title}
                        onChange={(e) => setTitle(e.target.value)}
                        required
                        slotProps={{ htmlInput: { maxLength: TITLE_MAX } }}
                    />
                    <TextField
                        label="Description"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        multiline
                        minRows={3}
                        helperText={`${description.length}/${DESCRIPTION_MAX}`}
                        error={description.length > DESCRIPTION_MAX}
                    />
                    <FormControl required>
                        <InputLabel id="task-department-label">Department</InputLabel>
                        <Select
                            labelId="task-department-label"
                            label="Department"
                            value={departmentId}
                            onChange={(e) => {
                                const value = String(e.target.value)
                                setDepartmentId(value === '' ? '' : Number(value))
                            }}
                        >
                            {(departments.data ?? []).map((d) => (
                                <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>
                            ))}
                        </Select>
                    </FormControl>
                    <FormControl required disabled={departmentId === ''} error={noProjects}>
                        <InputLabel id="task-project-label">Project</InputLabel>
                        <Select
                            labelId="task-project-label"
                            label="Project"
                            value={projectOptions.some((p) => p.id === projectId) ? projectId : ''}
                            onChange={(e) => {
                                const value = String(e.target.value)
                                setProjectId(value === '' ? '' : Number(value))
                            }}
                        >
                            {projectOptions.map((p) => (
                                <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>
                            ))}
                        </Select>
                        {noProjects && <FormHelperText>No active projects in this department</FormHelperText>}
                    </FormControl>
                    <FormControl required disabled={departmentId === ''}>
                        <InputLabel id="task-assignee-label">Assignees</InputLabel>
                        <Select<string[]>
                            multiple
                            labelId="task-assignee-label"
                            label="Assignees"
                            value={assigneeIds.filter((id) => assigneeOptions.some((a) => a.userId === id))}
                            onChange={(e) => {
                                const value = e.target.value
                                setAssigneeIds(typeof value === 'string' ? value.split(',') : value)
                            }}
                            renderValue={(selected) => (
                                <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                                    {selected.map((id) => <Chip key={id} size="small" label={nameOf(id)} />)}
                                </Stack>
                            )}
                        >
                            {assigneeOptions.map((a) => (
                                <MenuItem key={a.userId} value={a.userId}>{a.displayName}</MenuItem>
                            ))}
                        </Select>
                    </FormControl>
                    {task && (
                        <FormControl>
                            <InputLabel id="task-status-label">Status</InputLabel>
                            <Select<WorkTaskStatus>
                                labelId="task-status-label"
                                label="Status"
                                value={status}
                                onChange={(e) => setStatus(e.target.value as WorkTaskStatus)}
                            >
                                {(Object.keys(STATUS_LABELS) as WorkTaskStatus[]).map((s) => (
                                    <MenuItem key={s} value={s} sx={{ gap: '10px' }}>
                                        <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: STATUS_COLORS[s].dot, display: 'inline-block', mr: '8px' }} />
                                        {STATUS_LABELS[s]}
                                    </MenuItem>
                                ))}
                            </Select>
                        </FormControl>
                    )}
                    <Stack direction="row" spacing={2}>
                        <TextField
                            label="Due date"
                            type="date"
                            value={dueDate}
                            onChange={(e) => setDueDate(e.target.value)}
                            slotProps={{ inputLabel: { shrink: true } }}
                            sx={{ flex: 1 }}
                        />
                        <FormControl sx={{ flex: 1 }}>
                            <InputLabel id="task-priority-label">Priority</InputLabel>
                            <Select
                                labelId="task-priority-label"
                                label="Priority"
                                value={priority}
                                onChange={(e) => setPriority(e.target.value as WorkTaskPriority)}
                            >
                                {(Object.keys(PRIORITY_LABELS) as WorkTaskPriority[]).map((p) => (
                                    <MenuItem key={p} value={p}>{PRIORITY_LABELS[p]}</MenuItem>
                                ))}
                            </Select>
                        </FormControl>
                    </Stack>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Cancel</Button>
                <Button variant="contained" onClick={submit} disabled={!canSave}>
                    {task ? 'Save changes' : 'Create task'}
                </Button>
            </DialogActions>
        </Dialog>
    )
}
