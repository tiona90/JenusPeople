import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import Checkbox from '@mui/material/Checkbox'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'

const loginSchema = z.object({
    email: z.string().min(1, 'Email is required.').email('Enter a valid email address.'),
    password: z.string().min(1, 'Password is required.'),
    rememberMe: z.boolean(),
})
type LoginValues = z.infer<typeof loginSchema>

const inputSx = {
    '& .MuiOutlinedInput-root': {
        borderRadius: '8px',
        // Tinted at rest so the fields read as inputs against the white card,
        // then white on focus to signal the active row.
        bgcolor: '#F9FAFB',
        fontSize: 13,
        '& fieldset': { borderColor: '#D1D5DB', borderWidth: '1.5px' },
        '&:hover fieldset': { borderColor: '#9CA3AF', borderWidth: '1.5px' },
        '&.Mui-focused': { bgcolor: '#fff', boxShadow: '0 0 0 3px rgba(79,142,247,0.12)' },
        '&.Mui-focused fieldset': { borderColor: '#4F8EF7', borderWidth: '1.5px' },
    },
    '& .MuiInputLabel-root': { fontSize: 12, fontWeight: 500, color: '#374151' },
    '& .MuiInputLabel-root.Mui-focused': { color: '#4F8EF7' },
} as const

interface LoginFormProps {
    onForgotPassword: () => void
}

function LoginForm({ onForgotPassword }: LoginFormProps) {
    const { authStore } = useStore()
    const queryClient = useQueryClient()
    const [showPassword, setShowPassword] = useState(false)

    const { register, handleSubmit, formState: { errors } } = useForm<LoginValues>({
        resolver: zodResolver(loginSchema),
        defaultValues: { email: '', password: '', rememberMe: false },
    })

    const mutation = useMutation({ mutationFn: authStore.signIn })

    const onSubmit = handleSubmit(async (values) => {
        mutation.reset()

        try {
            await mutation.mutateAsync({ email: values.email, password: values.password, rememberMe: values.rememberMe })
        } catch {
            // The failure is already rendered from `mutation.isError`. Swallow it
            // here so a rejected sign-in doesn't escape handleSubmit as an
            // unhandled promise rejection, and skip the post-success reload.
            return
        }

        await queryClient.cancelQueries()
        queryClient.clear()
        window.location.reload()
    })

    return (
        <Box component="form" onSubmit={onSubmit} noValidate>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: '#1A1A2E', mb: 0.75 }}>Welcome back</Typography>
            <Typography sx={{ fontSize: 13, color: '#6B7280', mb: 3 }}>Sign in to your Jenus People account</Typography>

            {mutation.isError && (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, p: '10px 14px', borderRadius: '8px', bgcolor: '#FEF2F2', border: '1px solid #FECACA', mb: 1.75 }}>
                    <Typography sx={{ fontSize: 12, color: '#991B1B' }}>
                        ⚠️ {getApiErrorMessage(mutation.error, 'Invalid email or password. Please try again.')}
                    </Typography>
                </Box>
            )}

            {/* Fields */}
            <Stack spacing={1.75} mb={1.25}>
                <TextField
                    label="Email address"
                    type="email"
                    {...register('email')}
                    error={!!errors.email}
                    helperText={errors.email?.message}
                    placeholder="you@company.com"
                    fullWidth
                    disabled={mutation.isPending}
                    autoComplete="email"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>✉️</Typography></InputAdornment>,
                    }}
                    sx={inputSx}
                />

                <TextField
                    label="Password"
                    type={showPassword ? 'text' : 'password'}
                    {...register('password')}
                    error={!!errors.password}
                    helperText={errors.password?.message}
                    placeholder="Enter your password"
                    fullWidth
                    disabled={mutation.isPending}
                    autoComplete="current-password"
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><Typography sx={{ fontSize: 15, lineHeight: 1 }}>🔒</Typography></InputAdornment>,
                        endAdornment: (
                            <InputAdornment position="end">
                                <IconButton size="small" onClick={() => setShowPassword((v) => !v)} onMouseDown={(e) => e.preventDefault()} edge="end" sx={{ color: '#9CA3AF' }}>
                                    {showPassword ? <VisibilityOffRoundedIcon fontSize="small" /> : <VisibilityRoundedIcon fontSize="small" />}
                                </IconButton>
                            </InputAdornment>
                        ),
                    }}
                    sx={inputSx}
                />
            </Stack>

            {/* Session length + recovery, paired on one row */}
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, mb: 2.5 }}>
                <FormControlLabel
                    control={
                        <Checkbox
                            size="small"
                            {...register('rememberMe')}
                            disabled={mutation.isPending}
                            sx={{ py: 0.5, color: '#B6BCC6', '&.Mui-checked': { color: '#4F8EF7' } }}
                        />
                    }
                    label="Keep me signed in"
                    sx={{ m: 0, '& .MuiFormControlLabel-label': { fontSize: 12, color: '#374151' } }}
                />

                <Box
                    component="button"
                    type="button"
                    onClick={onForgotPassword}
                    sx={{ fontSize: 12, fontWeight: 500, color: '#4F8EF7', background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', p: 0, whiteSpace: 'nowrap', '&:hover': { textDecoration: 'underline' } }}
                >
                    Forgot password?
                </Box>
            </Box>

            {/* Submit */}
            <Box
                component="button"
                type="submit"
                disabled={mutation.isPending}
                sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: mutation.isPending ? 'not-allowed' : 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, transition: 'all 0.15s', '&:hover:not(:disabled)': { bgcolor: '#3A7AE4', transform: 'translateY(-1px)', boxShadow: '0 4px 12px rgba(79,142,247,0.3)' }, '&:disabled': { opacity: 0.7 } }}
            >
                {mutation.isPending ? <><CircularProgress size={16} sx={{ color: '#fff' }} /> Signing in...</> : 'Sign In'}
            </Box>
        </Box>
    )
}

export default LoginForm
