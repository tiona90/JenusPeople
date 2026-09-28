import { softBg } from '../../lib/theme-tokens'
import type { WorkTaskPriority, WorkTaskStatus } from '../../lib/types'

type Tint = string | ReturnType<typeof softBg>

/** One palette for a status wherever it is drawn: the card's pill, the Status menu, the edit dialog. */
export const STATUS_COLORS: Record<WorkTaskStatus, { bg: Tint; fg: string; dot: string }> = {
    ToDo:       { bg: 'divider', fg: 'text.secondary', dot: 'text.disabled' },
    InProgress: { bg: softBg('primary'), fg: 'primary.dark', dot: 'primary.main' },
    Done:       { bg: softBg('success'), fg: 'success.dark', dot: 'success.main' },
    Cancelled:  { bg: 'action.hover', fg: 'text.disabled', dot: 'error.light' },
}

export const PRIORITY_COLORS: Record<WorkTaskPriority, { bg: Tint; fg: string }> = {
    Low:    { bg: 'action.hover', fg: 'text.secondary' },
    Normal: { bg: softBg('primary'), fg: 'primary.dark' },
    High:   { bg: softBg('error'), fg: 'error.dark' },
}
