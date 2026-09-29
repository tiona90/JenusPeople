import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    Alert, Box, Button, Checkbox, FormControlLabel, Paper, Stack, Switch, TextField, ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material'
import { WORK_TASK_SETTINGS_KEY, getWorkTaskSettings, updateWorkTaskSettings } from '../../lib/api'
import type { FieldRequirement, WorkTaskSettings } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { FIELD_OPTIONS, requirementOf, withRequirement, type TaskField } from '../../lib/task-settings'

const FIELD_LABELS: Record<TaskField, string> = {
    description: 'Description',
    dueDate: 'Due date',
    targetHours: 'Target hours',
    attachments: 'Attachments',
    project: 'Project',
    billable: 'Billable',
}

const FIELD_NOTES: Partial<Record<TaskField, string>> = {
    attachments: 'Required means a task cannot be marked done until a file is attached.',
    project: 'Cannot be hidden: timesheet rows take their project from the task.',
    billable: 'A yes/no answer — asked, or not asked.',
}

/** Mirrors UpdateWorkTaskSettingsValidator, with its messages. */
function settingsError(s: WorkTaskSettings): string | null {
    if (!(s.allowImages || s.allowPdf || s.allowWord || s.allowExcel)) return 'Allow at least one kind of file.'
    if (!Number.isInteger(s.maxAttachmentsPerTask) || s.maxAttachmentsPerTask < 1 || s.maxAttachmentsPerTask > 20)
        return 'A task can be allowed 1 to 20 attachments.'
    if (!Number.isInteger(s.maxAttachmentSizeMb) || s.maxAttachmentSizeMb < 1 || s.maxAttachmentSizeMb > 10)
        return 'The size limit must be 1 to 10 MB.'
    return null
}

/**
 * The System Administrator's Task Settings: which task fields are asked, whether an
 * assignee's Done waits for confirmation, and what files a task may carry. One set for
 * the whole organisation. The System Administrator does not see the tasks themselves.
 */
export default function TaskSettingsPanel() {
    const queryClient = useQueryClient()
    const { data, isLoading, isError } = useQuery({ queryKey: WORK_TASK_SETTINGS_KEY, queryFn: getWorkTaskSettings })
    const [draft, setDraft] = useState<WorkTaskSettings | null>(null)
    useEffect(() => { if (data) setDraft(data) }, [data])

    const save = useMutation({
        mutationFn: updateWorkTaskSettings,
        onSuccess: (saved) => {
            queryClient.setQueryData(WORK_TASK_SETTINGS_KEY, saved)
            void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        },
    })

    if (isLoading || !draft) return isError ? <Alert severity="error">Could not load the task settings.</Alert> : null
    const error = settingsError(draft)
    const set = (patch: Partial<WorkTaskSettings>) => setDraft({ ...draft, ...patch })

    return (
        <Stack spacing={2}>
            <Box>
                <Typography sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary' }}>✅ Task Settings</Typography>
                <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>
                    How tasks work for everyone: which fields are asked, how a task is finished, and what files it may carry. Title and Department are always required.
                </Typography>
            </Box>

            {save.isError && <Alert severity="error">{getApiErrorMessage(save.error, 'The task settings could not be saved.')}</Alert>}
            {save.isSuccess && <Alert severity="success">Task settings saved.</Alert>}

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1.5 }}>Fields</Typography>
                <Stack spacing={1.5}>
                    {(Object.keys(FIELD_OPTIONS) as TaskField[]).map((field) => (
                        <Box key={field} sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
                            <Box sx={{ width: 140, fontSize: 14 }} id={`task-field-${field}`}>{FIELD_LABELS[field]}</Box>
                            <ToggleButtonGroup
                                exclusive
                                size="small"
                                aria-labelledby={`task-field-${field}`}
                                value={requirementOf(draft, field)}
                                onChange={(_, value: FieldRequirement | null) => { if (value) setDraft(withRequirement(draft, field, value)) }}
                            >
                                {FIELD_OPTIONS[field].map((option) => (
                                    <ToggleButton key={option} value={option}>{option}</ToggleButton>
                                ))}
                            </ToggleButtonGroup>
                            {FIELD_NOTES[field] && <Box sx={{ fontSize: 12, color: 'text.secondary' }}>{FIELD_NOTES[field]}</Box>}
                        </Box>
                    ))}
                    <FormControlLabel
                        control={<Switch checked={draft.showPriority} onChange={(e) => set({ showPriority: e.target.checked })} />}
                        label="Show priority"
                    />
                </Stack>
            </Paper>

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1 }}>Completion</Typography>
                <FormControlLabel
                    control={<Switch checked={draft.requireCompletionConfirmation} onChange={(e) => set({ requireCompletionConfirmation: e.target.checked })} />}
                    label="A task somebody else handed you waits for confirmation when marked done"
                />
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    Switching this off closes every task now waiting for confirmation, without emailing anyone.
                </Typography>
            </Paper>

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1.5 }}>Attachments</Typography>
                <Stack direction="row" spacing={2} sx={{ mb: 1.5 }}>
                    <TextField
                        label="Files per task" type="number" size="small"
                        value={draft.maxAttachmentsPerTask}
                        onChange={(e) => set({ maxAttachmentsPerTask: Number(e.target.value) })}
                        slotProps={{ htmlInput: { min: 1, max: 20 } }}
                    />
                    <TextField
                        label="Size limit (MB)" type="number" size="small"
                        value={draft.maxAttachmentSizeMb}
                        onChange={(e) => set({ maxAttachmentSizeMb: Number(e.target.value) })}
                        slotProps={{ htmlInput: { min: 1, max: 10 } }}
                    />
                </Stack>
                <Box>
                    <FormControlLabel control={<Checkbox checked={draft.allowImages} onChange={(e) => set({ allowImages: e.target.checked })} />} label="Images" />
                    <FormControlLabel control={<Checkbox checked={draft.allowPdf} onChange={(e) => set({ allowPdf: e.target.checked })} />} label="PDF" />
                    <FormControlLabel control={<Checkbox checked={draft.allowWord} onChange={(e) => set({ allowWord: e.target.checked })} />} label="Word" />
                    <FormControlLabel control={<Checkbox checked={draft.allowExcel} onChange={(e) => set({ allowExcel: e.target.checked })} />} label="Excel" />
                </Box>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    Limits apply to new uploads. Files already attached stay.
                </Typography>
            </Paper>

            {error && <Typography sx={{ fontSize: 13, color: 'error.main' }}>{error}</Typography>}
            <Box>
                <Button variant="contained" disabled={!!error || save.isPending} onClick={() => save.mutate(draft)}>Save changes</Button>
            </Box>
        </Stack>
    )
}
