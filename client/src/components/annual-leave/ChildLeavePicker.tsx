import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
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
    disabled?: boolean
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
 * The aged-out half quotes the *configured* cut-off age against the birthday the
 * child reached it on — not the child's age today against the day before that
 * birthday, which was two errors in one phrase: a child now 17 read "turned 17 on
 * 28 Feb 2027" when they turned 15 on 1 Mar 2027. The server's own refusal message
 * ("<Name> turns 15 on <date>") states it this way.
 */
function optionLabel(child: ChildLeaveEntitlement, childEligibleUntilAge: number) {
    return child.isEligible
        ? `${child.name} — ${child.remainingDays} of ${child.totalDays} days left · ${child.thisYearRemainingDays} left this year`
        : `${child.name} — turned ${childEligibleUntilAge} on ${formatDate(qualifyingBirthday(child.lastEligibleDate))} (no longer eligible)`
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
    value, onChange, employeeId, childEligibleUntilAge, onBehalfOfName,
    requestedDays, error, disabled, onBlockedChange,
}: ChildLeavePickerProps) {
    const { data, isLoading, isError, error: queryError } = useQuery({
        queryKey: ['childLeaveEntitlements', employeeId ?? 'me'],
        queryFn: () => getChildLeaveEntitlements(employeeId),
    })

    const children = data?.children ?? []
    const hasEligible = children.some((child) => child.isEligible)
    const selected = children.find((child) => child.childId === value)

    // A failed query lands here too (data undefined -> no children), which is
    // exactly the state a blocked submit needs to guard against as well.
    const blocked = !isLoading && (children.length === 0 || !hasEligible)

    useEffect(() => {
        onBlockedChange?.(blocked)
    }, [blocked, onBlockedChange])

    // A value that matches nothing once the ledger has *successfully* loaded is
    // stale — most often an admin who switched the employee after picking a
    // child for the previous one. The select below already hides it to avoid a
    // MUI out-of-range warning; this re-couples the stored value to the
    // displayed one so the mismatch cannot be silently submitted. Deliberately
    // excludes a failed query: a transient error should not wipe a value that
    // may still be valid once the request succeeds.
    useEffect(() => {
        if (!isLoading && !isError && value && !selected) {
            onChange('')
        }
    }, [isLoading, isError, value, selected, onChange])

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
        <Stack spacing={0.5}>
            <TextField
                select
                required
                label="Child"
                /* Only once the options exist. While the ledger is still loading
                   there are no MenuItems, and a value with no matching option makes
                   MUI warn about an out-of-range select. */
                value={selected ? value : ''}
                onChange={(event) => onChange(event.target.value)}
                error={!!error}
                helperText={error}
                disabled={disabled || isLoading}
                fullWidth
            >
                {children.map((child) => (
                    <MenuItem key={child.childId} value={child.childId} disabled={!child.isEligible}>
                        {optionLabel(child, childEligibleUntilAge)}
                    </MenuItem>
                ))}
            </TextField>

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
