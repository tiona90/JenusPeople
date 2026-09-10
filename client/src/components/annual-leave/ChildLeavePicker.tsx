import { useQuery } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { getChildLeaveEntitlements } from '../../lib/api'
import type { ChildLeaveEntitlement } from '../../lib/types'

interface ChildLeavePickerProps {
    value: string
    onChange: (childId: string) => void
    /** An admin filing on behalf of someone else. Omitted for one's own request. */
    employeeId?: string
    /** Business days the chosen dates come to, or null while they are incomplete. */
    requestedDays: number | null
    error?: string
    disabled?: boolean
}

const BUSINESS_DAYS_PER_WEEK = 5

function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
}

function weeks(days: number) {
    return (Math.round((days / BUSINESS_DAYS_PER_WEEK) * 10) / 10).toFixed(1)
}

function optionLabel(child: ChildLeaveEntitlement) {
    return child.isEligible
        ? `${child.name} — ${child.remainingDays} of ${child.totalDays} days left · ${child.thisYearRemainingDays} left this year`
        : `${child.name} — turned ${child.ageYears} on ${formatDate(child.lastEligibleDate)} (no longer eligible)`
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
    value, onChange, employeeId, requestedDays, error, disabled,
}: ChildLeavePickerProps) {
    const { data, isLoading } = useQuery({
        queryKey: ['childLeaveEntitlements', employeeId ?? 'me'],
        queryFn: () => getChildLeaveEntitlements(employeeId),
    })

    const children = data?.children ?? []
    const hasEligible = children.some((child) => child.isEligible)
    const selected = children.find((child) => child.childId === value)

    if (!isLoading && children.length === 0) {
        return (
            <Alert severity="info">
                Add your children in Edit profile to request this leave — the entitlement
                is per child.
            </Alert>
        )
    }

    if (!isLoading && !hasEligible) {
        return (
            <Alert severity="warning">
                No eligible children. This leave is available only while a child is under
                15.
            </Alert>
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
                        {optionLabel(child)}
                    </MenuItem>
                ))}
            </TextField>

            {selected && requestedDays !== null && requestedDays > 0 && (
                <Typography variant="caption" color="text.secondary">
                    {`This request: ${requestedDays} business days (${weeks(requestedDays)} weeks) · `}
                    {`${selected.name}: ${Math.max(0, selected.remainingDays - requestedDays)} days left`}
                </Typography>
            )}
        </Stack>
    )
}
