import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import { resetPassword } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'

const resetPasswordSchema = z.object({
    email: z.string().min(1, 'Email is required.').email('Enter a valid email address.'),
    newPassword: z.string().min(6, 'Use at least 6 characters.'),
    confirmPassword: z.string().min(1, 'Please confirm your password.'),
}).refine((data) => data.newPassword === data.confirmPassword, {
    message: 'Passwords do not match.',
    path: ['confirmPassword'],
})
type ResetPasswordValues = z.infer<typeof resetPasswordSchema>

const inputSx = {
    '& .MuiOutlinedInput-root': {
        borderRadius: '8px',
        bgcolor: '#fff',
        fontSize: 13,
        '& fieldset': { borderColor: '#D1D5DB', borderWidth: '1.5px' },
        '&:hover fieldset': { borderColor: '#9CA3AF', borderWidth: '1.5px' },
        '&.Mui-focused': { boxShadow: '0 0 0 3px rgba(79,142,247,0.12)' },
        '&.Mui-focused fieldset': { borderColor: '#4F8EF7', borderWidth: '1.5px' },
    },
    '& .MuiInputLabel-root': { fontSize: 12, fontWeight: 500, color: '#374151' },
    '& .MuiInputLabel-root.Mui-focused': { color: '#4F8EF7' },
} as const

interface ResetPasswordFormProps {
    onBackToLogin: () => void
    onRequestNewLink: () => void
}

function ResetPasswordForm({ onBackToLogin, onRequestNewLink }: ResetPasswordFormProps) {
    const searchParams = useMemo(() => new URLSearchParams(window.location.search), [])
    const initialEmail = searchParams.get('email') ?? ''
    const initialToken = searchParams.get('token') ?? ''
    const hasValidLink = Boolean(initialEmail && initialToken)

    const [showNew, setShowNew] = useState(false)
    const [showConfirm, setShowConfirm] = useState(false)

    const { register, handleSubmit, formState: { errors } } = useForm<ResetPasswordValues>({
        resolver: zodResolver(resetPasswordSchema),
        defaultValues: { email: initialEmail, newPassword: '', confirmPassword: '' },
    })

    const mutation = useMutation({
        mutationFn: resetPassword,
        onSuccess: () => {
            window.history.replaceState({}, document.title, `${window.location.pathname}#login`)
        },
    })

    const onSubmit = handleSubmit(async (values) => {
        if (!hasValidLink) return
        mutation.reset()
        await mutation.mutateAsync({
            email: values.email.trim(),
            token: initialToken.trim(),
            newPassword: values.newPassword,
            confirmPassword: values.confirmPassword,
        })
    })

    return (
        <Box component="form" onSubmit={onSubmit} noValidate>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: '#1A1A2E', mb: 0.75 }}>Choose a new password</Typography>
            <Typography sx={{ fontSize: 13, color: '#6B7280', mb: 2.5 }}>
                Set a new password for your WorkFlow account
            </Typography>

            {!hasValidLink && (
                <Alert severity="error" sx={{ mb: 2, borderRadius: '8px', fontSize: 12 }}>
                    This reset link is incomplete or expired. Please request a new one.
                </Alert>
            )}
            {mutation.isSuccess && (
                <Alert severity="success" sx={{ mb: 2, borderRadius: '8px', fontSize: 12 }}>
                    Your password has been reset. You can now sign in with your new password.
                </Alert>
            )}
            {mutation.isError && (
                <Alert severity="error" sx={{ mb: 2, borderRadius: '8px', fontSize: 12 }}>
                    {getApiErrorMessage(mutation.error, 'Unable to reset password. Please request a new link.')}
                </Alert>
            )}

            <Stack spacing={1.75} mb={2}>
                <TextField
                    label="Email address"
                    type="email"
                    {...register('email')}
                    error={!!errors.email}
                    helperText={errors.email?.message}
                    fullWidth
                    disabled={mutation.isSuccess}
                    autoComplete="email"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>✉️</Typography></InputAdornment>,
                    }}
                    sx={inputSx}
                />

                <TextField
                    label="New password"
                    type={showNew ? 'text' : 'password'}
                    {...register('newPassword')}
                    error={!!errors.newPassword}
                    helperText={errors.newPassword?.message ?? 'Use at least 6 characters.'}
                    placeholder="Create a strong password"
                    fullWidth
                    disabled={!hasValidLink || mutation.isSuccess}
                    autoComplete="new-password"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>🔒</Typography></InputAdornment>,
                        endAdornment: (
                            <InputAdornment position="end">
                                <IconButton size="small" onClick={() => setShowNew((v) => !v)} onMouseDown={(e) => e.preventDefault()} edge="end" sx={{ color: '#9CA3AF' }}>
                                    {showNew ? <VisibilityOffRoundedIcon fontSize="small" /> : <VisibilityRoundedIcon fontSize="small" />}
                                </IconButton>
                            </InputAdornment>
                        ),
                    }}
                    sx={inputSx}
                />

                <TextField
                    label="Confirm new password"
                    type={showConfirm ? 'text' : 'password'}
                    {...register('confirmPassword')}
                    error={!!errors.confirmPassword}
                    helperText={errors.confirmPassword?.message}
                    placeholder="Repeat your password"
                    fullWidth
                    disabled={!hasValidLink || mutation.isSuccess}
                    autoComplete="new-password"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>🔒</Typography></InputAdornment>,
                        endAdornment: (
                            <InputAdornment position="end">
                                <IconButton size="small" onClick={() => setShowConfirm((v) => !v)} onMouseDown={(e) => e.preventDefault()} edge="end" sx={{ color: '#9CA3AF' }}>
                                    {showConfirm ? <VisibilityOffRoundedIcon fontSize="small" /> : <VisibilityRoundedIcon fontSize="small" />}
                                </IconButton>
                            </InputAdornment>
                        ),
                    }}
                    sx={inputSx}
                />
            </Stack>

            <Box
                component="button"
                type="submit"
                disabled={mutation.isPending || !hasValidLink || mutation.isSuccess}
                sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: mutation.isPending ? 'not-allowed' : 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, mb: 2, transition: 'all 0.15s', '&:hover:not(:disabled)': { bgcolor: '#3A7AE4', transform: 'translateY(-1px)', boxShadow: '0 4px 12px rgba(79,142,247,0.3)' }, '&:disabled': { opacity: 0.7 } }}
            >
                {mutation.isPending ? <><CircularProgress size={16} sx={{ color: '#fff' }} /> Resetting...</> : 'Reset Password'}
            </Box>

            <Typography sx={{ textAlign: 'center', fontSize: 13, color: '#6B7280' }}>
                {mutation.isSuccess ? 'Ready to continue? ' : 'Need another link? '}
                <Box
                    component="button"
                    type="button"
                    onClick={mutation.isSuccess ? onBackToLogin : onRequestNewLink}
                    sx={{ color: '#4F8EF7', fontWeight: 500, background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', fontSize: 13, p: 0, '&:hover': { textDecoration: 'underline' } }}
                >
                    {mutation.isSuccess ? 'Go to Sign In' : 'Request a new reset email'}
                </Box>
            </Typography>
        </Box>
    )
}

export default ResetPasswordForm
