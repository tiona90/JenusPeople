import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { apiBaseUrl } from '../../lib/api'
import { useStore } from '../../lib/mobx'

const socialReturnUrl = encodeURIComponent(`${window.location.origin}/#dashboard`)
const googleLoginUrl = `${apiBaseUrl}/account/external-login/google?returnUrl=${socialReturnUrl}`
const githubLoginUrl = `${apiBaseUrl}/account/external-login/github?returnUrl=${socialReturnUrl}`

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

function GoogleIcon() {
    return (
        <Box component="svg" viewBox="0 0 24 24" sx={{ width: 18, height: 18, flexShrink: 0 }}>
            <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" />
            <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
            <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" />
            <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" />
        </Box>
    )
}

function GitHubIcon() {
    return (
        <Box component="svg" viewBox="0 0 24 24" sx={{ width: 18, height: 18, flexShrink: 0, fill: 'currentColor' }}>
            <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z" />
        </Box>
    )
}

interface LoginFormProps {
    onForgotPassword: () => void
    onSwitchToRegister: () => void
}

function LoginForm({ onForgotPassword, onSwitchToRegister }: LoginFormProps) {
    const { authStore } = useStore()
    const queryClient = useQueryClient()
    const [email, setEmail] = useState('')
    const [password, setPassword] = useState('')
    const [showPassword, setShowPassword] = useState(false)

    const mutation = useMutation({ mutationFn: authStore.signIn })

    async function handleSubmit(e: React.FormEvent) {
        e.preventDefault()
        mutation.reset()
        await mutation.mutateAsync({ email, password, rememberMe: false })
        await queryClient.cancelQueries()
        queryClient.clear()
        window.location.hash = '#dashboard'
        window.location.reload()
    }

    return (
        <Box component="form" onSubmit={handleSubmit} noValidate>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: '#1A1A2E', mb: 0.75 }}>Welcome back</Typography>
            <Typography sx={{ fontSize: 13, color: '#6B7280', mb: 3 }}>Sign in to your WorkFlow account</Typography>

            {mutation.isError && (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, p: '10px 14px', borderRadius: '8px', bgcolor: '#FEF2F2', border: '1px solid #FECACA', mb: 1.75 }}>
                    <Typography sx={{ fontSize: 12, color: '#991B1B' }}>
                        ⚠️ {getApiErrorMessage(mutation.error, 'Invalid email or password. Please try again.')}
                    </Typography>
                </Box>
            )}

            {/* Social */}
            <Stack spacing={1.25} mb={2.5}>
                <Box
                    component="a"
                    href={githubLoginUrl}
                    sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1.25, py: '10px', px: 2, borderRadius: '8px', fontSize: 13, fontWeight: 500, textDecoration: 'none', bgcolor: '#24292F', color: '#fff', border: '1px solid #24292F', transition: 'all 0.15s', '&:hover': { bgcolor: '#1a1f24' } }}
                >
                    <GitHubIcon />
                    Continue with GitHub
                </Box>
                <Box
                    component="a"
                    href={googleLoginUrl}
                    sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1.25, py: '10px', px: 2, borderRadius: '8px', fontSize: 13, fontWeight: 500, textDecoration: 'none', bgcolor: '#fff', color: '#374151', border: '1px solid #D1D5DB', transition: 'all 0.15s', '&:hover': { bgcolor: '#F9FAFB', borderColor: '#9CA3AF' } }}
                >
                    <GoogleIcon />
                    Continue with Google
                </Box>
            </Stack>

            {/* Divider */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2.5 }}>
                <Box sx={{ flex: 1, height: '1px', bgcolor: '#E4E6EA' }} />
                <Typography sx={{ fontSize: 12, color: '#9CA3AF', whiteSpace: 'nowrap' }}>or sign in with email</Typography>
                <Box sx={{ flex: 1, height: '1px', bgcolor: '#E4E6EA' }} />
            </Box>

            {/* Fields */}
            <Stack spacing={1.75} mb={1.25}>
                <TextField
                    label="Email address"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="you@company.com"
                    required
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
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Enter your password"
                    required
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

            {/* Forgot password */}
            <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1.75 }}>
                <Box
                    component="button"
                    type="button"
                    onClick={onForgotPassword}
                    sx={{ fontSize: 12, color: '#4F8EF7', background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', p: 0, '&:hover': { textDecoration: 'underline' } }}
                >
                    Forgot password?
                </Box>
            </Box>

            {/* Submit */}
            <Box
                component="button"
                type="submit"
                disabled={mutation.isPending}
                sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: mutation.isPending ? 'not-allowed' : 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, mb: 2, transition: 'all 0.15s', '&:hover:not(:disabled)': { bgcolor: '#3A7AE4', transform: 'translateY(-1px)', boxShadow: '0 4px 12px rgba(79,142,247,0.3)' }, '&:disabled': { opacity: 0.7 } }}
            >
                {mutation.isPending ? <><CircularProgress size={16} sx={{ color: '#fff' }} /> Signing in...</> : 'Sign In'}
            </Box>

            <Typography sx={{ textAlign: 'center', fontSize: 13, color: '#6B7280', mb: 2 }}>
                Don&apos;t have an account?{' '}
                <Box component="button" type="button" onClick={onSwitchToRegister} sx={{ color: '#4F8EF7', fontWeight: 500, background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', fontSize: 13, p: 0, '&:hover': { textDecoration: 'underline' } }}>
                    Create one
                </Box>
            </Typography>

            <Typography sx={{ fontSize: 11, color: '#9CA3AF', textAlign: 'center' }}>
                Demo: admin@annualleave.com / Pa$$w0rd
            </Typography>
        </Box>
    )
}

export default LoginForm
