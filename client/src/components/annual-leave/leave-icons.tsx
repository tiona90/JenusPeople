// The one leave-type icon map. Four views render these — the apply flow, the
// employee's own leave list, the manager dashboard's approval queue and the
// admin leave table — and they drifted apart once before: the first two moved
// the sensitive types off emoji while the other two kept rendering 🤒 and 👶.
//
// The lighter categories keep their emoji; sick, bereavement and parental leave
// use plain icons instead — a cartoon face is the wrong register for those
// requests.
import type { ReactNode } from 'react'
import ChildCareRoundedIcon from '@mui/icons-material/ChildCareRounded'
import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import HealthAndSafetyRoundedIcon from '@mui/icons-material/HealthAndSafetyRounded'

// Sized/coloured off the surrounding text so these drop in where an emoji used to sit.
const neutralIconSx = { fontSize: 'inherit', verticalAlign: '-0.15em' } as const

/**
 * Keyed by substring of the leave-type name, matched in insertion order.
 */
export const LEAVE_ICONS: Record<string, ReactNode> = {
    annual: '🌴',
    vacation: '🌴',
    sick: <HealthAndSafetyRoundedIcon sx={neutralIconSx} />,
    personal: '🏠',
    bereavement: <FavoriteBorderRoundedIcon sx={neutralIconSx} />,
    unpaid: '💼',
    maternity: <ChildCareRoundedIcon sx={neutralIconSx} />,
    paternity: <ChildCareRoundedIcon sx={neutralIconSx} />,
    parental: <ChildCareRoundedIcon sx={neutralIconSx} />,
    study: '📚',
    compassionate: '💙',
}

/** Icon for a leave-type name, for anywhere that can render a node. */
export function iconForLeaveType(name?: string | null): ReactNode {
    const n = (name ?? '').toLowerCase()
    for (const k in LEAVE_ICONS) {
        if (n.includes(k)) return LEAVE_ICONS[k]
    }
    return '📅'
}

/**
 * Text-only glyph, for the few places that cannot render a node — a native
 * `<option>` shows its text content and silently drops element children.
 *
 * The sensitive types are deliberately absent: their whole point is that they
 * no longer have an emoji, and there is no text stand-in for an SVG. Callers
 * get '' and should render the type name on its own.
 */
const LEAVE_EMOJI: Record<string, string> = {
    annual: '🌴',
    vacation: '🌴',
    personal: '🏠',
    unpaid: '💼',
    study: '📚',
    compassionate: '💙',
}

/** Emoji for a leave-type name, or '' when it has no text-safe form. */
export function emojiForLeaveType(name?: string | null): string {
    const n = (name ?? '').toLowerCase()
    for (const k in LEAVE_EMOJI) {
        if (n.includes(k)) return LEAVE_EMOJI[k]
    }
    return ''
}

/** `"🌴 Annual Leave"` / `"Sick Leave"` — a label safe for plain-text contexts. */
export function labelWithEmoji(name: string): string {
    const emoji = emojiForLeaveType(name)
    return emoji ? `${emoji} ${name}` : name
}
