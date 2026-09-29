import Box from '@mui/material/Box'
import type { WorkTask, WorkTaskStatus } from '../../lib/types'
import { STATUS_LABELS } from '../../lib/work-tasks'
import { TaskCard, type TaskItemProps } from './TaskCard'
import { STATUS_COLORS } from './statusStyles'

/**
 * The board: one column per status, the columns the status filter admits
 * (`boardStatuses`). It ignores the page's groups — those are about who a task
 * belongs to, the board is about where it stands. Moves happen on the cards'
 * own buttons; there is no dragging, so a move is never a mis-drop.
 */
export default function TaskBoard({ tasks, statuses, renderProps }: {
    tasks: readonly WorkTask[]
    statuses: readonly WorkTaskStatus[]
    renderProps: (task: WorkTask) => TaskItemProps
}) {
    return (
        // The columns scroll sideways inside the board, never the page.
        <Box sx={{ overflowX: 'auto', pb: '6px', mb: '22px' }}>
            <Box sx={{
                display: 'grid', gap: '12px', alignItems: 'start',
                gridTemplateColumns: `repeat(${statuses.length}, minmax(270px, 1fr))`,
            }}>
                {statuses.map((status) => {
                    const column = tasks.filter((t) => t.status === status)
                    const c = STATUS_COLORS[status]
                    return (
                        <Box key={status} component="section" aria-label={STATUS_LABELS[status]} sx={{
                            bgcolor: 'action.hover', borderRadius: '12px', p: '10px',
                            display: 'flex', flexDirection: 'column', gap: '10px', minHeight: 120,
                        }}>
                            <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', px: '4px' }}>
                                <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: c.dot }} />
                                <Box component="h2" sx={{ m: 0, fontSize: 13, fontWeight: 700, color: 'text.primary' }}>{STATUS_LABELS[status]}</Box>
                                <Box data-testid="column-count" sx={{ fontSize: 11, fontWeight: 700, px: '7px', borderRadius: '10px', bgcolor: c.bg, color: c.fg }}>
                                    {column.length}
                                </Box>
                            </Box>
                            {column.length === 0 ? (
                                <Box sx={{ fontSize: 12, color: 'text.disabled', textAlign: 'center', py: 3, border: '1px dashed', borderColor: 'divider', borderRadius: '8px' }}>
                                    Nothing here
                                </Box>
                            ) : (
                                column.map((task) => <TaskCard key={task.id} variant="board" {...renderProps(task)} />)
                            )}
                        </Box>
                    )
                })}
            </Box>
        </Box>
    )
}
