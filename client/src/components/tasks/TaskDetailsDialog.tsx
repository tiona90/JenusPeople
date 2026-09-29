import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import { CODE_COLORS, avatarBg, initials } from '../../lib/card-kit'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTask } from '../../lib/types'
import { PRIORITY_LABELS, STATUS_LABELS, describeTaskProgress, formatTaskDate, isOpenTask, overdueDays } from '../../lib/work-tasks'
import { SectionLabel } from '../ui/CardKit'
import TaskAttachments from './TaskAttachments'
import { PRIORITY_COLORS, STATUS_COLORS } from './statusStyles'

function Fact({ label, value, sub, danger }: { label: string; value: string; sub?: string; danger?: boolean }) {
    return (
        <Box sx={{ flex: 1, minWidth: 110 }}>
            <SectionLabel>{label}</SectionLabel>
            <Box sx={{ fontSize: 14, fontWeight: 700, mt: '2px', color: danger ? 'error.main' : 'text.primary' }}>{value}</Box>
            {sub && <Box sx={{ fontSize: 11, color: 'text.secondary' }}>{sub}</Box>}
        </Box>
    )
}

/**
 * Everything about one task that the card only summarises: the whole description,
 * every assignee by name, and the files to open or download. Whoever may edit the
 * task attaches and removes files here too, and can go on to Edit.
 */
export default function TaskDetailsDialog({ task, today, onClose, onEdit }: {
    /** Null closes the dialog. */
    task: WorkTask | null
    today: string
    onClose: () => void
    onEdit: (task: WorkTask) => void
}) {
    return (
        <Dialog open={task != null} onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="task-details-title">
            {task && <Details task={task} today={today} onClose={onClose} onEdit={onEdit} />}
        </Dialog>
    )
}

function Details({ task, today, onClose, onEdit }: {
    task: WorkTask
    today: string
    onClose: () => void
    onEdit: (task: WorkTask) => void
}) {
    const closed = !isOpenTask(task)
    const late = overdueDays(task, today)
    const progress = describeTaskProgress(task.targetHours, task.loggedHours)
    const status = STATUS_COLORS[task.status]
    const priority = PRIORITY_COLORS[task.priority]
    const codeColor = closed ? 'text.disabled' : (CODE_COLORS[task.projectColorKey ?? ''] ?? CODE_COLORS.p1)
    const attachments = task.attachments ?? []

    return (
        <>
            <DialogContent sx={{ pt: 3 }}>
                {/* Header */}
                <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: '12px', alignItems: 'flex-start' }}>
                    <Box sx={{ minWidth: 0 }}>
                        {task.projectCode && (
                            <Box sx={{
                                display: 'inline-block', bgcolor: codeColor, color: '#fff',
                                fontSize: 11, fontWeight: 700, px: '8px', py: '3px', borderRadius: '6px', mb: '6px',
                            }}>{task.projectCode}</Box>
                        )}
                        <Box component="h2" id="task-details-title" sx={{ m: 0, fontSize: 20, fontWeight: 700, lineHeight: 1.3, wordBreak: 'break-word' }}>
                            {task.title}
                        </Box>
                        <Box sx={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap', mt: '8px' }}>
                            <Box sx={{ fontSize: 11, px: '8px', py: '2px', borderRadius: '10px', bgcolor: 'action.hover', color: 'text.secondary' }}>
                                {task.departmentName}
                            </Box>
                            {task.isBillable != null && (
                                <Box sx={{
                                    fontSize: 11, px: '8px', py: '2px', borderRadius: '10px', fontWeight: 600,
                                    bgcolor: task.isBillable ? softBg('success') : 'action.hover',
                                    color: task.isBillable ? 'success.dark' : 'text.secondary',
                                }}>{task.isBillable ? 'Billable' : 'Non-billable'}</Box>
                            )}
                            {task.projectName && <Box sx={{ fontSize: 12, color: 'text.secondary' }}>{task.projectName}</Box>}
                        </Box>
                        <Box sx={{ fontSize: 12, color: 'text.secondary', mt: '8px' }}>
                            From: <Box component="strong" sx={{ color: 'text.primary' }}>{task.createdByName}</Box>
                        </Box>
                    </Box>
                    <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '6px', flexShrink: 0 }}>
                        <Box sx={{
                            display: 'inline-flex', alignItems: 'center', gap: '4px', px: '10px', py: '4px', borderRadius: '12px',
                            fontSize: 11, fontWeight: 600, bgcolor: status.bg, color: status.fg, whiteSpace: 'nowrap',
                        }}>
                            <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: status.dot }} />
                            {STATUS_LABELS[task.status]}
                        </Box>
                        <Box sx={{
                            px: '8px', py: '2px', borderRadius: '10px', fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap',
                            bgcolor: priority.bg, color: priority.fg,
                        }}>{PRIORITY_LABELS[task.priority]} priority</Box>
                    </Box>
                </Box>

                {/* Facts */}
                <Box sx={{ display: 'flex', gap: '16px', flexWrap: 'wrap', mt: 3, p: '12px 14px', bgcolor: 'action.hover', borderRadius: '8px' }}>
                    <Fact
                        label="Due"
                        value={task.dueDate ? formatTaskDate(task.dueDate) : '—'}
                        sub={task.dueDate ? (late > 0 ? `overdue by ${late} ${late === 1 ? 'day' : 'days'}` : closed ? 'closed' : 'on track') : 'no date set'}
                        danger={late > 0}
                    />
                    <Fact
                        label="Target"
                        value={task.targetHours != null ? `${task.targetHours}h` : '—'}
                        sub={task.targetHours == null && progress.text === 'nothing logged' ? 'no target set' : progress.text}
                        danger={progress.over}
                    />
                    <Fact
                        label={task.status === 'Done' && task.completedAtUtc ? 'Completed' : 'Created'}
                        value={formatTaskDate(task.status === 'Done' && task.completedAtUtc ? task.completedAtUtc : task.createdAtUtc)}
                    />
                </Box>

                {/* Description */}
                <Box sx={{ mt: 3 }}>
                    <SectionLabel>Description</SectionLabel>
                    <Box sx={{
                        mt: '6px', fontSize: 13, lineHeight: 1.6, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                        color: task.description ? 'text.primary' : 'text.disabled', fontStyle: task.description ? 'normal' : 'italic',
                    }}>
                        {task.description || 'No description'}
                    </Box>
                </Box>

                {/* Attachments */}
                <Box sx={{ mt: 3 }}>
                    <SectionLabel>Attachments{attachments.length > 0 ? ` (${attachments.length})` : ''}</SectionLabel>
                    <Box sx={{ mt: '6px' }}>
                        {attachments.length === 0 && !task.canEdit ? (
                            <Box sx={{ fontSize: 13, color: 'text.disabled', fontStyle: 'italic' }}>No attachments</Box>
                        ) : (
                            <TaskAttachments taskId={task.id} attachments={attachments} canAttach={task.canEdit} downloadable />
                        )}
                    </Box>
                </Box>

                {/* Assignees */}
                <Box sx={{ mt: 3 }}>
                    <SectionLabel>Assignees ({task.assignees.length})</SectionLabel>
                    <Box sx={{ display: 'flex', gap: '8px', flexWrap: 'wrap', mt: '8px' }}>
                        {task.assignees.map((a) => (
                            <Box key={a.userId} sx={{
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                border: '1px solid', borderColor: 'divider', borderRadius: '16px', pl: '3px', pr: '10px', py: '3px',
                            }}>
                                <Box sx={{
                                    width: 24, height: 24, borderRadius: '50%', bgcolor: avatarBg(a.displayName || a.userId), color: '#fff',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 10, fontWeight: 600,
                                }}>{initials(a.displayName)}</Box>
                                <Box sx={{ fontSize: 12 }}>{a.displayName}</Box>
                            </Box>
                        ))}
                    </Box>
                </Box>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Close</Button>
                {task.canEdit && <Button variant="contained" onClick={() => onEdit(task)}>Edit task</Button>}
            </DialogActions>
        </>
    )
}
