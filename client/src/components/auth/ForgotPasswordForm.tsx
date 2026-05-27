import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import InputAdornment from '@mui/material/InputAdornment'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { forgotPassword } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'

const forgotPasswordSchema = z.object({
    email: z.string().min(1, 'Email is required.').email('Enter a valid email address.'),
})
type ForgotPasswordValues = z.infer<typeof forgotPasswordSchema>

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

interface ForgotPasswordFormProps {
    onBackToLogin: () => void
}

function ForgotPasswordForm({ onBackToLogin }: ForgotPasswordFormProps) {
    const [submittedEmail, setSubmittedEmail] = useState('')

    const { register, handleSubmit, formState: { errors } } = useForm<ForgotPasswordValues>({
        resolver: zodResolver(forgotPasswordSchema),
        defaultValues: { email: '' },
    })

    const mutation = useMutation({ mutationFn: forgotPassword })

    const onSubmit = handleSubmit(async (values) => {
        mutation.reset()
        const trimmed = values.email.trim()
        await mutation.mutateAsync({ email: trimmed })
        setSubmittedEmail(trimmed)
    })

    return (
        <Box component="form" onSubmit={onSubmit} noValidate>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: '#1A1A2E', mb: 0.75 }}>Reset password</Typography>
            <Typography sx={{ fontSize: 13, color: '#6B7280', mb: 2.5 }}>
                Enter your email and we&apos;ll send you a reset link
            </Typography>

            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, p: '10px 14px', borderRadius: '8px', bgcolor: '#EFF6FF', border: '1px solid #BFDBFE', mb: 2.5 }}>
                <Typography sx={{ fontSize: 12, color: '#1D4ED8', lineHeight: 1.5 }}>
                    ℹ️ Check your spam folder if you don&apos;t receive the email within a few minutes.
                </Typography>
            </Box>

            {mutation.isSuccess && (
                <Alert severity="success" sx={{ mb: 2, borderRadius: '8px', fontSize: 12 }}>
                    If <strong>{submittedEmail}</strong> is registered and verified, a reset link has been sent.
                </Alert>
            )}

            {mutation.isError && (
                <Alert severity="error" sx={{ mb: 2, borderRadius: '8px', fontSize: 12 }}>
                    {getApiErrorMessage(mutation.error, 'We could not send a reset link. Please try again.')}
                </Alert>
            )}

            <Stack spacing={2} mb={2}>
                <TextField
                    label="Email address"
                    type="email"
                    {...register('email')}
                    error={!!errors.email}
                    helperText={errors.email?.message}
                    placeholder="you@company.com"
                    fullWidth
                    disabled={mutation.isPending || mutation.isSuccess}
                    autoComplete="email"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>✉️</Typography></InputAdornment>,
                    }}
                    sx={inputSx}
                />
            </Stack>

            <Box
                component="button"
                type="submit"
                disabled={mutation.isPending || mutation.isSuccess}
                sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: mutation.isPending ? 'not-allowed' : 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, mb: 2, transition: 'all 0.15s', '&:hover:not(:disabled)': { bgcolor: '#3A7AE4', transform: 'translateY(-1px)', boxShadow: '0 4px 12px rgba(79,142,247,0.3)' }, '&:disabled': { opacity: 0.7 } }}
            >
                {mutation.isPending ? <><CircularProgress size={16} sx={{ color: '#fff' }} /> Sending...</> : 'Send Reset Link'}
            </Box>

            <Typography sx={{ textAlign: 'center', fontSize: 13, color: '#6B7280' }}>
                <Box component="button" type="button" onClick={onBackToLogin} sx={{ color: '#4F8EF7', fontWeight: 500, background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', fontSize: 13, p: 0, '&:hover': { textDecoration: 'underline' } }}>
                    ← Back to Sign In
                </Box>
            </Typography>
        </Box>
    )
}

export default ForgotPasswordForm
