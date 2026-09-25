import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import {
    getProjectActivityTypes,
    getProjectComponents,
    getProjects,
    getProjectTypes,
    getTimesheet,
} from '../../lib/api'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'

/*
 * One card per working day of the timesheet's week, each listing what was logged
 * on it. Shared by All Timesheets (the HR Administrator's expanded row) and Team
 * Timesheets (the manager's View dialog) so the two reviewers read a week the
 * same way.
 */

type Task = {
    hours: number
    project: string
    detail: string
    notes: string
}

type Day = {
    key: string
    name: string
    dateLabel: string
    total: number
    tasks: Task[]
}

export default function TimesheetDailyBreakdown({ ts, title = 'Daily breakdown' }: { ts: Timesheet; title?: string | null }) {
    const { data, isLoading } = useQuery({
        queryKey: ['timesheet', ts.id],
        queryFn: () => getTimesheet(ts.id),
    })
    const entries = useMemo(() => (data?.entries as TimesheetEntry[] | undefined) ?? [], [data])

    // Entries carry project, type, component and activity ids, not their names —
    // the detail endpoint returns the entity with no project loaded, so e.project
    // is never there. Same query keys the pages use, so these are cache reads
    // rather than second fetches.
    const { data: projects = [] } = useQuery({ queryKey: ['projects'], queryFn: getProjects })
    const { data: projectTypes = [] } = useQuery({ queryKey: ['projectTypes'], queryFn: getProjectTypes })
    const { data: components = [] } = useQuery({ queryKey: ['projectComponents'], queryFn: getProjectComponents })
    const { data: activityTypes = [] } = useQuery({ queryKey: ['projectActivityTypes'], queryFn: getProjectActivityTypes })
    const projectById = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects])
    const typeById = useMemo(() => new Map(projectTypes.map((t) => [t.id, t])), [projectTypes])
    const componentById = useMemo(() => new Map(components.map((c) => [c.id, c])), [components])
    const activityById = useMemo(() => new Map(activityTypes.map((a) => [a.id, a])), [activityTypes])

    /* Each card is a calendar date and an entry belongs to the card whose date it
       carries. Both are dates, not instants, so the keys are built in UTC from the
       date part of periodStart and compared with the date part of the entry's
       own value. Building them from local midnight and reading them back through
       toISOString put every card a day behind east of Greenwich: Monday read
       "Nothing logged" while its rows sat under Tue, and Friday's never showed. */
    const days = useMemo<Day[]>(() => {
        const base = new Date(`${ts.periodStart.slice(0, 10)}T00:00:00Z`)
        const out: Day[] = []
        for (let i = 0; i < 5; i++) {
            const d = new Date(base)
            d.setUTCDate(base.getUTCDate() + i)
            const key = d.toISOString().slice(0, 10)
            const name = d.toLocaleDateString('en-GB', { weekday: 'short', timeZone: 'UTC' })
            const dateLabel = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' })
            const dayEntries = entries.filter((e) => e.date.slice(0, 10) === key)
            const total = dayEntries.reduce((s, e) => s + Number(e.hoursWorked), 0)
            const tasks = dayEntries.map((e): Task => {
                const project = projectById.get(e.projectId) ?? e.project
                // Blank parts are left out, so an untyped entry reads as project alone.
                const type = e.projectTypeId != null ? typeById.get(e.projectTypeId)?.name : undefined
                const component = e.projectComponentId != null ? componentById.get(e.projectComponentId)?.name : undefined
                const activity = e.activityTypeId != null ? activityById.get(e.activityTypeId)?.name : undefined
                return {
                    hours: Number(e.hoursWorked),
                    project: project?.code || project?.name || `Project #${e.projectId}`,
                    detail: [type, component, activity].filter(Boolean).join(' · '),
                    notes: e.notes?.trim() ?? '',
                }
            })
            out.push({ key, name, dateLabel, total, tasks })
        }
        return out
    }, [entries, ts.periodStart, projectById, typeById, componentById, activityById])

    if (isLoading && entries.length === 0) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
                <CircularProgress size={20} />
            </Box>
        )
    }

    return (
        <>
            {title && (
                <Typography sx={{
                    fontSize: 11, color: 'text.secondary',
                    textTransform: 'uppercase', letterSpacing: '0.05em',
                    mb: 1,
                }}>
                    {title}
                </Typography>
            )}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 1 }}>
                {days.map((d) => {
                    const isEmpty = d.total === 0
                    return (
                        <Box key={d.key} data-day={d.key} sx={{
                            bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
                            borderRadius: '6px', p: '10px 12px', minWidth: 0,
                        }}>
                            <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 0.75 }}>
                                <Typography sx={{
                                    fontSize: 11, fontWeight: 600, color: 'text.secondary',
                                    textTransform: 'uppercase', letterSpacing: '0.05em',
                                }}>
                                    {d.name}
                                    <Box component="span" sx={{ ml: 0.75, fontWeight: 400, textTransform: 'none', letterSpacing: 0 }}>
                                        {d.dateLabel}
                                    </Box>
                                </Typography>
                                <Typography sx={{
                                    fontSize: 13, fontWeight: 700,
                                    color: isEmpty ? 'text.disabled' : 'text.primary',
                                }}>
                                    {d.total.toFixed(1)}h
                                </Typography>
                            </Stack>
                            <Box sx={{
                                fontSize: 11,
                                color: isEmpty ? 'text.disabled' : 'text.primary',
                                fontStyle: isEmpty ? 'italic' : 'normal',
                                lineHeight: 1.4,
                            }}>
                                {isEmpty ? (
                                    <span>Nothing logged</span>
                                ) : (
                                    d.tasks.map((t, idx) => (
                                        <Box key={idx} sx={{
                                            py: '4px',
                                            borderTop: idx === 0 ? 'none' : '1px dashed',
                                            borderTopColor: 'divider',
                                        }}>
                                            <Stack direction="row" spacing={0.75} alignItems="baseline">
                                                <Box component="span" sx={{ fontWeight: 700, fontSize: 11, minWidth: 34 }}>
                                                    {t.hours.toFixed(1)}h
                                                </Box>
                                                <Box component="span" sx={{ color: 'primary.main', fontWeight: 600, fontSize: 11 }}>
                                                    {t.project}
                                                </Box>
                                            </Stack>
                                            {t.detail && (
                                                <Box sx={{ color: 'text.secondary', fontSize: 10, pl: '40px' }}>
                                                    {t.detail}
                                                </Box>
                                            )}
                                            {t.notes && (
                                                <Box sx={{ color: 'text.secondary', fontSize: 10, fontStyle: 'italic', pl: '40px' }}>
                                                    {t.notes}
                                                </Box>
                                            )}
                                        </Box>
                                    ))
                                )}
                            </Box>
                        </Box>
                    )
                })}
            </Box>
        </>
    )
}
