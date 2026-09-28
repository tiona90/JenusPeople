/** Helpers shared by the card-grid pages (Projects, Tasks); the components are in `components/ui/CardKit.tsx`. */

/** A project's `colorKey` → the badge colour its code is drawn in. */
export const CODE_COLORS: Record<string, string> = {
    p1: 'primary.main', p2: 'success.main', p3: 'warning.main', p4: 'secondary.main', p5: 'error.main',
}

const AVATAR_PALETTE = ['primary.main', 'success.main', 'warning.main', 'secondary.main', '#EC4899', '#06B6D4', '#84CC16', 'error.main']

export function initials(name: string) {
    const parts = (name ?? '').trim().split(/\s+/).filter(Boolean)
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

/** A stable colour per person, so the same name is always the same avatar. */
export function avatarBg(seed: string) {
    let hash = 0
    for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0
    return AVATAR_PALETTE[Math.abs(hash) % AVATAR_PALETTE.length]
}
