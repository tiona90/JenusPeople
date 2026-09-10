import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import IconButton from '@mui/material/IconButton'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import { createChild, deleteChild, getChildren } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'

interface ChildrenSectionProps {
    /** null means the employee has never answered. */
    hasChildren: boolean | null | undefined
    onHasChildrenChange: (value: boolean) => void
    disabled?: boolean
}

/**
 * Children on the employee's own profile. What paternity leave is measured
 * against: 18 weeks per child, until that child turns 15.
 *
 * The rows commit immediately, while the "I have children" checkbox saves with the
 * rest of the profile. That split is deliberate — each child is its own resource,
 * and batching them into the dialog's Save would mean building a diff-and-sync
 * command for very little gain. Rows therefore show their own pending state.
 */
export default function ChildrenSection({ hasChildren, onHasChildrenChange, disabled }: ChildrenSectionProps) {
    const queryClient = useQueryClient()
    const [isAdding, setIsAdding] = useState(false)
    const [name, setName] = useState('')
    const [dateOfBirth, setDateOfBirth] = useState('')

    const declared = hasChildren === true

    const { data: children, isLoading, isError: isChildrenError, error: childrenError } = useQuery({
        queryKey: ['children'],
        queryFn: () => getChildren(),
        enabled: declared,
    })

    const invalidate = () => {
        void queryClient.invalidateQueries({ queryKey: ['children'] })
        // The picker on the leave form reads the ledger, not the list.
        void queryClient.invalidateQueries({ queryKey: ['childLeaveEntitlements'] })
    }

    const addMutation = useMutation({
        mutationFn: () => createChild({ name: name.trim(), dateOfBirth }),
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

    return (
        <Stack spacing={1}>
            <FormControlLabel
                control={
                    <Checkbox
                        checked={declared}
                        onChange={(event) => onHasChildrenChange(event.target.checked)}
                        disabled={disabled}
                    />
                }
                label="I have children"
            />

            {declared && (
                <Box sx={{ pl: 1 }}>
                    {isLoading && <CircularProgress size={18} />}

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
                            <TextField
                                label="Date of birth"
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
                                    onClick={() => addMutation.mutate()}
                                    disabled={!canSaveNew || addMutation.isPending}
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
