import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { getChildLeaveEntitlements } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { ChildLeaveEntitlement } from '../../lib/types'

interface ChildLeavePickerProps {
    value: string
    onChange: (childId: string) => void
    /** Whose ledger to load. Omitted to load the signed-in user's own. */
    employeeId?: string
    /**
     * The age at which a child stops being eligible, from the leave type's
     * `childEligibleUntilAge`. Required rather than defaulted: the whole premise of
     * the feature is that this number is configurable, so a hard-coded 15 in here
     * would contradict the configuration the server enforces against.
     */
    childEligibleUntilAge: number
    /**
     * Which per-child ledger to read. Maternity and Paternity Leave each configure
     * their own weeks and their own cut-off age, so the ledger has to follow the
     * type being requested — without it the server resolves one winner for the
     * whole application and quotes its policy for both. Undefined for a type that
     * carries no per-child entitlement of its own, which is the server's own
     * fallback.
     */
    leaveTypeId?: number
    /**
     * The employee this request is *for*, when that is somebody other than the
     * signed-in user — an admin filing or editing on their behalf. Used only for
     * wording: "add your children" is nonsense to an admin, who has no screen for
     * editing another person's children (by design). Not derivable from
     * `employeeId`, which is also set when a user views their own request.
     */
    onBehalfOfName?: string
    /** Business days the chosen dates come to, or null while they are incomplete. */
    requestedDays: number | null
    error?: string
    /**
     * Fires whenever the picker cannot currently offer a real choice — the query
     * failed, the employee has no children on file, or none are eligible — so the
     * form can disable submit and explain the blocked state without re-running
     * this query itself.
     */
    onBlockedChange?: (blocked: boolean) => void
}

const BUSINESS_DAYS_PER_WEEK = 5

/**
 * Formatted in UTC: `lastEligibleDate` is a date-only value, which `Date` parses as
 * UTC midnight, so formatting it in a timezone behind UTC would render the day before.
 */
function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', {
        day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC',
    })
}

/**
 * The qualifying birthday, derived the way the server derives it in reverse:
 * `lastEligibleDate` is the day *before* it (see `PerChildLeaveCalculationService`).
 */
function qualifyingBirthday(lastEligibleDate: string) {
    const date = new Date(`${lastEligibleDate.slice(0, 10)}T00:00:00Z`)
    date.setUTCDate(date.getUTCDate() + 1)
    return date.toISOString().slice(0, 10)
}

function weeks(days: number) {
    return (Math.round((days / BUSINESS_DAYS_PER_WEEK) * 10) / 10).toFixed(1)
}

/**
 * The child a per-child leave request is for — paternity leave in practice, where
 * the entitlement is 18 weeks per child until that child turns 15.
 *
 * Ineligible children are listed and disabled rather than filtered out. An employee
 * who cannot find their child in the list assumes the record is missing; one who
 * sees them greyed out with a date understands why.
 *
 * Every figure here is advisory. The server re-checks both caps on create and again
 * on approval, and its message is the one that counts.
 */
export default function ChildLeavePicker({
    value, onChange, employeeId, childEligibleUntilAge, leaveTypeId, onBehalfOfName,
    requestedDays, error, onBlockedChange,
}: ChildLeavePickerProps) {
    const { data, isLoading, isError, error: queryError } = useQuery({
        queryKey: ['childLeaveEntitlements', employeeId ?? 'me', leaveTypeId ?? null],
        queryFn: () => getChildLeaveEntitlements(employeeId, leaveTypeId),
    })

    const children = data?.children ?? []
    const eligible = children.filter((child) => child.isEligible)
    const hasEligible = eligible.length > 0
    const selected = children.find((child) => child.childId === value)

    /* The cards are read-only. They exist to be read — each child's entitlement
       and how long it lasts — not pressed.

       A per-child request still has to name one child: the server requires it
       and the ledger is keyed by it. So the picker chooses, and it chooses the
       eligible child whose entitlement expires soonest, on the reasoning that an
       approaching birthday is the only way this entitlement is ever lost. Ties
       go to whoever has the most left. The chosen card is the highlighted one,
       so the choice is visible rather than silent.

       The cost, stated plainly because it is real: an employee with two eligible
       children can no longer aim a request at the other one from this screen. */
    const chargedTo = [...eligible].sort((a, b) =>
        a.lastEligibleDate.localeCompare(b.lastEligibleDate) || b.remainingDays - a.remainingDays,
    )[0] ?? null

    // A failed query lands here too (data undefined -> no children), which is
    // exactly the state a blocked submit needs to guard against as well.
    const blocked = !isLoading && (children.length === 0 || !hasEligible)

    useEffect(() => {
        onBlockedChange?.(blocked)
    }, [blocked, onBlockedChange])

    // A value that matches nothing once the ledger has *successfully* loaded is
    // stale — most often an admin who switched the employee after picking a
    // child for the previous one, or a switch between the two parental types,
    // whose ledgers are separate. No card would render as selected, so clearing
    // it keeps the stored value and the visible grid saying the same thing.
    // Deliberately excludes a failed query: a transient error should not wipe a
    // value that may still be valid once the request succeeds.
    useEffect(() => {
        if (!isLoading && !isError && value && !selected) {
            onChange('')
        }
    }, [isLoading, isError, value, selected, onChange])

    // The other half of read-only cards: having stopped asking, the picker has
    // to answer for itself, or the form waits forever for a child nothing can
    // now supply.
    const chargedToId = chargedTo?.childId ?? null
    useEffect(() => {
        if (!isLoading && !isError && !value && chargedToId) {
            onChange(chargedToId)
        }
    }, [isLoading, isError, value, chargedToId, onChange])

    if (isError) {
        return (
            <Stack spacing={0.5}>
                <Alert severity="error">
                    {getApiErrorMessage(queryError, 'Unable to load children right now. Please try again.')}
                </Alert>
                {error && (
                    <Typography variant="caption" color="error">
                        {error}
                    </Typography>
                )}
            </Stack>
        )
    }

    if (!isLoading && children.length === 0) {
        return (
            <Stack spacing={0.5}>
                <Alert severity="info">
                    {onBehalfOfName
                        // An admin can now fix this themselves, from the Profile
                        // section of Edit User on the Users panel — so say where,
                        // rather than only that somebody else has to do it.
                        ? `${onBehalfOfName} has no children on file. The entitlement is per child, so add them under Users → Edit User → Profile, or ask them to add them in Edit profile.`
                        : 'Add your children in Edit profile to request this leave — the entitlement is per child.'}
                </Alert>
                {error && (
                    <Typography variant="caption" color="error">
                        {error}
                    </Typography>
                )}
            </Stack>
        )
    }

    if (!isLoading && !hasEligible) {
        return (
            <Stack spacing={0.5}>
                <Alert severity="warning">
                    {`No eligible children. This leave is available only while a child is under ${childEligibleUntilAge}.`}
                </Alert>
                {error && (
                    <Typography variant="caption" color="error">
                        {error}
                    </Typography>
                )}
            </Stack>
        )
    }

    return (
        <Stack spacing={0.75}>
            {/* No asterisk: it came from the dropdown, where a required select
                needs marking. A grid that unlocks submit only once a card is
                chosen — and a submit button that says "Select a child to
                continue" — states the requirement better than a symbol. */}
            <Box sx={{ fontSize: 12, fontWeight: 600, color: error ? 'error.main' : 'text.secondary' }}>
                Child
            </Box>

            {/* A grid rather than a select: every child's entitlement and how long
                they go on qualifying is readable at once, which is the comparison
                an employee is actually making. Laid out like the leave-type cards
                directly above so Step 1 reads as one thing. */}
            <Box sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' },
                gap: '10px',
            }}>
                {children.map((child) => (
                    <ChildCard
                        key={child.childId}
                        child={child}
                        childEligibleUntilAge={childEligibleUntilAge}
                        selected={child.childId === value}
                    />
                ))}
            </Box>

            {error && (
                <Typography variant="caption" color="error">
                    {error}
                </Typography>
            )}

            {selected && requestedDays !== null && requestedDays > 0 && (() => {
                // Neither cap alone tells the whole story: a request can clear the
                // lifetime remainder yet still bust the yearly one (or vice versa).
                // Quote whichever is tighter so the figure never over-promises.
                const cappedRemaining = Math.min(selected.remainingDays, selected.thisYearRemainingDays)
                return (
                    <Typography variant="caption" color="text.secondary">
                        {`This request: ${requestedDays} business days (${weeks(requestedDays)} weeks) · `}
                        {`${selected.name}: ${Math.max(0, cappedRemaining - requestedDays)} days left`}
                    </Typography>
                )
            })()}
        </Stack>
    )
}

/**
 * One child, as a card. Mirrors `LeaveTypeCard` on the apply page — same border,
 * same tinted selected state — because the two grids sit one above the other.
 *
 * A card is a button only when there is something to decide: an aged-out child
 * never is, and no child is when only one qualifies, because the picker has
 * already chosen them. In every other case the card is a plain element, so there
 * is nothing to click, nothing in the tab order, and no pointer cursor inviting
 * a try. Ineligible children stay on the grid regardless — an employee who
 * cannot find their child concludes the record is missing, not that the child
 * grew up.
 */
function ChildCard({
    child, childEligibleUntilAge, selected,
}: {
    child: ChildLeaveEntitlement
    childEligibleUntilAge: number
    /** Whether this is the child the request is charged to. */
    selected: boolean
}) {
    const used = Math.max(0, child.totalDays - child.remainingDays)
    const pct = child.totalDays > 0 ? Math.min(100, (used / child.totalDays) * 100) : 0
    const low = pct >= 80

    /* `available` is about the child — do they still qualify? Nothing here is
       about whether it can be clicked, because nothing can: the cards are read
       only. An eligible child therefore looks live, chosen or not; an aged-out
       one is muted and loses the card shape entirely, so a bordered panel never
       sits beside a bordered card pretending to be one.

       `aria-current` rather than `aria-pressed`: the card is no longer a button,
       and "this is the one in use" is what it now means. */
    const available = child.isEligible

    return (
        <Box
            component="div"
            {...(selected ? { 'aria-current': true } : {})}
            sx={{
                borderRadius: '10px', p: '12px',
                textAlign: 'left', fontFamily: 'inherit', cursor: 'default',
                /* Every eligible child is drawn the same. The charged one was
                   picked out first by a green tint and then by a firmer border,
                   and both read as "you selected this" on a grid where nothing
                   can be selected — the emphasis was answering a question the
                   employee had not been asked. Which child the request is
                   charged to is still announced through `aria-current` and named
                   in the caption below once the dates are in. */
                ...(available
                    ? {
                        border: '1px solid',
                        borderColor: 'divider',
                        bgcolor: 'background.paper',
                    }
                    /* Deliberately not card-shaped. A bordered panel beside a
                       bordered card reads as another card however faint it is, so
                       this drops the border entirely for a flat inset block — the
                       shape says "note", not "choice", before any of the text is
                       read. */
                    : {
                        border: 'none',
                        bgcolor: 'action.hover',
                        color: 'text.disabled',
                        cursor: 'default',
                    }),
            }}
        >
            <Box sx={{ display: 'flex', alignItems: 'center', gap: '6px', mb: '6px' }}>
                <Box component="span" sx={{ fontSize: 18, lineHeight: 1, opacity: available ? 1 : 0.4 }}>👶</Box>
                {/* No tick. The border and tint already say which card is chosen,
                    exactly as they do on the leave-type grid above — a tick was a
                    second marker for the same state, on only one of the two grids.
                    aria-pressed is what announces it to assistive tech. */}
                <Box component="span" sx={{
                    fontSize: 13, fontWeight: 600, flex: 1,
                    color: available ? 'text.primary' : 'text.disabled',
                }}>
                    {child.name}
                </Box>
            </Box>

            <Box sx={{ fontSize: 11, color: available ? 'text.secondary' : 'text.disabled', mb: '6px' }}>
                {`Age ${child.ageYears}`}
            </Box>

            {child.isEligible ? (
                <>
                    <Box sx={{ fontSize: 11, color: 'text.primary', fontWeight: 600 }}>
                        {`${child.remainingDays} of ${child.totalDays} days left`}
                    </Box>
                    <Box sx={{ height: 3, bgcolor: 'divider', borderRadius: '2px', mt: '5px', overflow: 'hidden' }}>
                        <Box sx={{
                            height: '100%', borderRadius: '2px', width: `${pct}%`,
                            bgcolor: pct >= 100 ? 'error.main' : low ? 'warning.main' : 'success.main',
                        }} />
                    </Box>
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '5px' }}>
                        {`${child.thisYearRemainingDays} left this year`}
                    </Box>
                    {/* The dates half: how long this child goes on qualifying at all. */}
                    <Box sx={{ fontSize: 11, color: 'text.secondary', mt: '2px' }}>
                        {`Available until ${formatDate(child.lastEligibleDate)}`}
                    </Box>
                </>
            ) : (
                <Box sx={{ fontSize: 11, color: 'text.disabled' }}>
                    {`Turned ${childEligibleUntilAge} on ${formatDate(qualifyingBirthday(child.lastEligibleDate))} — no longer eligible`}
                </Box>
            )}
        </Box>
    )
}
