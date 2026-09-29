import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import { getIdleTaskPeople } from '../../lib/api'
import { avatarBg, initials } from '../../lib/card-kit'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTaskIdlePerson } from '../../lib/types'

/**
 * For a Manager or HR Administrator: who in their departments is not working on
 * anything right now — no task In Progress. Somebody with tasks still To Do is
 * listed too, and says so, since none of it has started. On the Tasks page it
 * follows the department filter; on the dashboards it links through to Tasks.
 */
export default function IdlePeoplePanel({ departmentId = null, onViewTasks }: {
    departmentId?: number | null
    /** A "Go to Tasks" link in the header (the dashboards). */
    onViewTasks?: () => void
}) {
    const [open, setOpen] = useState(true)
    const idle = useQuery({ queryKey: ['work-tasks', 'idle-people'], queryFn: getIdleTaskPeople })

    if (idle.isLoading || idle.isError) return null
    const people = (idle.data ?? []).filter((p) => departmentId == null || p.departmentIds.includes(departmentId))

    return (
        <Box component="section" aria-label="Not working on a task" sx={{
            bgcolor: 'background.paper', border: '1px solid', borderColor: people.length > 0 ? 'warning.light' : 'divider',
            borderRadius: '10px', mb: '14px', overflow: 'hidden',
        }}>
            <Box
                component="button"
                type="button"
                aria-expanded={open}
                onClick={() => setOpen((v) => !v)}
                sx={{
                    width: '100%', display: 'flex', alignItems: 'center', gap: '10px', p: '10px 14px',
                    bgcolor: people.length > 0 ? softBg('warning') : 'transparent', border: 'none', cursor: 'pointer',
                    fontFamily: 'inherit', textAlign: 'left',
                }}
            >
                <Box component="span" aria-hidden sx={{ fontSize: 14 }}>🧍</Box>
                <Box sx={{ fontSize: 13, fontWeight: 700, color: 'text.primary' }}>Not working on a task</Box>
                <Box data-testid="idle-count" sx={{
                    fontSize: 11, fontWeight: 700, px: '8px', py: '1px', borderRadius: '10px',
                    bgcolor: people.length > 0 ? 'warning.main' : 'action.hover', color: people.length > 0 ? '#fff' : 'text.secondary',
                }}>{people.length}</Box>
                <Box sx={{ fontSize: 12, color: 'text.secondary', flex: 1 }}>
                    {people.length === 0 ? 'Everyone has a task in progress.' : 'Nobody has started a task for them — no task In progress.'}
                </Box>
                <Box component="span" aria-hidden sx={{ fontSize: 12, color: 'text.secondary' }}>{open ? '▲' : '▼'}</Box>
            </Box>
            {onViewTasks && (
                <Box sx={{ display: 'flex', justifyContent: 'flex-end', px: '14px', pt: open && people.length > 0 ? '8px' : '6px', pb: open && people.length > 0 ? 0 : '8px' }}>
                    <Box
                        component="button"
                        type="button"
                        onClick={onViewTasks}
                        sx={{
                            bgcolor: 'transparent', border: 'none', p: 0, cursor: 'pointer', fontFamily: 'inherit',
                            fontSize: 12, fontWeight: 600, color: 'primary.main', '&:hover': { textDecoration: 'underline' },
                        }}
                    >
                        Go to Tasks →
                    </Box>
                </Box>
            )}
            {open && people.length > 0 && (
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: '8px', p: '12px 14px' }}>
                    {people.map((p) => <Person key={p.userId} person={p} />)}
                </Box>
            )}
        </Box>
    )
}

function Person({ person }: { person: WorkTaskIdlePerson }) {
    const waiting = person.toDoCount > 0
    return (
        <Box data-testid="idle-person" sx={{
            display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0,
            border: '1px solid', borderColor: 'divider', borderRadius: '20px', pl: '4px', pr: '12px', py: '4px',
        }}>
            <Box sx={{
                width: 28, height: 28, borderRadius: '50%', flexShrink: 0,
                bgcolor: avatarBg(person.displayName || person.userId), color: '#fff',
                display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 10, fontWeight: 600,
            }}>{initials(person.displayName)}</Box>
            <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap' }}>
                    {person.displayName}
                    {person.isManager && <Box component="span" sx={{ fontSize: 10, color: 'text.secondary', fontWeight: 500, ml: '6px' }}>Manager</Box>}
                </Box>
                <Box sx={{ fontSize: 10, color: 'text.secondary', whiteSpace: 'nowrap' }}>
                    {person.departmentNames.join(', ')} ·{' '}
                    <Box component="span" sx={{ color: waiting ? 'warning.dark' : 'error.main', fontWeight: 600 }}>
                        {waiting ? `${person.toDoCount} to do, none started` : 'No tasks'}
                    </Box>
                </Box>
            </Box>
        </Box>
    )
}
