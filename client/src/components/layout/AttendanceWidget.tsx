import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Modal from '@mui/material/Modal'
import Typography from '@mui/material/Typography'
import { softBg } from '../../lib/theme-tokens'
import { getAppSettings } from '../../lib/api'
import { breakAllowanceMinutes, breakClock, formatClock, type BreakClock } from '../../lib/break-policy'
import { queryKeys } from '../../lib/hooks/queryKeys'
import {
    AppDialog,
    AppDialogActions,
    AppDialogContent,
    AppDialogTitle,
    cancelBtnSx,
    dangerBtnSx,
} from '../ui/AppDialog'
import {
    formatElapsed,
    formatTime12,
    useAttendanceActions,
    useAttendanceToday,
    useLiveElapsedMinutes,
} from '../../lib/hooks/useAttendance'

const FULL_DAY_MINUTES = 8 * 60

const GREEN = 'success.main'
const GREEN_HOVER = 'success.dark'
const AMBER = 'warning.main'
const RED = 'error.main'
const RED_HOVER = 'error.dark'

const solidBtnSx = (bg: string, hover: string) => ({
    bgcolor: bg,
    color: '#fff',
    textTransform: 'none' as const,
    fontSize: 12,
    fontWeight: 500,
    lineHeight: 1,
    minHeight: 0,
    minWidth: 0,
    px: '14px',
    py: '6px',
    borderRadius: '6px',
    boxShadow: 'none',
    whiteSpace: 'nowrap' as const,
    '&:hover': { bgcolor: hover, boxShadow: 'none' },
})

const ghostBtnSx = {
    bgcolor: 'transparent',
    color: 'text.secondary',
    border: '1px solid', borderColor: 'divider',
    textTransform: 'none' as const,
    fontSize: 12,
    fontWeight: 500,
    lineHeight: 1,
    minHeight: 0,
    minWidth: 0,
    px: '10px',
    py: '5px',
    borderRadius: '6px',
    boxShadow: 'none',
    whiteSpace: 'nowrap' as const,
    '&:hover': { bgcolor: 'background.paper', borderColor: 'divider', boxShadow: 'none' },
}

function clockLabel(clock: BreakClock): string {
    return clock.kind === 'remaining' ? `${formatClock(clock.seconds)} of break left`
        : clock.kind === 'over' ? `${formatClock(clock.seconds)} over break allowance`
            : `On break for ${formatClock(clock.seconds)}`
}

/**
 * The break, full screen: opened by the Break button (and by clicking the pill while
 * a break runs), over the page blurred behind it. Minimise drops back to the pill;
 * the break itself carries on until Resume.
 */
function BreakOverlay({ open, clock, allowanceMinutes, idle, since, workedLabel, resuming, onResume, onMinimise }: {
    open: boolean
    clock: BreakClock | null
    allowanceMinutes: number
    idle: boolean
    since: string | null
    workedLabel: string
    resuming: boolean
    onResume: () => void
    onMinimise: () => void
}) {
    const over = clock?.kind === 'over'
    const ringValue = !clock || allowanceMinutes <= 0 ? 0
        : over ? 100
            : Math.min(100, (clock.seconds / (allowanceMinutes * 60)) * 100)

    return (
        <Modal
            open={open}
            onClose={onMinimise}
            aria-labelledby="break-overlay-title"
            slotProps={{
                backdrop: {
                    sx: {
                        bgcolor: 'rgba(15, 23, 42, 0.55)',
                        backdropFilter: 'blur(6px)',
                        WebkitBackdropFilter: 'blur(6px)',
                    },
                },
            }}
        >
            <Box sx={{
                position: 'fixed', inset: 0,
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                gap: 3, px: 2, color: '#fff', textAlign: 'center', outline: 'none',
            }}>
                <Typography id="break-overlay-title" sx={{ fontSize: 22, fontWeight: 600, color: '#fff' }}>
                    {idle ? 'You are idle' : 'On break'}
                </Typography>

                <Box sx={{ position: 'relative', width: 260, height: 260, maxWidth: '70vw', maxHeight: '70vw' }}>
                    <CircularProgress
                        variant="determinate" value={100} size="100%" thickness={2}
                        sx={{ position: 'absolute', inset: 0, color: 'rgba(255,255,255,0.18)' }}
                    />
                    <CircularProgress
                        variant="determinate" value={ringValue} size="100%" thickness={2}
                        sx={{ position: 'absolute', inset: 0, color: over ? RED : AMBER }}
                    />
                    <Box sx={{
                        position: 'absolute', inset: 0,
                        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                    }}>
                        {clock && (
                            <Box
                                role="timer"
                                aria-label={clockLabel(clock)}
                                sx={{
                                    fontVariantNumeric: 'tabular-nums',
                                    fontWeight: 700,
                                    fontSize: { xs: 40, sm: 54 },
                                    lineHeight: 1.1,
                                    color: over ? RED : '#fff',
                                }}
                            >
                                {over ? '+' : ''}{formatClock(clock.seconds)}
                            </Box>
                        )}
                        <Typography sx={{ fontSize: 14, color: over ? RED : 'rgba(255,255,255,0.8)', fontWeight: over ? 600 : 400 }}>
                            {clock?.kind === 'remaining' ? 'left of your break'
                                : over ? 'over your break allowance'
                                    : 'on break'}
                        </Typography>
                    </Box>
                </Box>

                <Typography sx={{ fontSize: 14, color: 'rgba(255,255,255,0.8)' }}>
                    {since && <>Since {formatTime12(since)} · </>}{workedLabel} worked today
                </Typography>

                <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', justifyContent: 'center' }}>
                    <Button
                        disableElevation
                        disabled={resuming}
                        onClick={onResume}
                        startIcon={resuming ? <CircularProgress size={14} color="inherit" /> : null}
                        sx={{ ...solidBtnSx(GREEN, GREEN_HOVER), fontSize: 15, px: 4, py: 1.25, borderRadius: '8px' }}
                    >
                        {resuming ? 'Resuming…' : 'Resume work'}
                    </Button>
                    <Button
                        onClick={onMinimise}
                        sx={{
                            ...ghostBtnSx, color: '#fff', borderColor: 'rgba(255,255,255,0.4)',
                            fontSize: 15, px: 3, py: 1.25, borderRadius: '8px',
                            '&:hover': { bgcolor: 'rgba(255,255,255,0.1)', borderColor: '#fff' },
                        }}
                    >
                        Minimise
                    </Button>
                </Box>
            </Box>
        </Modal>
    )
}

/** The wall clock, ticking every second while `active`, so the break clock moves. */
function useSecondTicker(active: boolean): number {
    const [now, setNow] = useState(() => Date.now())
    useEffect(() => {
        if (!active) return
        const id = window.setInterval(() => setNow(Date.now()), 1000)
        return () => window.clearInterval(id)
    }, [active])
    return now
}

export default function AttendanceWidget({ enabled }: { enabled: boolean }) {
    const { data: today, isLoading } = useAttendanceToday(enabled)
    const { checkIn, checkOut, startBreak, endBreak, anyPending } = useAttendanceActions()
    const elapsed = useLiveElapsedMinutes(today)
    const [showEarlyCheckOutWarning, setShowEarlyCheckOutWarning] = useState(false)
    // Readable by every signed-in user (GET /api/settings): the break the day allows
    // for, which the clock counts down while a break runs.
    const { data: settings } = useQuery({ queryKey: queryKeys.appSettings, queryFn: getAppSettings, enabled })
    const onBreak = enabled && today?.status === 'break'
    const now = useSecondTicker(onBreak)
    const allowance = breakAllowanceMinutes(settings)
    const clock = onBreak && today ? breakClock(today, allowance, now) : null
    const [showBreakOverlay, setShowBreakOverlay] = useState(false)
    // A break that ends anywhere (the pill's Resume, another tab) closes it, so the
    // next idle auto-break does not open it unasked.
    const [wasOnBreak, setWasOnBreak] = useState(onBreak)
    if (wasOnBreak !== onBreak) {
        setWasOnBreak(onBreak)
        if (!onBreak) setShowBreakOverlay(false)
    }

    if (!enabled) return null

    function handleCheckOutClick() {
        if (elapsed < FULL_DAY_MINUTES) {
            setShowEarlyCheckOutWarning(true)
            return
        }
        checkOut.mutate()
    }

    function confirmEarlyCheckOut() {
        setShowEarlyCheckOutWarning(false)
        checkOut.mutate()
    }

    const status = today?.status ?? 'out'

    const bg = status === 'in' ? softBg('success') : status === 'break' ? softBg('warning') : 'action.hover'
    const border = status === 'in' ? '#A7F3D0' : status === 'break' ? '#FDE68A' : 'divider'
    const dotColor = status === 'in' ? GREEN : status === 'break' ? AMBER : 'text.disabled'

    return (
        <Box sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1.25,
            pl: '12px', pr: '4px', py: '4px',
            bgcolor: bg,
            border: `1px solid ${border}`,
            borderRadius: '8px',
            height: 38,
            flexShrink: 0,
        }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexShrink: 0 }}>
                <Box sx={{
                    width: 8, height: 8, borderRadius: '50%',
                    bgcolor: dotColor,
                    boxShadow: status === 'in' ? `0 0 0 3px rgba(34,196,122,0.2)` : 'none',
                    animation: status === 'in' ? 'pulse 2s infinite' : 'none',
                    '@keyframes pulse': {
                        '0%,100%': { opacity: 1 },
                        '50%': { opacity: 0.6 },
                    },
                    flexShrink: 0,
                }} />
                {isLoading && !today ? (
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>Loading…</Typography>
                ) : status === 'out' ? (
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>Not checked in</Typography>
                ) : status === 'in' ? (
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>
                        In since{' '}
                        <Box component="span" sx={{
                            fontVariantNumeric: 'tabular-nums',
                            fontWeight: 600,
                            color: 'text.primary',
                            fontSize: 13,
                        }}>
                            {today?.checkInAt ? formatTime12(today.checkInAt) : ''}
                        </Box>
                    </Typography>
                ) : status === 'break' ? (
                    <Typography
                        component="button"
                        type="button"
                        title="Show the break full screen"
                        onClick={() => setShowBreakOverlay(true)}
                        sx={{
                            fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap',
                            background: 'none', border: 0, p: 0, fontFamily: 'inherit', cursor: 'pointer',
                        }}
                    >
                        {today?.isAutoBreak ? 'Idle' : 'On break'}
                        {clock && (
                            <Box
                                component="span"
                                role="timer"
                                aria-label={clockLabel(clock)}
                                sx={{ ml: 0.75 }}
                            >
                                <Box component="span" sx={{
                                    fontVariantNumeric: 'tabular-nums',
                                    fontWeight: 700,
                                    fontSize: 13,
                                    color: clock.kind === 'over' ? RED : 'text.primary',
                                }}>
                                    {clock.kind === 'over' ? '+' : ''}{formatClock(clock.seconds)}
                                </Box>
                                {clock.kind === 'remaining' && ' left'}
                                {clock.kind === 'over' && (
                                    <Box component="span" sx={{ color: RED, fontWeight: 600 }}> over</Box>
                                )}
                            </Box>
                        )}
                    </Typography>
                ) : (
                    <Typography sx={{ fontSize: 12, color: 'text.secondary', whiteSpace: 'nowrap' }}>Done for today</Typography>
                )}
            </Box>

            {(status === 'in' || status === 'break') && (
                <>
                    <Box sx={{ width: '1px', height: 18, bgcolor: 'divider', flexShrink: 0 }} />
                    <Typography sx={{
                        fontSize: 11,
                        color: 'text.secondary',
                        fontVariantNumeric: 'tabular-nums',
                        whiteSpace: 'nowrap',
                        flexShrink: 0,
                    }}>
                        <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700, fontSize: 13 }}>
                            {formatElapsed(elapsed)}
                        </Box>
                        {status === 'break' && ' worked'}
                    </Typography>
                </>
            )}

            {status === 'out' && (
                <Button
                    disableElevation
                    disabled={anyPending}
                    onClick={() => checkIn.mutate()}
                    startIcon={checkIn.isPending ? <CircularProgress size={11} color="inherit" /> : null}
                    sx={solidBtnSx(GREEN, GREEN_HOVER)}
                >
                    {checkIn.isPending ? 'Checking in…' : 'Check In'}
                </Button>
            )}

            {status === 'in' && (
                <Box sx={{ display: 'flex', gap: '6px', flexShrink: 0 }}>
                    <Button
                        disableElevation
                        disabled={anyPending}
                        onClick={() => {
                            setShowBreakOverlay(true)
                            startBreak.mutate()
                        }}
                        startIcon={startBreak.isPending ? <CircularProgress size={11} color="inherit" /> : null}
                        sx={ghostBtnSx}
                    >
                        Break
                    </Button>
                    <Button
                        disableElevation
                        disabled={anyPending}
                        onClick={handleCheckOutClick}
                        startIcon={checkOut.isPending ? <CircularProgress size={11} color="inherit" /> : null}
                        sx={solidBtnSx(RED, RED_HOVER)}
                    >
                        {checkOut.isPending ? 'Checking out…' : 'Check Out'}
                    </Button>
                </Box>
            )}

            {status === 'break' && (
                <Button
                    disableElevation
                    disabled={anyPending}
                    onClick={() => endBreak.mutate()}
                    startIcon={endBreak.isPending ? <CircularProgress size={11} color="inherit" /> : null}
                    sx={solidBtnSx(GREEN, GREEN_HOVER)}
                >
                    {endBreak.isPending ? 'Resuming…' : 'Resume'}
                </Button>
            )}

            <BreakOverlay
                open={showBreakOverlay && onBreak}
                clock={clock}
                allowanceMinutes={allowance}
                idle={!!today?.isAutoBreak}
                since={today?.onBreakSince ?? null}
                workedLabel={formatElapsed(elapsed)}
                resuming={endBreak.isPending}
                onResume={() => endBreak.mutate(undefined, { onSuccess: () => setShowBreakOverlay(false) })}
                onMinimise={() => setShowBreakOverlay(false)}
            />

            <AppDialog open={showEarlyCheckOutWarning} onClose={() => setShowEarlyCheckOutWarning(false)} maxWidth="xs">
                <AppDialogTitle>Check out early?</AppDialogTitle>
                <AppDialogContent>
                    <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>
                        You've only worked <strong>{formatElapsed(elapsed)}</strong> today, less than a full 8-hour day.
                        Are you sure you want to check out?
                    </Typography>
                </AppDialogContent>
                <AppDialogActions>
                    <Button size="small" variant="outlined" sx={cancelBtnSx} onClick={() => setShowEarlyCheckOutWarning(false)}>
                        Cancel
                    </Button>
                    <Button size="small" variant="contained" sx={dangerBtnSx} onClick={confirmEarlyCheckOut}>
                        Check Out Anyway
                    </Button>
                </AppDialogActions>
            </AppDialog>
        </Box>
    )
}
