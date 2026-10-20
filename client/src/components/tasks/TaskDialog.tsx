import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueries, useQuery } from '@tanstack/react-query'
import {
    Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, FormHelperText, FormLabel, InputLabel,
    MenuItem, Select, Stack, TextField,
} from '@mui/material'
// From their own modules, not the lib/api index, so a test that mocks the whole index
// (as TasksPage's does) still renders the dialog.
import { getHolidays } from '../../lib/api/holidays'
import { getAppSettings } from '../../lib/api/settings'
import { addWorkTaskAttachment, createWorkTask, getWorkTaskAssignees, getWorkTaskDepartments, getWorkTaskProjects, updateWorkTask, updateWorkTaskStatus } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { describeKinds } from '../../lib/task-attachments'
import { describeEstimate, estimateTargetHours, yearsBetween } from '../../lib/task-target'
import { fieldRequirementError, isShown, needsAttachmentBeforeDone, requirementOf, attachmentLimits } from '../../lib/task-settings'
import { useWorkTaskSettings } from '../../lib/task-settings-query'
import type { UpsertWorkTaskRequest, WorkTask, WorkTaskAttachment, WorkTaskPriority, WorkTaskStatus } from '../../lib/types'
import { PRIORITY_LABELS, SETTABLE_STATUSES, STATUS_LABELS } from '../../lib/work-tasks'
import { STATUS_COLORS } from './statusStyles'
import TaskAttachments, { StagedTaskAttachments } from './TaskAttachments'

const TITLE_MAX = 200
const DESCRIPTION_MAX = 2000
// Mirrors WorkTask.MaxTargetHours on the server.
const TARGET_HOURS_MAX = 9999
// The assignee menu's first item. Picking it empties the selection, and an empty
// selection is sent as-is: the server assigns everyone eligible in the department.
const EVERYONE = '__everyone__'
// Mirrors UpsertWorkTaskRequestValidator.StartAfterDueMessage.
const START_AFTER_DUE = 'The start date cannot be after the due date.'
const ONE_HOUR = 60 * 60 * 1000

/** A target field's text as a whole number in range, null when blank, or 'invalid'. */
function parseTarget(text: string, max: number): number | null | 'invalid' {
    if (text.trim() === '') return null
    const n = Number(text)
    return Number.isInteger(n) && n >= 1 && n <= max ? n : 'invalid'
}

interface Props {
    open: boolean
    /** Null creates; a task edits it (creator only — the page never opens this otherwise). */
    task: WorkTask | null
    /**
     * An Employee's own task: no assignee picker, since the server assigns it to
     * the caller alone whatever the request says.
     */
    personal?: boolean
    /** The signed-in user: listed first in the picker as "Assign to me", and shown as "Me". */
    currentUserId?: string
    onClose: () => void
    /** A warning when the task saved but some of the files picked for it did not attach. */
    onSaved: (warning?: string) => void
}

export default function TaskDialog({ open, task, personal = false, currentUserId, onClose, onSaved }: Props) {
    const settings = useWorkTaskSettings()
    const limits = attachmentLimits(settings)
    const [title, setTitle] = useState('')
    const [description, setDescription] = useState('')
    const [departmentId, setDepartmentId] = useState<number | ''>('')
    const [projectId, setProjectId] = useState<number | ''>('')
    const [projectTypeId, setProjectTypeId] = useState<number | ''>('')
    const [assigneeIds, setAssigneeIds] = useState<string[]>([])
    const [startDate, setStartDate] = useState('')
    const [dueDate, setDueDate] = useState('')
    const [priority, setPriority] = useState<WorkTaskPriority>('Normal')
    const [status, setStatus] = useState<WorkTaskStatus>('ToDo')
    const [targetHours, setTargetHours] = useState('')
    // A saved task's files change on the server as they are picked; a new task's wait here.
    const [attachments, setAttachments] = useState<WorkTaskAttachment[]>([])
    const [stagedFiles, setStagedFiles] = useState<File[]>([])

    useEffect(() => {
        if (!open) return
        setTitle(task?.title ?? '')
        setDescription(task?.description ?? '')
        setDepartmentId(task?.departmentId ?? '')
        setProjectId(task?.projectId ?? '')
        setProjectTypeId(task?.projectTypeId ?? '')
        // HR is never assigned; one left on from before the rule comes off here,
        // since a save that keeps them is refused.
        setAssigneeIds(task?.assignees.filter((a) => !a.isHrAdministrator).map((a) => a.userId) ?? [])
        setStartDate(task?.startDate?.slice(0, 10) ?? '')
        setDueDate(task?.dueDate?.slice(0, 10) ?? '')
        setPriority(task?.priority ?? 'Normal')
        setStatus(task?.status ?? 'ToDo')
        setTargetHours(task?.targetHours != null ? String(task.targetHours) : '')
        setAttachments(task?.attachments ?? [])
        setStagedFiles([])
    }, [open, task])

    // The working week and its public holidays, for the hours the dates come to.
    const holidayYears = yearsBetween(startDate, dueDate)
    const appSettings = useQuery({ queryKey: ['appSettings'], queryFn: getAppSettings, enabled: open && holidayYears.length > 0, staleTime: ONE_HOUR })
    const holidayQueries = useQueries({
        queries: holidayYears.map((year) => ({
            queryKey: ['holidays', year],
            queryFn: () => getHolidays(year),
            enabled: open,
            staleTime: ONE_HOUR,
        })),
    })
    const holidaysLoaded = holidayQueries.every((q) => !q.isPending)
    const holidayKeys = holidayQueries.flatMap((q) => q.data ?? []).map((h) => h.date.slice(0, 10)).join(',')
    const estimate = useMemo(
        () => holidaysLoaded
            ? estimateTargetHours(startDate, dueDate, appSettings.data, new Set(holidayKeys.split(',')), TARGET_HOURS_MAX)
            : null,
        [startDate, dueDate, appSettings.data, holidayKeys, holidaysLoaded],
    )
    // With a start and a due date that come to a figure, the hours are that figure and
    // the field is read-only. Clearing the start date hands the field back, keeping the
    // last figure as a starting point. An edited task's stored hours follow its dates too.
    const hoursFromDates = estimate !== null
    useEffect(() => {
        if (estimate) setTargetHours(String(estimate.hours))
    }, [estimate])

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

    // The type is one the chosen project carries. Dropped once the list says the
    // project does not — except an edited task's own type on its own project, which
    // the server re-checks only when either moves.
    const chosenProject = projects.data?.find((p) => p.id === projectId)
    useEffect(() => {
        if (projectTypeId === '') return
        if (projectId === '') {
            setProjectTypeId('')
            return
        }
        if (!chosenProject) return
        const unchangedOnEdit = task != null && projectId === task.projectId && projectTypeId === task.projectTypeId
        if (!unchangedOnEdit && !(chosenProject.types ?? []).some((t) => t.id === projectTypeId)) setProjectTypeId('')
    }, [chosenProject, projectId, projectTypeId, task])

    const assignees = useQuery({
        queryKey: ['work-tasks', 'assignees', departmentId],
        queryFn: () => getWorkTaskAssignees(departmentId as number),
        enabled: open && !personal && departmentId !== '',
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
        // A new task's files go up once it exists. One that is refused does not undo
        // the task: it is reported, and can be attached again from the card.
        mutationFn: async (request: UpsertWorkTaskRequest): Promise<string | undefined> => {
            if (!task) {
                const created = await createWorkTask(request)
                const failed: string[] = []
                for (const file of stagedFiles) {
                    try {
                        await addWorkTaskAttachment(created.id, file)
                    } catch (error) {
                        failed.push(`${file.name}: ${getApiErrorMessage(error, 'could not be attached.')}`)
                    }
                }
                return failed.length > 0 ? `The task was created, but some files were not attached. ${failed.join(' ')}` : undefined
            }
            await updateWorkTask(task.id, request)
            if (status !== task.status) await updateWorkTaskStatus(task.id, status)
            return undefined
        },
        onSuccess: (warning) => onSaved(warning),
    })

    // The dialog stays mounted between uses, so a refused save from last time
    // would otherwise greet the next, blank form.
    const { reset: resetSave } = save
    useEffect(() => {
        if (open) resetSave()
    }, [open, resetSave])

    const trimmedTitle = title.trim()
    const hours = parseTarget(targetHours, TARGET_HOURS_MAX)
    const startAfterDue = startDate !== '' && dueDate !== '' && startDate > dueDate
    // Mirrors WorkTaskProjectRule.IsTypeMissingAsync: a project classified by any type
    // needs one picked; a project with none leaves nothing to ask.
    const typeRequired = (chosenProject?.types?.length ?? 0) > 0
    const typeMissing = typeRequired && projectTypeId === ''
    const fieldError = fieldRequirementError(settings, {
        description,
        dueDate,
        targetHours: typeof hours === 'number' ? hours : null,
        projectId: projectId === '' ? null : projectId,
    })
    const canSave =
        trimmedTitle.length > 0 &&
        trimmedTitle.length <= TITLE_MAX &&
        description.length <= DESCRIPTION_MAX &&
        departmentId !== '' &&
        fieldError === null &&
        !startAfterDue &&
        !typeMissing &&
        // Empty means everyone in the department, which is only somebody once the list says so.
        (personal || assigneeIds.length > 0 || (assignees.data?.length ?? 0) > 0) &&
        hours !== 'invalid' &&
        !save.isPending

    const submit = () => {
        if (!canSave) return
        save.mutate({
            title: trimmedTitle,
            description: description.trim() === '' ? null : description.trim(),
            departmentId: departmentId as number,
            projectId: projectId === '' ? null : projectId,
            projectTypeId: projectTypeId === '' ? null : projectTypeId,
            assigneeIds: personal ? [] : assigneeIds,
            startDate: startDate === '' ? null : startDate,
            dueDate: dueDate === '' ? null : dueDate,
            targetHours: typeof hours === 'number' ? hours : null,
            priority,
        })
    }

    // One department in scope — an Employee, or a Manager covering only their own —
    // leaves nothing to pick, so the field is read-only. An edited task sitting in a
    // department the caller no longer covers keeps the select, so it shows as it is.
    const onlyDepartment =
        departments.data?.length === 1 && (task == null || task.departmentId === departments.data[0].id)
            ? departments.data[0]
            : null

    // Keep an edited task's own project selectable after it was switched off.
    const projectOptions = [...(projects.data ?? [])]
    if (task?.projectId != null && projectId === task.projectId && !projectOptions.some((p) => p.id === projectId))
        projectOptions.push({ id: task.projectId, name: task.projectName ?? `Project ${task.projectId}`, code: '' })
    const noProjects = departmentId !== '' && projects.data?.length === 0 && projectOptions.length === 0

    // The chosen project's types, plus an edited task's own type if the project has since dropped it.
    const typeOptions = [...(chosenProject?.types ?? [])]
    if (task?.projectTypeId != null && projectTypeId === task.projectTypeId && projectId === task.projectId
        && !typeOptions.some((t) => t.id === projectTypeId))
        typeOptions.push({ id: task.projectTypeId, name: task.projectTypeName ?? `Type ${task.projectTypeId}`, icon: task.projectTypeIcon ?? '' })
    const typeHelper = projectId === ''
        ? 'Choose a project first'
        : chosenProject && typeOptions.length === 0 ? 'This project has no types' : 'Timesheet entries for it use this type'

    // Keep an edited task's own people selectable while the list loads, or after they left scope.
    const assigneeOptions = [...(assignees.data ?? [])]
    for (const existing of task?.assignees ?? [])
        if (assigneeIds.includes(existing.userId) && !assigneeOptions.some((a) => a.userId === existing.userId))
            assigneeOptions.push(existing)
    // The caller first, as themselves rather than by name; everyone else keeps the server's order.
    const mine = assigneeOptions.findIndex((a) => a.userId === currentUserId)
    if (mine > 0) assigneeOptions.unshift(...assigneeOptions.splice(mine, 1))
    const nameOf = (id: string) =>
        id === currentUserId ? 'Me' : assigneeOptions.find((a) => a.userId === id)?.displayName ?? id
    const everyone = departmentId !== '' && assigneeIds.length === 0
    const nobodyToAssign = everyone && assignees.data?.length === 0

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{task ? 'Edit task' : 'New project'}</DialogTitle>
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
                    {isShown(settings, 'description') && <TextField
                        label="Description"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        multiline
                        minRows={3}
                        required={requirementOf(settings, 'description') === 'Required'}
                        helperText={`${description.length}/${DESCRIPTION_MAX}`}
                        error={description.length > DESCRIPTION_MAX}
                    />}
                    {isShown(settings, 'attachments') && <Box>
                        <FormLabel sx={{ fontSize: 13 }}>Attachments</FormLabel>
                        <Box sx={{ mt: 0.5 }}>
                            {task ? (
                                <TaskAttachments taskId={task.id} attachments={attachments} canAttach onChanged={(saved) => setAttachments(saved.attachments ?? [])} limits={limits} />
                            ) : (
                                <StagedTaskAttachments files={stagedFiles} onChange={setStagedFiles} limits={limits} />
                            )}
                        </Box>
                        <FormHelperText>{`${describeKinds(limits)}, up to ${settings.maxAttachmentSizeMb}MB each`}</FormHelperText>
                    </Box>}
                    {onlyDepartment ? (
                        <TextField
                            label="Department"
                            value={onlyDepartment.name}
                            required
                            helperText="Tasks are filed under your department"
                            slotProps={{ input: { readOnly: true } }}
                        />
                    ) : <FormControl required>
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
                    </FormControl>}
                    {isShown(settings, 'project') && <FormControl required={requirementOf(settings, 'project') === 'Required'} disabled={departmentId === ''} error={noProjects}>
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
                            {requirementOf(settings, 'project') === 'Optional' && <MenuItem value="">No project</MenuItem>}
                            {projectOptions.map((p) => (
                                <MenuItem key={p.id} value={p.id}>{p.name}</MenuItem>
                            ))}
                        </Select>
                        {noProjects && <FormHelperText>No active projects in this department</FormHelperText>}
                    </FormControl>}
                    {isShown(settings, 'project') && <FormControl required={typeRequired} disabled={projectId === '' || typeOptions.length === 0}>
                        <InputLabel id="task-type-label">Type</InputLabel>
                        <Select
                            labelId="task-type-label"
                            label="Type"
                            value={typeOptions.some((t) => t.id === projectTypeId) ? projectTypeId : ''}
                            onChange={(e) => {
                                const value = String(e.target.value)
                                setProjectTypeId(value === '' ? '' : Number(value))
                            }}
                        >
                            {!typeRequired && <MenuItem value=""><em>No type</em></MenuItem>}
                            {typeOptions.map((t) => (
                                <MenuItem key={t.id} value={t.id}>{t.icon ? `${t.icon} ${t.name}` : t.name}</MenuItem>
                            ))}
                        </Select>
                        <FormHelperText>{typeHelper}</FormHelperText>
                    </FormControl>}
                    {!personal && <FormControl disabled={departmentId === ''} error={nobodyToAssign}>
                        <InputLabel id="task-assignee-label" shrink={everyone || assigneeIds.length > 0}>Assignees</InputLabel>
                        <Select<string[]>
                            multiple
                            displayEmpty={everyone}
                            notched={everyone || assigneeIds.length > 0}
                            labelId="task-assignee-label"
                            label="Assignees"
                            value={assigneeIds.filter((id) => assigneeOptions.some((a) => a.userId === id))}
                            onChange={(e) => {
                                const value = typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value
                                setAssigneeIds(value.includes(EVERYONE) ? [] : value)
                            }}
                            renderValue={(selected) =>
                                selected.length === 0 ? (
                                    <Chip size="small" label="Everyone in the department" />
                                ) : (
                                    <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                                        {selected.map((id) => <Chip key={id} size="small" label={nameOf(id)} />)}
                                    </Stack>
                                )
                            }
                        >
                            <MenuItem value={EVERYONE} divider>Everyone in the department</MenuItem>
                            {assigneeOptions.map((a) => (
                                <MenuItem key={a.userId} value={a.userId}>{a.userId === currentUserId ? 'Assign to me' : a.displayName}</MenuItem>
                            ))}
                        </Select>
                        {everyone && (
                            <FormHelperText>
                                {nobodyToAssign
                                    ? 'Nobody in this department can be assigned a task'
                                    : assignees.data
                                      ? `All ${assignees.data.length} Managers and Employees in the department as of saving. Pick names to narrow it.`
                                      : 'Everyone in the department as of saving. Pick names to narrow it.'}
                            </FormHelperText>
                        )}
                    </FormControl>}
                    {task && (
                        <FormControl>
                            <InputLabel id="task-status-label">Status</InputLabel>
                            <Select<WorkTaskStatus>
                                labelId="task-status-label"
                                label="Status"
                                value={status}
                                onChange={(e) => setStatus(e.target.value as WorkTaskStatus)}
                                disabled={task.status === 'AwaitingConfirmation'}
                            >
                                {/* Awaiting confirmation is reached by marking the task done, never picked;
                                    it is listed, disabled, only so a waiting task's select is not blank. */}
                                {(task.status === 'AwaitingConfirmation' ? [...SETTABLE_STATUSES, task.status] : SETTABLE_STATUSES).map((s) => (
                                    <MenuItem
                                        key={s}
                                        value={s}
                                        disabled={s === 'AwaitingConfirmation' || (s === 'Done' && needsAttachmentBeforeDone(settings, attachments.length))}
                                        sx={{ gap: '10px' }}
                                    >
                                        <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: STATUS_COLORS[s].dot, display: 'inline-block', mr: '8px' }} />
                                        {STATUS_LABELS[s]}
                                    </MenuItem>
                                ))}
                            </Select>
                            {/* Confirm, Send back (which needs a reason) and Withdraw are the card's; a status
                                picked here would save the edit and then be refused. */}
                            {task.status === 'AwaitingConfirmation' && (
                                <FormHelperText>Waiting for confirmation. Confirm it or send it back from its card.</FormHelperText>
                            )}
                        </FormControl>
                    )}
                    {/* The start date is the due date's companion: shown and hidden with it, never required. */}
                    {isShown(settings, 'dueDate') && <Stack direction="row" spacing={2}>
                        <TextField
                            label="Start date"
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            error={startAfterDue}
                            helperText={startAfterDue ? START_AFTER_DUE : undefined}
                            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: dueDate || undefined } }}
                            sx={{ flex: 1 }}
                        />
                        <TextField
                            label="Due date"
                            type="date"
                            value={dueDate}
                            onChange={(e) => setDueDate(e.target.value)}
                            required={requirementOf(settings, 'dueDate') === 'Required'}
                            error={startAfterDue}
                            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: startDate || undefined } }}
                            sx={{ flex: 1 }}
                        />
                    </Stack>}
                    <Stack direction="row" spacing={2}>
                        {isShown(settings, 'targetHours') && <TextField
                            label="Target hours"
                            type="number"
                            value={targetHours}
                            onChange={(e) => setTargetHours(e.target.value)}
                            required={requirementOf(settings, 'targetHours') === 'Required'}
                            error={hours === 'invalid'}
                            helperText={
                                hours === 'invalid'
                                    ? `1 to ${TARGET_HOURS_MAX}`
                                    : estimate
                                      ? `${describeEstimate(estimate)} — from the dates`
                                      : 'Estimated effort'
                            }
                            slotProps={{
                                input: { readOnly: hoursFromDates },
                                htmlInput: { min: 1, max: TARGET_HOURS_MAX, step: 1 },
                            }}
                            sx={{ flex: 1 }}
                        />}
                        {isShown(settings, 'priority') && <FormControl sx={{ flex: 1 }}>
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
                        </FormControl>}
                    </Stack>
                    {fieldError && trimmedTitle.length > 0 && <FormHelperText error>{fieldError}</FormHelperText>}
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
