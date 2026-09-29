import Box from '@mui/material/Box'
import type { WorkTaskSettings } from '../../lib/api/work-task-settings'
import { isShown } from '../../lib/task-settings'
import type { WorkTask } from '../../lib/types'
import { formatTaskDate as formatDate, taskFacts } from '../../lib/work-tasks'
import { Assignees, PriorityChip, ProjectBadge, StatusPill, TaskActions, TaskNotices, type TaskItemProps } from './TaskCard'

/**
 * The dense view: one row per task, for scanning many at once. The same facts and
 * moves as the card (`TaskCard`'s pieces), minus the description, which the details
 * dialog holds. The table scrolls sideways inside its own frame on a narrow screen.
 */
export default function TaskList({ tasks, settings, renderProps }: {
    tasks: readonly WorkTask[]
    settings: WorkTaskSettings
    /** Everything a row needs apart from the task, the same callbacks a card gets. */
    renderProps: (task: WorkTask) => TaskItemProps
}) {
    const showPriority = isShown(settings, 'priority')
    const showDue = isShown(settings, 'dueDate')
    const showTarget = isShown(settings, 'targetHours')
    const th = {
        textAlign: 'left', fontSize: 10, fontWeight: 600, color: 'text.disabled', textTransform: 'uppercase',
        letterSpacing: '0.05em', px: '12px', py: '8px', whiteSpace: 'nowrap', borderBottom: '1px solid', borderBottomColor: 'divider',
    } as const

    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '10px', overflowX: 'auto' }}>
            <Box component="table" sx={{ width: '100%', borderCollapse: 'collapse', minWidth: 760 }}>
                <Box component="thead" sx={{ bgcolor: 'action.hover' }}>
                    <tr>
                        <Box component="th" sx={th}>Task</Box>
                        <Box component="th" sx={th}>Assignees</Box>
                        <Box component="th" sx={th}>Status</Box>
                        {showPriority && <Box component="th" sx={th}>Priority</Box>}
                        {showDue && <Box component="th" sx={th}>Due</Box>}
                        {showTarget && <Box component="th" sx={th}>Hours</Box>}
                        <Box component="th" aria-label="Actions" sx={th} />
                    </tr>
                </Box>
                <tbody>
                    {tasks.map((task) => <TaskRow key={task.id} {...renderProps(task)} />)}
                </tbody>
            </Box>
        </Box>
    )
}

function TaskRow(props: TaskItemProps) {
    const { task, today, settings, onOpen } = props
    const f = taskFacts(task, today, settings)
    const td = { px: '12px', py: '10px', fontSize: 12, verticalAlign: 'middle', borderBottom: '1px solid', borderBottomColor: 'divider' } as const

    return (
        <Box component="tr" data-testid="task-row" onClick={onOpen} sx={{
            cursor: 'pointer', opacity: f.closed ? 0.7 : 1,
            '&:hover': { bgcolor: 'action.hover' },
            '&:last-of-type td': { borderBottom: 'none' },
        }}>
            <Box component="td" sx={{ ...td, maxWidth: 360 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                    <ProjectBadge task={task} closed={f.closed} />
                    <Box
                        component="button"
                        type="button"
                        title={task.title}
                        onClick={(e: React.MouseEvent) => { e.stopPropagation(); onOpen() }}
                        sx={{
                            minWidth: 0, p: 0, border: 'none', bgcolor: 'transparent', textAlign: 'left', fontFamily: 'inherit', cursor: 'pointer',
                            fontSize: 13, fontWeight: 700, color: 'text.primary',
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            '&:hover': { color: 'primary.main' },
                        }}
                    >
                        {task.title}
                    </Box>
                </Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '2px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {[task.departmentName, task.projectName, `by ${task.createdByName}`].filter(Boolean).join(' · ')}
                </Box>
                <TaskNotices task={task} late={f.late} fileNeeded={f.fileNeeded} inline />
            </Box>
            <Box component="td" sx={{ ...td, maxWidth: 200 }}><Assignees task={task} max={4} size={24} /></Box>
            <Box component="td" sx={td}><StatusPill status={task.status} /></Box>
            {isShown(settings, 'priority') && <Box component="td" sx={td}><PriorityChip task={task} /></Box>}
            {f.showDue && (
                <Box component="td" sx={{ ...td, whiteSpace: 'nowrap', color: f.late > 0 ? 'error.main' : 'text.primary', fontWeight: f.late > 0 ? 700 : 500 }}>
                    {task.dueDate ? formatDate(task.dueDate) : '—'}
                </Box>
            )}
            {f.showTarget && (
                <Box component="td" sx={{ ...td, whiteSpace: 'nowrap', color: f.progress.over ? 'error.main' : 'text.secondary' }}>
                    {task.targetHours != null ? f.progress.text : '—'}
                </Box>
            )}
            <Box component="td" sx={{ ...td, textAlign: 'right' }}>
                <TaskActions {...props} fileNeeded={f.fileNeeded} dense />
            </Box>
        </Box>
    )
}
