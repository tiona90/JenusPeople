import Box from '@mui/material/Box'

export default function DeptBreakdown({ stats, totalUsed, totalAllowance, onFilter }: {
    stats: { name: string; total: number; used: number; pending: number; entitled: number }[]
    totalUsed: number
    totalAllowance: number
    onFilter: (dept: string) => void
}) {
    return (
        <Box sx={{ bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', p: '14px 18px', mb: '14px' }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: '12px', flexWrap: 'wrap', gap: '4px' }}>
                <Box sx={{ fontSize: 13, fontWeight: 600, color: 'text.primary' }}>Leave by Department</Box>
                <Box sx={{ fontSize: 11, color: 'text.secondary' }}>YTD · {totalUsed} of {totalAllowance} days used</Box>
            </Box>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                {stats.length === 0 ? (
                    <Box sx={{ fontSize: 12, color: 'text.secondary', py: '6px' }}>No departments configured.</Box>
                ) : stats.map((d) => {
                    const pct = d.entitled > 0 ? (d.used / d.entitled) * 100 : 0
                    const fillColor = pct >= 75 ? 'error.main' : pct >= 50 ? 'warning.main' : 'success.main'
                    return (
                        <Box key={d.name} sx={{
                            display: 'grid',
                            gridTemplateColumns: { xs: '1fr', sm: '140px 1fr 130px auto' },
                            gap: '10px', alignItems: 'center',
                        }}>
                            <Box>
                                <Box sx={{ fontSize: 12, fontWeight: 600, color: 'text.primary' }}>{d.name}</Box>
                                <Box sx={{ fontSize: 11, color: 'text.disabled' }}>{d.total} {d.total === 1 ? 'person' : 'people'}</Box>
                            </Box>
                            <Box sx={{ position: 'relative', height: 22, bgcolor: 'action.hover', borderRadius: '4px', overflow: 'hidden' }}>
                                <Box sx={{
                                    height: '100%', bgcolor: fillColor, width: `${Math.min(100, pct)}%`,
                                    display: 'flex', alignItems: 'center', justifyContent: 'flex-end',
                                    pr: '8px', fontSize: 10, color: '#fff', fontWeight: 600,
                                }}>
                                    {pct >= 14 && `${d.used}d · ${Math.round(pct)}%`}
                                </Box>
                            </Box>
                            <Box sx={{ fontSize: 12, color: 'text.secondary' }}>
                                {d.pending > 0
                                    ? <><Box component="strong" sx={{ color: 'warning.main' }}>{d.pending}</Box> pending</>
                                    : <Box component="span" sx={{ color: 'success.main' }}>✓ None pending</Box>}
                            </Box>
                            <Box
                                component="button"
                                onClick={() => onFilter(d.name)}
                                sx={{
                                    fontSize: 11, color: 'primary.main', bgcolor: 'transparent',
                                    border: 'none', cursor: 'pointer', fontFamily: 'inherit',
                                    '&:hover': { textDecoration: 'underline' },
                                }}
                            >
                                Filter
                            </Box>
                        </Box>
                    )
                })}
            </Box>
        </Box>
    )
}
