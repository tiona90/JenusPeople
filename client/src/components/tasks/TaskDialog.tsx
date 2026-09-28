import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
    Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, FormHelperText, InputLabel,
    MenuItem, Select, Stack, TextField,
} from '@mui/material'
import { createWorkTask, getWorkTaskAssignees, getWorkTaskDepartments, getWorkTaskProjects, updateWorkTask } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { UpsertWorkTaskRequest, WorkTask, WorkTaskPriority } from '../../lib/types'
import { PRIORITY_LABELS } from '../../lib/work-tasks'

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
    const [assigneeId, setAssigneeId] = useState('')
    const [dueDate, setDueDate] = useState('')
    const [priority, setPriority] = useState<WorkTaskPriority>('Normal')

    useEffect(() => {
        if (!open) return
        setTitle(task?.title ?? '')
        setDescription(task?.description ?? '')
        setDepartmentId(task?.departmentId ?? '')
        setProjectId(task?.projectId ?? '')
        setAssigneeId(task?.assigneeId ?? '')
        setDueDate(task?.dueDate?.slice(0, 10) ?? '')
        setPriority(task?.priority ?? 'Normal')
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

    // A department change can leave the chosen assignee outside it. Keep them while
    // the list loads; drop them once it says they are not there. Editing an existing
    // task keeps its assignee even if they have since left — the server only
    // re-checks when the assignee or department changes.
    useEffect(() => {
        if (!assignees.data || assigneeId === '') return
        const unchangedOnEdit = task != null && departmentId === task.departmentId && assigneeId === task.assigneeId
        if (!unchangedOnEdit && !assignees.data.some((a) => a.userId === assigneeId)) setAssigneeId('')
    }, [assignees.data, assigneeId, departmentId, task])

    const save = useMutation({
        mutationFn: (request: UpsertWorkTaskRequest) => (task ? updateWorkTask(task.id, request) : createWorkTask(request)),
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
        assigneeId !== '' &&
        !save.isPending

    const submit = () => {
        if (!canSave) return
        save.mutate({
            title: trimmedTitle,
            description: description.trim() === '' ? null : description.trim(),
            departmentId: departmentId as number,
            projectId: projectId as number,
            assigneeId,
            dueDate: dueDate === '' ? null : dueDate,
            priority,
        })
    }

    // Keep an edited task's own project selectable after it was switched off.
    const projectOptions = [...(projects.data ?? [])]
    if (task?.projectId != null && projectId === task.projectId && !projectOptions.some((p) => p.id === projectId))
        projectOptions.push({ id: task.projectId, name: task.projectName ?? `Project ${task.projectId}`, code: '' })
    const noProjects = departmentId !== '' && projects.data?.length === 0 && projectOptions.length === 0

    // Keep an edited task's current assignee selectable while the list loads, or after they left scope.
    const assigneeOptions = [...(assignees.data ?? [])]
    if (task && assigneeId === task.assigneeId && !assigneeOptions.some((a) => a.userId === assigneeId))
        assigneeOptions.push({ userId: task.assigneeId, displayName: task.assigneeName })

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
                        <InputLabel id="task-assignee-label">Assignee</InputLabel>
                        <Select
                            labelId="task-assignee-label"
                            label="Assignee"
                            value={assigneeOptions.some((a) => a.userId === assigneeId) ? assigneeId : ''}
                            onChange={(e) => setAssigneeId(String(e.target.value))}
                        >
                            {assigneeOptions.map((a) => (
                                <MenuItem key={a.userId} value={a.userId}>{a.displayName}</MenuItem>
                            ))}
                        </Select>
                    </FormControl>
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
