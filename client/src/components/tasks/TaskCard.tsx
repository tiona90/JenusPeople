import { useState } from 'react'
import Box from '@mui/material/Box'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import type { WorkTaskSettings } from '../../lib/api/work-task-settings'
import { CODE_COLORS, avatarBg, initials } from '../../lib/card-kit'
import { ATTACHMENT_REQUIRED_MESSAGE, isShown } from '../../lib/task-settings'
import { softBg } from '../../lib/theme-tokens'
import type { WorkTask, WorkTaskStatus } from '../../lib/types'
import {
    PRIORITY_LABELS, SETTABLE_STATUSES, STATUS_LABELS, formatTaskDate as formatDate,
    isAwaitingConfirmation, isOpenTask, nextStatusAction, plural, taskFacts,
} from '../../lib/work-tasks'
import { OutlineBtn } from '../ui/CardKit'
import { PRIORITY_COLORS, STATUS_COLORS } from './statusStyles'

/**
 * One task, drawn three ways — a card in the grid, a slimmer card on the board, a
 * row in the list — from the same pieces, so the three cannot disagree about what a
 * task says or which moves it offers.
 */
export interface TaskItemProps {
    task: WorkTask
    today: string
    settings: WorkTaskSettings
    statusPending: boolean
    onStatus: (next: WorkTaskStatus) => void
    /** The details dialog: the whole description and the files, which the card only summarises. */
    onOpen: () => void
    onEdit: () => void
    onDelete: () => void
    /** A reviewer returning a waiting task: opens the reason dialog. */
    onSendBack: () => void
}

/* ─── small pieces ───────────────────────────────────────────────────────── */

export function ProjectBadge({ task, closed }: { task: WorkTask; closed: boolean }) {
    if (!task.projectCode) return null
    const color = closed ? 'text.disabled' : (CODE_COLORS[task.projectColorKey ?? ''] ?? CODE_COLORS.p1)
    return (
        <Box sx={{
            display: 'inline-block', flexShrink: 0, bgcolor: color, color: '#fff',
            fontSize: 10, fontWeight: 700, px: '7px', py: '2px', borderRadius: '5px', letterSpacing: '0.02em',
        }}>{task.projectCode}</Box>
    )
}

export function StatusPill({ status }: { status: WorkTaskStatus }) {
    const c = STATUS_COLORS[status]
    return (
        <Box sx={{
            display: 'inline-flex', alignItems: 'center', gap: '4px', flexShrink: 0,
            px: '9px', py: '3px', borderRadius: '12px', fontSize: 11, fontWeight: 600,
            bgcolor: c.bg, color: c.fg, whiteSpace: 'nowrap',
        }}>
            <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: c.dot }} />
            {STATUS_LABELS[status]}
        </Box>
    )
}

export function PriorityChip({ task }: { task: WorkTask }) {
    const c = PRIORITY_COLORS[task.priority]
    return (
        <Box sx={{ px: '8px', py: '2px', borderRadius: '10px', fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap', bgcolor: c.bg, color: c.fg }}>
            {PRIORITY_LABELS[task.priority]} priority
        </Box>
    )
}

function Chip({ children, tone = 'neutral' }: { children: React.ReactNode; tone?: 'neutral' | 'success' }) {
    return (
        <Box sx={{
            fontSize: 11, px: '8px', py: '2px', borderRadius: '10px', whiteSpace: 'nowrap',
            fontWeight: tone === 'success' ? 600 : 500,
            bgcolor: tone === 'success' ? softBg('success') : 'action.hover',
            color: tone === 'success' ? 'success.dark' : 'text.secondary',
        }}>{children}</Box>
    )
}

/** Overlapping avatars, a name on hover, and the head count. */
export function Assignees({ task, max = 5, size = 26 }: { task: WorkTask; max?: number; size?: number }) {
    const shown = task.assignees.slice(0, max)
    const remaining = task.assignees.length - shown.length
    const avatar = {
        width: size, height: size, borderRadius: '50%', flexShrink: 0,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: size < 28 ? 10 : 11, fontWeight: 600,
        border: '2px solid', borderColor: 'background.paper', marginLeft: '-6px', '&:first-of-type': { marginLeft: 0 },
    } as const
    return (
        <Box sx={{ display: 'flex', alignItems: 'center', minWidth: 0 }}>
            {shown.map((m) => (
                <Box key={m.userId} title={m.displayName} sx={{ ...avatar, bgcolor: avatarBg(m.displayName || m.userId), color: '#fff' }}>
                    {initials(m.displayName)}
                </Box>
            ))}
            {remaining > 0 && <Box sx={{ ...avatar, bgcolor: 'action.hover', color: 'text.secondary' }}>+{remaining}</Box>}
            {task.assignees.length === 1 ? (
                <Box sx={{ fontSize: 12, color: 'text.primary', fontWeight: 500, ml: '8px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {task.assignees[0].displayName}
                </Box>
            ) : (
                <Box sx={{ fontSize: 12, fontWeight: 700, color: 'text.primary', ml: '8px', whiteSpace: 'nowrap' }}>
                    {task.assignees.length}
                    <Box component="span" sx={{ fontSize: 11, color: 'text.secondary', fontWeight: 500, ml: '3px' }}>people</Box>
                </Box>
            )}
        </Box>
    )
}

/** A label over a value, with a note under it: one cell of the card's facts line. */
function Fact({ label, value, sub, valueColor }: { label: string; value: string; sub?: string; valueColor?: string }) {
    return (
        <Box sx={{ minWidth: 0 }}>
            <Box sx={{ fontSize: 10, color: 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600 }}>{label}</Box>
            <Box sx={{ fontSize: 13, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1.4 }}>{value}</Box>
            {sub && <Box sx={{ fontSize: 11, color: 'text.secondary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{sub}</Box>}
        </Box>
    )
}

/**
 * The notes a task can carry — late, waiting on somebody, sent back, a file owed.
 * `inline` is the list row's smaller wording under the title.
 */
export function TaskNotices({ task, late, fileNeeded, inline = false }: { task: WorkTask; late: number; fileNeeded: boolean; inline?: boolean }) {
    const notes: { key: string; tone: 'warning' | 'error'; text: string }[] = []
    if (late > 0) notes.push({ key: 'late', tone: 'warning', text: `⚠ Overdue by ${plural(late, 'day')}` })
    if (isAwaitingConfirmation(task) && !task.canConfirm) notes.push({ key: 'waiting', tone: 'warning', text: `Waiting for ${task.createdByName} to confirm` })
    if (isOpenTask(task) && !isAwaitingConfirmation(task) && task.sentBackReason) notes.push({ key: 'sent-back', tone: 'error', text: `Sent back: ${task.sentBackReason}` })
    if (fileNeeded) notes.push({ key: 'file', tone: 'warning', text: 'File needed before Done' })
    if (notes.length === 0) return null

    if (inline) {
        return (
            <>
                {notes.map((n) => (
                    <Box key={n.key} title={n.key === 'file' ? ATTACHMENT_REQUIRED_MESSAGE : undefined}
                        sx={{ fontSize: 11, fontWeight: 600, color: `${n.tone}.dark`, mt: '2px', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                        {n.text}
                    </Box>
                ))}
            </>
        )
    }
    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            {notes.map((n) => (
                <Box key={n.key} title={n.key === 'file' ? ATTACHMENT_REQUIRED_MESSAGE : undefined} sx={{
                    px: '10px', py: '5px', borderRadius: '6px', bgcolor: softBg(n.tone),
                    borderLeft: '3px solid', borderLeftColor: `${n.tone}.main`,
                    fontSize: 11, color: `${n.tone}.dark`, fontWeight: 600, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                }}>
                    {n.text}
                </Box>
            ))}
        </Box>
    )
}

/**
 * The task's moves: Confirm / Send back for a reviewer, the next step and the Status
 * menu for whoever works it, and Edit / Delete for whoever runs it. Its clicks —
 * the status menu's included, which bubble through the portal — are not a click on
 * the card or row around it.
 */
export function TaskActions({ task, statusPending, fileNeeded, onStatus, onEdit, onDelete, onSendBack, dense = false }: Omit<TaskItemProps, 'today' | 'settings' | 'onOpen'> & { fileNeeded: boolean; dense?: boolean }) {
    return (
        <Box onClick={(e) => e.stopPropagation()} sx={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap', cursor: 'default' }}>
            {task.canConfirm ? (
                <ReviewControls pending={statusPending} doneBlocked={fileNeeded} onConfirm={() => onStatus('Done')} onSendBack={onSendBack} />
            ) : task.canChangeStatus ? (
                <StatusControls status={task.status} pending={statusPending} doneBlocked={fileNeeded} onStatus={onStatus} />
            ) : (
                <Box sx={{ fontSize: 11, color: 'text.disabled' }}>{dense ? 'View only' : 'Only the creator and assignees change the status'}</Box>
            )}
            <Box sx={{ flex: 1 }} />
            {task.canEdit && (
                <>
                    <OutlineBtn onClick={onEdit}>✏️ Edit</OutlineBtn>
                    <OutlineBtn danger onClick={onDelete}>🗑 Delete</OutlineBtn>
                </>
            )}
        </Box>
    )
}

/* ─── the card ───────────────────────────────────────────────────────────── */

/**
 * `grid` is the page's card; `board` drops what the column already says (the status)
 * and what a narrow column has no room for (the description, the target, the dates
 * bar the due one).
 */
export function TaskCard({ variant = 'grid', ...props }: TaskItemProps & { variant?: 'grid' | 'board' }) {
    const { task, today, settings, onOpen } = props
    const f = taskFacts(task, today, settings)
    const board = variant === 'board'
    const done = task.status === 'Done' && task.completedAtUtc

    return (
        <Box data-testid="task-card" onClick={onOpen} sx={{
            bgcolor: f.closed ? 'action.hover' : 'background.paper',
            border: '1px solid', borderColor: f.late > 0 ? 'warning.main' : 'divider', borderRadius: '12px',
            overflow: 'hidden', transition: 'border-color 0.15s, box-shadow 0.15s', cursor: 'pointer',
            display: 'flex', flexDirection: 'column', opacity: f.closed ? 0.75 : 1,
            '&:hover': { borderColor: f.late > 0 ? 'warning.main' : 'primary.main', boxShadow: '0 2px 10px rgba(0,0,0,0.06)' },
        }}>
            <Box sx={{ p: board ? '12px 14px' : '14px 16px', display: 'flex', flexDirection: 'column', gap: '10px', flex: 1 }}>
                {/* Title line: the project, the title (one line, whole on hover), the status. */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                    <ProjectBadge task={task} closed={f.closed} />
                    {/* A real button, so the details open from the keyboard too. */}
                    <Box
                        component="button"
                        type="button"
                        title={task.title}
                        onClick={(e: React.MouseEvent) => { e.stopPropagation(); onOpen() }}
                        sx={{
                            flex: 1, minWidth: 0, p: 0, border: 'none', bgcolor: 'transparent', textAlign: 'left',
                            fontFamily: 'inherit', cursor: 'pointer',
                            fontSize: board ? 14 : 15, fontWeight: 700, color: 'text.primary', lineHeight: 1.3,
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            '&:hover': { color: 'primary.main' },
                        }}
                    >
                        {task.title}
                    </Box>
                    {!board && <StatusPill status={task.status} />}
                </Box>

                <Box sx={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <Chip>{task.departmentName}</Chip>
                    {isShown(settings, 'priority') && <PriorityChip task={task} />}
                    {isShown(settings, 'billable') && task.isBillable != null && (
                        <Chip tone={task.isBillable ? 'success' : 'neutral'}>{task.isBillable ? 'Billable' : 'Non-billable'}</Chip>
                    )}
                    {task.projectName && !board && (
                        <Box sx={{ fontSize: 11, color: 'text.secondary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{task.projectName}</Box>
                    )}
                </Box>

                {/* Only when there is one: an empty "No description" band told nobody anything. */}
                {!board && isShown(settings, 'description') && task.description && (
                    <Box data-testid="task-summary" sx={{
                        fontSize: 12, lineHeight: 1.5, color: 'text.secondary', whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                        display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden',
                    }}>{task.description}</Box>
                )}

                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' }}>
                    <Assignees task={task} max={board ? 4 : 5} size={board ? 24 : 26} />
                    {isShown(settings, 'attachments') && f.attachmentCount > 0 && (
                        <Box sx={{ fontSize: 11, color: 'text.primary', fontWeight: 600, whiteSpace: 'nowrap' }}>📎 {plural(f.attachmentCount, 'attachment')}</Box>
                    )}
                </Box>

                <TaskNotices task={task} late={f.late} fileNeeded={f.fileNeeded} />

                {/* Due, target and when — one line, not a band of three boxes. */}
                <Box sx={{
                    display: 'grid', gap: '12px', mt: 'auto', pt: '10px', borderTop: '1px solid', borderTopColor: 'divider',
                    gridTemplateColumns: `repeat(${board ? 2 : 1 + (f.showDue ? 1 : 0) + (f.showTarget ? 1 : 0)}, minmax(0, 1fr))`,
                }}>
                    {f.showDue && <Fact
                        label="Due"
                        value={task.dueDate ? formatDate(task.dueDate) : '—'}
                        sub={task.dueDate ? (f.late > 0 ? 'past due' : f.closed ? 'closed' : 'on track') : 'no date set'}
                        valueColor={f.late > 0 ? 'error.main' : undefined}
                    />}
                    {f.showTarget && !board && <Fact
                        label="Target"
                        value={task.targetHours != null ? `${task.targetHours}h` : '—'}
                        sub={task.targetHours == null && f.progress.text === 'nothing logged' ? 'no target set' : f.progress.text}
                        valueColor={f.progress.over ? 'error.main' : undefined}
                    />}
                    <Fact
                        label={done ? 'Completed' : 'Created'}
                        value={formatDate(done ? task.completedAtUtc! : task.createdAtUtc)}
                        sub={done ? 'finished' : `by ${task.createdByName}`}
                    />
                </Box>
            </Box>

            <Box sx={{ p: '8px 12px', bgcolor: 'action.hover', borderTop: '1px solid', borderTopColor: 'divider' }}>
                <TaskActions {...props} fileNeeded={f.fileNeeded} dense={board} />
            </Box>
        </Box>
    )
}

/* ─── status controls ────────────────────────────────────────────────────── */

/** A reviewer's two answers to a task marked done. */
function ReviewControls({ pending, doneBlocked, onConfirm, onSendBack }: { pending: boolean; doneBlocked: boolean; onConfirm: () => void; onSendBack: () => void }) {
    const btn = {
        display: 'inline-flex', alignItems: 'center', gap: '6px', borderRadius: '6px', px: '12px', py: '6px',
        fontSize: 12, fontWeight: 600, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
        '&:disabled': { opacity: 0.6, cursor: 'default' },
    } as const
    return (
        <>
            <Box component="button" type="button" disabled={pending || doneBlocked} title={doneBlocked ? ATTACHMENT_REQUIRED_MESSAGE : undefined} onClick={onConfirm}
                sx={{ ...btn, bgcolor: 'success.main', color: '#fff', border: 'none', '&:hover': { bgcolor: 'success.dark' } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>✓</Box>Confirm
            </Box>
            <Box component="button" type="button" disabled={pending} onClick={onSendBack}
                sx={{ ...btn, bgcolor: 'background.paper', color: 'warning.dark', border: '1px solid', borderColor: 'warning.main', '&:hover': { bgcolor: softBg('warning') } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>↩</Box>Send back
            </Box>
        </>
    )
}

/**
 * The obvious next step as a filled button, and every status in a menu beside it
 * for the rest (cancelling, stepping back).
 */
function StatusControls({ status, pending, doneBlocked, onStatus }: {
    status: WorkTaskStatus
    pending: boolean
    doneBlocked: boolean
    onStatus: (next: WorkTaskStatus) => void
}) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null)
    const next = nextStatusAction(status)
    // An assignee's one move on a task waiting for confirmation is Withdraw; the rest is the reviewer's.
    const waiting = status === 'AwaitingConfirmation'
    const mainBlocked = doneBlocked && next.to === 'Done'
    return (
        <>
            <Box
                component="button"
                type="button"
                disabled={pending || mainBlocked}
                title={mainBlocked ? ATTACHMENT_REQUIRED_MESSAGE : undefined}
                onClick={() => onStatus(next.to)}
                sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                    bgcolor: next.to === 'Done' ? 'success.main' : 'primary.main', color: '#fff',
                    border: 'none', borderRadius: '6px', px: '12px', py: '6px',
                    fontSize: 12, fontWeight: 600, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                    '&:hover': { bgcolor: next.to === 'Done' ? 'success.dark' : 'primary.dark' },
                    '&:disabled': { opacity: 0.6, cursor: 'default' },
                }}
            >
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>{next.icon}</Box>
                {next.label}
            </Box>
            {!waiting && (
                <>
                    <Box
                        component="button"
                        type="button"
                        aria-haspopup="menu"
                        aria-expanded={anchor != null}
                        disabled={pending}
                        onClick={(e: React.MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)}
                        sx={{
                            display: 'inline-flex', alignItems: 'center', gap: '6px',
                            bgcolor: 'background.paper', color: 'text.primary',
                            border: '1px solid', borderColor: 'divider', borderRadius: '6px', px: '10px', py: '6px',
                            fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                            '&:hover': { borderColor: 'primary.main', color: 'primary.main' },
                            '&:disabled': { opacity: 0.6, cursor: 'default' },
                        }}
                    >
                        Status
                        <Box component="span" aria-hidden sx={{ fontSize: 9, color: 'text.secondary' }}>▼</Box>
                    </Box>
                    <Menu
                        anchorEl={anchor}
                        open={anchor != null}
                        onClose={() => setAnchor(null)}
                        slotProps={{ paper: { sx: { minWidth: 170, borderRadius: '10px', mt: '4px' } } }}
                    >
                        {SETTABLE_STATUSES.map((s) => {
                            const current = s === status
                            const itemBlocked = doneBlocked && s === 'Done'
                            return (
                                <MenuItem
                                    key={s}
                                    aria-current={current ? 'true' : undefined}
                                    selected={current}
                                    disabled={itemBlocked}
                                    title={itemBlocked ? ATTACHMENT_REQUIRED_MESSAGE : undefined}
                                    onClick={() => {
                                        if (itemBlocked) return
                                        setAnchor(null)
                                        if (!current) onStatus(s)
                                    }}
                                    sx={{ fontSize: 13, gap: '10px' }}
                                >
                                    <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: STATUS_COLORS[s].dot, flexShrink: 0 }} />
                                    <Box component="span" sx={{ flex: 1 }}>{STATUS_LABELS[s]}</Box>
                                    {current && <Box component="span" aria-hidden sx={{ fontSize: 12, color: 'primary.main' }}>✓</Box>}
                                </MenuItem>
                            )
                        })}
                    </Menu>
                </>
            )}
        </>
    )
}
