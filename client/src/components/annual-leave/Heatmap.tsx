import type { ReactNode } from 'react'
import Box from '@mui/material/Box'
import { softBg, type SxColor } from '../../lib/theme-tokens'
import { fmtShort, isoDate } from './leave-format'

const MONTH_NAMES = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
]

export default function Heatmap({ month, year, heatmap, holidays, today, onNav, alert }: {
    month: number
    year: number
    heatmap: Map<string, { count: number; people: string[] }>
    holidays: Map<string, string>
    today: Date
    onNav: (delta: number) => void
    alert: { iso: string; count: number; people: string[] } | undefined
}) {
    const firstOfMonth = new Date(year, month, 1)
    const startDow = firstOfMonth.getDay()
    const startOffset = startDow === 0 ? 6 : startDow - 1
    const daysInMonth = new Date(year, month + 1, 0).getDate()
    const todayIso = isoDate(today)

    const cells: ReactNode[] = []
    for (let i = 0; i < startOffset; i++) cells.push(<Box key={`b-${i}`} sx={{ aspectRatio: '1' }} />)
    for (let d = 1; d <= daysInMonth; d++) {
        const date = new Date(year, month, d)
        const iso = isoDate(date)
        const dow = date.getDay()
        const weekend = dow === 0 || dow === 6
        const data = heatmap.get(iso)
        const holiday = holidays.has(iso)
        const conflictCount = data && data.count >= 3 ? data.count : 0
        const count = data?.count ?? 0
        const isToday = iso === todayIso

        let bg: SxColor = 'action.hover'
        let color: SxColor = 'text.primary'
        if (weekend) { bg = 'action.hover'; color = 'text.disabled' }
        if (count === 1) { bg = softBg('info'); color = 'info.dark' }
        else if (count === 2) { bg = softBg('info'); color = 'info.dark' }
        else if (count === 3) { bg = softBg('info'); color = 'info.dark' }
        else if (count >= 4) { bg = 'primary.main'; color = '#fff' }
        if (conflictCount > 0) { bg = softBg('warning'); color = 'warning.dark' }
        if (holiday) { bg = softBg('secondary'); color = 'secondary.dark' }

        const holidayName = holidays.get(iso)
        const titleParts: string[] = [`${d} ${MONTH_NAMES[month]}`]
        if (holidayName) titleParts.push(`🎉 ${holidayName}`)
        if (data) titleParts.push(`${data.count} on leave (${data.people.join(', ')})`)

        cells.push(
            <Box
                key={iso}
                title={titleParts.join(' · ')}
                sx={{
                    aspectRatio: '1', bgcolor: bg, color, borderRadius: '6px',
                    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                    fontSize: 11, position: 'relative',
                    border: holiday ? '1px solid' : 'none',
                    borderColor: holiday ? 'secondary.main' : 'transparent',
                    boxShadow: isToday
                        ? (theme) => `inset 0 0 0 2px ${theme.palette.primary.main}`
                        : conflictCount > 0 ? 'inset 0 0 0 1px #F59E0B' : 'none',
                    cursor: count > 0 || holiday ? 'help' : 'default',
                }}
            >
                <Box sx={{ fontWeight: isToday ? 700 : 500 }}>{d}</Box>
                {count > 0 && <Box sx={{ fontSize: 9, fontWeight: 700, mt: '2px' }}>{count}</Box>}
                {holiday && (
                    <Box component="span" sx={{ position: 'absolute', top: 2, left: 3, fontSize: 10, lineHeight: 1 }}>🎉</Box>
                )}
                {conflictCount > 0 && (
                    <Box component="span" sx={{ position: 'absolute', top: 2, right: 3, fontSize: 9 }}>⚠</Box>
                )}
            </Box>
        )
    }

    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '14px 18px', mb: '14px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '10px', mb: '12px' }}>
                <Box>
                    <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>{MONTH_NAMES[month]} {year} · Leave Calendar</Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '2px' }}>Click a request below to see details</Box>
                </Box>
                <Box sx={{ display: 'flex', gap: '14px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <Box sx={{ display: 'flex', gap: '10px', fontSize: 10, color: 'text.secondary', flexWrap: 'wrap' }}>
                        <Legend color="#F9FAFB" label="None" bordered />
                        <Legend color="#DBEAFE" label="1" />
                        <Legend color="#BFDBFE" label="2" />
                        <Legend color="#93C5FD" label="3" />
                        <Legend color={'primary.main'} label="4+" />
                        <Legend color="#FEF3C7" label="⚠ Conflict" bordered borderColor="#F59E0B" />
                        <Legend color="#EDE9FE" label="🎉 Holiday" bordered borderColor="#8B5CF6" />
                    </Box>
                    <Box sx={{ display: 'flex', gap: '4px' }}>
                        <CalNavBtn onClick={() => onNav(-1)}>‹</CalNavBtn>
                        <CalNavBtn onClick={() => onNav(1)}>›</CalNavBtn>
                    </Box>
                </Box>
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '4px', mb: '4px' }}>
                {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((d) => (
                    <Box key={d} sx={{ textAlign: 'center', fontSize: 10, color: 'text.disabled', fontWeight: 600, textTransform: 'uppercase' }}>{d}</Box>
                ))}
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '4px' }}>
                {cells}
            </Box>
            {alert && (
                <Box sx={{
                    mt: '12px', p: '10px 14px', bgcolor: softBg('warning'),
                    border: '1px solid #FDE68A', borderRadius: '8px',
                    display: 'flex', alignItems: 'flex-start', gap: '8px',
                    fontSize: 12, color: 'warning.dark',
                }}>
                    <Box component="span">⚠️</Box>
                    <Box>
                        <Box component="strong">{fmtShort(alert.iso)}:</Box>{' '}
                        {alert.count} employees on leave that day ({alert.people.join(', ')}).
                        Could impact multiple departments — review carefully.
                    </Box>
                </Box>
            )}
        </Box>
    )
}

function Legend({ color, label, bordered, borderColor }: {
    color: string; label: string; bordered?: boolean; borderColor?: string
}) {
    return (
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <Box sx={{
                width: 12, height: 12, borderRadius: '2px', bgcolor: color,
                border: bordered ? `1px solid ${borderColor ?? 'divider'}` : 'none',
                display: 'inline-block',
            }} />
            {label}
        </Box>
    )
}

function CalNavBtn({ onClick, children }: { onClick: () => void; children: ReactNode }) {
    return (
        <Box
            component="button"
            onClick={onClick}
            sx={{
                width: 28, height: 28, border: '1px solid', borderColor: 'divider', bgcolor: 'background.paper',
                borderRadius: '5px', cursor: 'pointer', fontSize: 14, color: 'text.secondary', fontFamily: 'inherit',
                '&:hover': { bgcolor: 'action.hover', color: 'text.primary' },
            }}
        >
            {children}
        </Box>
    )
}
