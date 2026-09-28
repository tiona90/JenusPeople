import Box from '@mui/material/Box'

/**
 * The pieces the card-grid pages share — Projects under Configuration, and Tasks.
 * One copy, so the two pages cannot drift apart. The plain helpers they share
 * (colours, initials) live in `lib/card-kit.ts`, so this file exports components only.
 */

/** The small uppercase label every card section opens with. */
export function SectionLabel({ children }: { children: React.ReactNode }) {
    return (
        <Box sx={{ fontSize: 10, color: 'text.disabled', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600 }}>
            {children}
        </Box>
    )
}

export function StatCard({ label, value, sub, valueColor }: {
    label: string
    value: string
    sub: string
    valueColor?: string
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '14px 16px' }}>
            <Box sx={{ fontSize: 11, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '6px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 26, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '6px' }}>{sub}</Box>
        </Box>
    )
}

/** One cell of the three-up strip near the foot of a card. */
export function CardStat({ label, value, sub, valueColor }: {
    label: string
    value: string
    sub: string
    valueColor?: string
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', p: '12px 14px', textAlign: 'center' }}>
            <Box sx={{ fontSize: 10, color: 'text.secondary', textTransform: 'uppercase', letterSpacing: '0.05em', mb: '4px' }}>
                {label}
            </Box>
            <Box sx={{ fontSize: 17, fontWeight: 700, color: valueColor ?? 'text.primary', lineHeight: 1 }}>{value}</Box>
            <Box sx={{ fontSize: 10, color: 'text.secondary', mt: '2px' }}>{sub}</Box>
        </Box>
    )
}

export function SelectFilter({ value, onChange, options, ariaLabel, disabled }: {
    value: string
    onChange: (v: string) => void
    options: { value: string; label: string }[]
    ariaLabel?: string
    disabled?: boolean
}) {
    return (
        <Box
            component="select"
            aria-label={ariaLabel}
            value={value}
            disabled={disabled}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => onChange(e.target.value)}
            sx={{
                fontSize: 12, fontFamily: 'inherit', p: '7px 10px',
                border: '1px solid', borderColor: 'divider', borderRadius: '6px',
                color: 'text.primary', bgcolor: 'background.paper', outline: 'none', cursor: 'pointer',
                '&:focus': { borderColor: 'primary.main' },
                '&:disabled': { cursor: 'default', color: 'text.disabled' },
            }}
        >
            {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </Box>
    )
}

export function OutlineBtn({ children, onClick, flex, danger }: {
    children: React.ReactNode
    onClick: () => void
    flex?: boolean
    danger?: boolean
}) {
    return (
        <Box
            component="button"
            type="button"
            onClick={onClick}
            sx={{
                bgcolor: 'background.paper', color: danger ? 'error.main' : 'text.primary',
                border: `1px solid ${danger ? 'error.main' : 'divider'}`,
                borderRadius: '6px', px: '12px', py: '6px',
                fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit',
                flex: flex ? 1 : 'initial',
                '&:hover': danger
                    ? { bgcolor: '#FFF5F5', borderColor: 'error.main' }
                    : { bgcolor: 'action.hover', borderColor: 'primary.main', color: 'primary.main' },
            }}
        >
            {children}
        </Box>
    )
}
