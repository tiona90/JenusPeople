// Shared date/format helpers for the annual-leave admin views (used by the page
// and the extracted Heatmap). Pure functions — no UI, no side effects.

export function isoDate(d: Date) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function fmtShort(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
}
