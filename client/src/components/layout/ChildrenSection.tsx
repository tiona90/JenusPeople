import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import IconButton from '@mui/material/IconButton'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import { createChild, deleteChild, getChildren } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'
import type { UpsertChildRequest } from '../../lib/types'

interface ChildrenSectionProps {
    /**
     * null means the employee has never answered. Omitted together with
     * `onHasChildrenChange` when somebody else is managing the list — see below.
     */
    hasChildren?: boolean | null
    /**
     * Reports the answer to the parent form, which saves it with the rest of the
     * profile. Omitting it puts the section in on-behalf mode: the list is managed
     * but the declaration is not asked, because "do you have children" is the
     * employee's own statement to make. The server records it anyway — adding a
     * child sets `HasChildren` on the profile (see `CreateChild`).
     */
    onHasChildrenChange?: (value: boolean) => void
    /**
     * Whose children these are. Omitted for the signed-in user's own; an admin
     * editing someone else passes that person's **user** id. The server authorizes
     * it either way — passing an id is not permission to use it.
     */
    employeeId?: string
    /** That person's name, for wording that would be nonsense in the first person. */
    onBehalfOfName?: string
    /**
     * Create mode: there is no account yet, so there is no profile for a child to
     * belong to and nothing to POST against. Supplying these makes the section
     * entirely local — it queries nothing and writes nothing — and the caller
     * writes the collected rows once the account exists.
     */
    pendingChildren?: UpsertChildRequest[]
    onPendingChildrenChange?: (children: UpsertChildRequest[]) => void
    disabled?: boolean
}

/**
 * Children on the employee's own profile. What the per-child leave types are
 * measured against — Maternity and Paternity Leave, each granting an entitlement
 * per child until that child reaches the configured age.
 *
 * The question is **Yes / No with neither preselected**, not a checkbox, because
 * the stored answer is a tri-state: null means never answered, false means
 * declared none. A checkbox has nowhere to put that difference — it renders
 * someone who has never been asked identically to someone who answered No, and
 * unchecking it sends a "no children" declaration the employee never made.
 *
 * The rows commit immediately, while the Yes/No answer saves with the rest of the
 * profile. That split is deliberate — each child is its own resource, and batching
 * them into the dialog's Save would mean building a diff-and-sync command for very
 * little gain. Rows therefore show their own pending state.
 */
export default function ChildrenSection({
    hasChildren, onHasChildrenChange, employeeId, onBehalfOfName,
    pendingChildren, onPendingChildrenChange, disabled,
}: ChildrenSectionProps) {
    const queryClient = useQueryClient()
    const [isAdding, setIsAdding] = useState(false)
    const [name, setName] = useState('')
    const [dateOfBirth, setDateOfBirth] = useState('')

    /* Whoever manages the declaration also gates the list behind it. On behalf of
       somebody else there is no declaration to ask, so the list always shows —
       an admin opening the dialog needs to see what is on file, not be told to
       answer a question about their own family first. */
    const managesDeclaration = onHasChildrenChange !== undefined
    const declared = managesDeclaration ? hasChildren === true : true

    /* Create mode: nothing to read and nothing to write yet. The rows live in the
       caller's state until the account they belong to exists. */
    const collectsLocally = onPendingChildrenChange !== undefined
    const pending = pendingChildren ?? []

    const { data: children, isLoading, isError: isChildrenError, error: childrenError } = useQuery({
        // Keyed by whose list it is, so an admin moving between two employees does
        // not read the previous one's children out of the cache. The invalidations
        // below stay prefix-only, which clears every key under it.
        queryKey: ['children', employeeId ?? 'me'],
        queryFn: () => getChildren(employeeId),
        enabled: declared && !collectsLocally,
    })

    const invalidate = () => {
        void queryClient.invalidateQueries({ queryKey: ['children'] })
        // The picker on the leave form reads the ledger, not the list.
        void queryClient.invalidateQueries({ queryKey: ['childLeaveEntitlements'] })
    }

    const addMutation = useMutation({
        mutationFn: () => createChild({ name: name.trim(), dateOfBirth }, employeeId),
        onSuccess: () => {
            invalidate()
            setIsAdding(false)
            setName('')
            setDateOfBirth('')
        },
    })

    const removeMutation = useMutation({
        mutationFn: (id: string) => deleteChild(id),
        onSuccess: invalidate,
    })

    // Reopening the form after a failed attempt, or backing out of one, should not
    // leave the previous failure's Alert on screen with no form left to explain it.
    const openAdd = () => {
        addMutation.reset()
        removeMutation.reset()
        setIsAdding(true)
    }

    const cancelAdd = () => {
        addMutation.reset()
        removeMutation.reset()
        setIsAdding(false)
    }

    const today = new Date().toISOString().slice(0, 10)
    const error = addMutation.error ?? removeMutation.error ?? (isChildrenError ? childrenError : undefined)
    const canSaveNew = name.trim().length > 0 && dateOfBirth.length > 0

    /** Saves the new row: to the caller's list in create mode, to the API otherwise. */
    const saveNew = () => {
        if (!collectsLocally) {
            addMutation.mutate()
            return
        }

        onPendingChildrenChange!([...pending, { name: name.trim(), dateOfBirth }])
        setIsAdding(false)
        setName('')
        setDateOfBirth('')
    }

    /* The server refuses "no children" while children are still on the profile
       (HasChildrenDeclaration), so No is disabled rather than left to fail on save
       — the employee can see what to do about it instead of being told afterwards. */
    const childCount = collectsLocally ? pending.length : (children?.length ?? 0)
    const cannotAnswerNo = declared && childCount > 0
    const unanswered = hasChildren !== true && hasChildren !== false

    return (
        <Stack spacing={1}>
            {!managesDeclaration && (
                <Typography variant="subtitle2" color="text.secondary">
                    {onBehalfOfName ? `${onBehalfOfName}'s children` : 'Children'}
                </Typography>
            )}

            {managesDeclaration && (
            <Box>
                <Typography variant="subtitle2" color="text.secondary">
                    Do you have children?
                </Typography>
                <RadioGroup
                    row
                    name="has-children"
                    /* Empty string when unanswered, so neither option is selected —
                       the whole point of asking rather than defaulting. */
                    value={hasChildren === true ? 'yes' : hasChildren === false ? 'no' : ''}
                    onChange={(event) => onHasChildrenChange(event.target.value === 'yes')}
                >
                    <FormControlLabel value="yes" control={<Radio />} label="Yes" disabled={disabled} />
                    <FormControlLabel
                        value="no"
                        control={<Radio />}
                        label="No"
                        disabled={disabled || cannotAnswerNo}
                    />
                </RadioGroup>

                {cannotAnswerNo && (
                    <Typography variant="caption" color="text.secondary">
                        {`Remove the ${childCount} ${childCount === 1 ? 'child' : 'children'} below before answering No.`}
                    </Typography>
                )}

                {unanswered && (
                    <Typography variant="caption" color="text.secondary">
                        Needed for maternity and paternity leave, which are granted per child.
                    </Typography>
                )}
            </Box>
            )}

            {declared && (
                <Box sx={{ pl: 1 }}>
                    {isLoading && <CircularProgress size={18} />}

                    {/* Only on behalf of somebody else: in the employee's own dialog
                        an empty list follows a Yes they just gave, so it needs no
                        explaining. An admin opening a stranger's record does. */}
                    {!managesDeclaration && !isLoading && !isChildrenError && childCount === 0 && (
                        <Typography variant="caption" color="text.secondary">
                            No children on file. Add them here so maternity or paternity leave can be requested.
                        </Typography>
                    )}

                    {/* Create mode. No age or eligibility chip: both are the server's
                        to compute, and guessing them here would be a second
                        implementation of the rule that decides who may take this
                        leave. They appear as soon as the account exists. */}
                    {collectsLocally && pending.map((child, index) => (
                        <Stack
                            key={`${child.name}-${child.dateOfBirth}-${index}`}
                            direction="row"
                            spacing={1}
                            alignItems="center"
                            sx={{ py: 0.5 }}
                        >
                            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                                <Typography variant="body2" fontWeight={600} noWrap>
                                    {child.name}
                                </Typography>
                                <Typography variant="caption" color="text.secondary">
                                    {new Date(child.dateOfBirth).toLocaleDateString('en-GB', {
                                        day: '2-digit', month: 'short', year: 'numeric',
                                    })}
                                </Typography>
                            </Box>

                            <IconButton
                                size="small"
                                aria-label={`Remove ${child.name}`}
                                onClick={() => onPendingChildrenChange!(pending.filter((_, i) => i !== index))}
                                disabled={disabled}
                            >
                                <DeleteOutlineRoundedIcon fontSize="small" />
                            </IconButton>
                        </Stack>
                    ))}

                    {children?.map((child) => (
                        <Stack
                            key={child.id}
                            direction="row"
                            spacing={1}
                            alignItems="center"
                            sx={{ py: 0.5 }}
                        >
                            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                                <Typography variant="body2" fontWeight={600} noWrap>
                                    {child.name}
                                </Typography>
                                <Typography variant="caption" color="text.secondary">
                                    {`${new Date(child.dateOfBirth).toLocaleDateString('en-GB', {
                                        day: '2-digit', month: 'short', year: 'numeric',
                                    })} · Age ${child.ageYears}`}
                                </Typography>
                            </Box>

                            <Chip
                                size="small"
                                label={child.isEligible ? 'Eligible' : 'No longer eligible'}
                                sx={child.isEligible
                                    ? { bgcolor: softBg('success'), color: 'success.dark' }
                                    : { color: 'text.secondary' }}
                            />

                            <IconButton
                                size="small"
                                aria-label={`Remove ${child.name}`}
                                onClick={() => removeMutation.mutate(child.id)}
                                disabled={disabled || removeMutation.isPending}
                            >
                                <DeleteOutlineRoundedIcon fontSize="small" />
                            </IconButton>
                        </Stack>
                    ))}

                    {isAdding ? (
                        <Stack spacing={1} sx={{ mt: 1 }}>
                            <TextField
                                label="Child's name"
                                value={name}
                                onChange={(event) => setName(event.target.value)}
                                size="small"
                                fullWidth
                            />
                            {/* "Child's date of birth", not "Date of birth": both dialogs
                                this section appears in already have a Date of birth field
                                for the person themselves, and two identically labelled
                                date inputs are ambiguous — to a screen reader as much as
                                to anyone reading quickly. Matches "Child's name" above. */}
                            <TextField
                                label="Child's date of birth"
                                type="date"
                                value={dateOfBirth}
                                onChange={(event) => setDateOfBirth(event.target.value)}
                                slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
                                size="small"
                                fullWidth
                            />
                            <Stack direction="row" spacing={1}>
                                <Button
                                    variant="contained"
                                    size="small"
                                    onClick={saveNew}
                                    disabled={!canSaveNew || addMutation.isPending || disabled}
                                    sx={{ textTransform: 'none' }}
                                >
                                    Save child
                                </Button>
                                <Button
                                    variant="outlined"
                                    size="small"
                                    onClick={cancelAdd}
                                    disabled={addMutation.isPending}
                                    sx={{ textTransform: 'none' }}
                                >
                                    Cancel
                                </Button>
                            </Stack>
                        </Stack>
                    ) : (
                        <Button
                            size="small"
                            onClick={openAdd}
                            disabled={disabled}
                            sx={{ textTransform: 'none', mt: 0.5 }}
                        >
                            Add child
                        </Button>
                    )}

                    {error && (
                        <Alert severity="error" sx={{ mt: 1 }}>
                            {getApiErrorMessage(
                                error,
                                isChildrenError && !addMutation.error && !removeMutation.error
                                    ? 'Unable to load children.'
                                    : 'Unable to update children.',
                            )}
                        </Alert>
                    )}
                </Box>
            )}
        </Stack>
    )
}
