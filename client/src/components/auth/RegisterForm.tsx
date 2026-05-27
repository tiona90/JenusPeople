import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery } from '@tanstack/react-query'
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
import { getDepartments } from '../../lib/api'
import { apiBaseUrl } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { useStore } from '../../lib/mobx'
import type { Department } from '../../lib/types'

const socialReturnUrl = encodeURIComponent(`${window.location.origin}/#dashboard`)
const googleLoginUrl = `${apiBaseUrl}/account/external-login/google?returnUrl=${socialReturnUrl}`
const githubLoginUrl = `${apiBaseUrl}/account/external-login/github?returnUrl=${socialReturnUrl}`

const registerSchema = z.object({
    firstName: z.string().trim().min(1, 'First name is required.'),
    lastName: z.string().trim().min(1, 'Last name is required.'),
    email: z.string().trim().min(1, 'Email is required.').email('Enter a valid email address.'),
    departmentId: z.number().int().positive('Please choose your department.'),
    password: z.string().min(6, 'Use at least 6 characters.'),
    confirmPassword: z.string().min(1, 'Please confirm your password.'),
    termsAccepted: z.literal(true, { message: 'You must accept the terms to continue.' }),
}).refine((data) => data.password === data.confirmPassword, {
    message: 'Passwords do not match.',
    path: ['confirmPassword'],
})
type RegisterValues = z.infer<typeof registerSchema>

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

function getPasswordScore(pw: string) {
    let score = 0
    if (pw.length >= 8) score++
    if (/[A-Z]/.test(pw)) score++
    if (/[0-9]/.test(pw)) score++
    if (/[^A-Za-z0-9]/.test(pw)) score++
    return score
}

function PasswordStrength({ password }: { password: string }) {
    const score = getPasswordScore(password)
    if (!password) return null

    const barColor = score <= 1 ? '#FF4D4F' : score <= 2 ? '#F59E0B' : '#22C47A'
    const labels = ['', 'Weak', 'Fair', 'Good', 'Strong']
    const labelColor = score <= 1 ? '#FF4D4F' : score <= 2 ? '#F59E0B' : '#22C47A'

    return (
        <Box sx={{ mt: 0.75 }}>
            <Box sx={{ display: 'flex', gap: '4px', mb: 0.5 }}>
                {[1, 2, 3, 4].map((i) => (
                    <Box
                        key={i}
                        sx={{
                            flex: 1,
                            height: '3px',
                            borderRadius: '2px',
                            bgcolor: i <= score ? barColor : '#E4E6EA',
                            transition: 'background 0.2s',
                        }}
                    />
                ))}
            </Box>
            <Typography sx={{ fontSize: 11, color: labelColor }}>
                Password strength: {labels[score]}
            </Typography>
        </Box>
    )
}

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

interface RegisterFormProps {
    onSwitchToLogin: () => void
}

function RegisterForm({ onSwitchToLogin }: RegisterFormProps) {
    const { authStore } = useStore()
    const [showPassword, setShowPassword] = useState(false)
    const [showConfirm, setShowConfirm] = useState(false)
    const [registeredEmail, setRegisteredEmail] = useState('')

    const { data: departments = [], isLoading: deptsLoading, isError: deptsError } = useQuery({
        queryKey: ['departments'],
        queryFn: getDepartments,
    })
    const activeDepts = useMemo(() => departments.filter((d: Department) => d.isActive), [departments])

    const { register, handleSubmit, watch, reset, formState: { errors } } = useForm<RegisterValues>({
        resolver: zodResolver(registerSchema),
        defaultValues: {
            firstName: '',
            lastName: '',
            email: '',
            departmentId: 0,
            password: '',
            confirmPassword: '',
            termsAccepted: false as unknown as true, // Zod literal(true) — RHF needs an initial value
        },
    })

    const password = watch('password')

    const mutation = useMutation({ mutationFn: authStore.signUp })

    const onSubmit = handleSubmit(async (values) => {
        mutation.reset()
        setRegisteredEmail('')

        const displayName = [values.firstName.trim(), values.lastName.trim()].filter(Boolean).join(' ')
        const submittedEmail = values.email.trim()
        const response = await mutation.mutateAsync({
            email: submittedEmail,
            password: values.password,
            displayName,
            departmentId: values.departmentId,
        })

        if (response && response.verificationEmailSent === false) return

        setRegisteredEmail(submittedEmail)
        reset()
    })

    if (mutation.isSuccess && mutation.data?.verificationEmailSent !== false && registeredEmail) {
        return (
            <Box sx={{ textAlign: 'center', py: 2.5 }}>
                <Typography sx={{ fontSize: 48, mb: 1.5 }}>✅</Typography>
                <Typography sx={{ fontSize: 18, fontWeight: 700, color: '#1A1A2E', mb: 1 }}>Account created!</Typography>
                <Typography sx={{ fontSize: 13, color: '#6B7280', lineHeight: 1.6, mb: 2.5 }}>
                    Your account is pending email verification.<br />
                    Please check <strong>{registeredEmail}</strong> and click the link to activate.
                </Typography>
                <Box
                    component="button"
                    onClick={onSwitchToLogin}
                    sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', transition: 'all 0.15s', '&:hover': { bgcolor: '#3A7AE4' } }}
                >
                    Back to Sign In
                </Box>
            </Box>
        )
    }

    return (
        <Box component="form" onSubmit={onSubmit} noValidate>
            <Typography sx={{ fontSize: 22, fontWeight: 700, color: '#1A1A2E', mb: 0.75 }}>Create your account</Typography>
            <Typography sx={{ fontSize: 13, color: '#6B7280', mb: 3 }}>Join WorkFlow to manage your leave and timesheets</Typography>

            {/* Social */}
            <Stack spacing={1.25} mb={2.5}>
                <Box
                    component="a"
                    href={githubLoginUrl}
                    sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1.25, py: '10px', px: 2, borderRadius: '8px', fontSize: 13, fontWeight: 500, textDecoration: 'none', bgcolor: '#24292F', color: '#fff', border: '1px solid #24292F', transition: 'all 0.15s', '&:hover': { bgcolor: '#1a1f24' } }}
                >
                    <GitHubIcon />
                    Sign up with GitHub
                </Box>
                <Box
                    component="a"
                    href={googleLoginUrl}
                    sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1.25, py: '10px', px: 2, borderRadius: '8px', fontSize: 13, fontWeight: 500, textDecoration: 'none', bgcolor: '#fff', color: '#374151', border: '1px solid #D1D5DB', transition: 'all 0.15s', '&:hover': { bgcolor: '#F9FAFB', borderColor: '#9CA3AF' } }}
                >
                    <GoogleIcon />
                    Sign up with Google
                </Box>
            </Stack>

            {/* Divider */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2.5 }}>
                <Box sx={{ flex: 1, height: '1px', bgcolor: '#E4E6EA' }} />
                <Typography sx={{ fontSize: 12, color: '#9CA3AF', whiteSpace: 'nowrap' }}>or register with email</Typography>
                <Box sx={{ flex: 1, height: '1px', bgcolor: '#E4E6EA' }} />
            </Box>

            {/* First / Last Name */}
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5, mb: 1.75 }}>
                <TextField
                    label="First Name"
                    {...register('firstName')}
                    error={!!errors.firstName}
                    helperText={errors.firstName?.message}
                    placeholder="John"
                    fullWidth
                    disabled={mutation.isPending}
                    autoComplete="given-name"
                    sx={inputSx}
                />
                <TextField
                    label="Last Name"
                    {...register('lastName')}
                    error={!!errors.lastName}
                    helperText={errors.lastName?.message}
                    placeholder="Doe"
                    fullWidth
                    disabled={mutation.isPending}
                    autoComplete="family-name"
                    sx={inputSx}
                />
            </Box>

            <Stack spacing={1.75} mb={1.75}>
                <TextField
                    label="Work Email"
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
                    select
                    label="Department"
                    {...register('departmentId', { valueAsNumber: true })}
                    error={!!errors.departmentId}
                    helperText={errors.departmentId?.message}
                    fullWidth
                    disabled={mutation.isPending || deptsLoading}
                    SelectProps={{ native: true }}
                    sx={{ ...inputSx, '& select': { fontSize: 13 } }}
                >
                    <option value="0">Select your department…</option>
                    {activeDepts.map((d: Department) => (
                        <option key={d.id} value={d.id}>{d.name}</option>
                    ))}
                </TextField>

                <Box>
                    <TextField
                        label="Password"
                        type={showPassword ? 'text' : 'password'}
                        {...register('password')}
                        error={!!errors.password}
                        helperText={errors.password?.message}
                        placeholder="Create a strong password"
                        fullWidth
                        disabled={mutation.isPending}
                        autoComplete="new-password"
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
                    <PasswordStrength password={password ?? ''} />
                </Box>

                <TextField
                    label="Confirm Password"
                    type={showConfirm ? 'text' : 'password'}
                    {...register('confirmPassword')}
                    error={!!errors.confirmPassword}
                    helperText={errors.confirmPassword?.message}
                    placeholder="Repeat your password"
                    fullWidth
                    disabled={mutation.isPending}
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

            {/* Terms */}
            <Box sx={{ mb: 2 }}>
                <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1 }}>
                    <Box
                        component="input"
                        type="checkbox"
                        id="terms"
                        {...register('termsAccepted')}
                        sx={{ width: 15, height: 15, mt: '2px', flexShrink: 0, cursor: 'pointer', accentColor: '#4F8EF7' }}
                    />
                    <Typography component="label" htmlFor="terms" sx={{ fontSize: 12, color: '#6B7280', lineHeight: 1.5, cursor: 'pointer' }}>
                        I agree to the{' '}
                        <Box component="span" sx={{ color: '#4F8EF7' }}>Terms of Service</Box>
                        {' '}and{' '}
                        <Box component="span" sx={{ color: '#4F8EF7' }}>Privacy Policy</Box>
                    </Typography>
                </Box>
                {errors.termsAccepted && (
                    <Typography sx={{ fontSize: 11, color: '#FF4D4F', mt: 0.5, ml: '23px' }}>
                        {errors.termsAccepted.message}
                    </Typography>
                )}
            </Box>

            {deptsError && <Alert severity="error" sx={{ mb: 1.5, borderRadius: '8px', fontSize: 12 }}>Unable to load departments. Please refresh.</Alert>}
            {mutation.isError && (
                <Alert severity="error" sx={{ mb: 1.5, borderRadius: '8px', fontSize: 12 }}>
                    {getApiErrorMessage(mutation.error, 'Unable to create your account. Please try again.')}
                </Alert>
            )}
            {mutation.isSuccess && mutation.data?.verificationEmailSent === false && (
                <Alert severity="error" sx={{ mb: 1.5, borderRadius: '8px', fontSize: 12 }}>
                    Registration failed: could not send verification email. Please try again later.
                </Alert>
            )}

            <Box
                component="button"
                type="submit"
                disabled={mutation.isPending}
                sx={{ width: '100%', py: '11px', borderRadius: '8px', fontSize: 14, fontWeight: 600, cursor: mutation.isPending ? 'not-allowed' : 'pointer', border: 'none', bgcolor: '#4F8EF7', color: '#fff', fontFamily: 'inherit', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, mb: 2, transition: 'all 0.15s', '&:hover:not(:disabled)': { bgcolor: '#3A7AE4', transform: 'translateY(-1px)', boxShadow: '0 4px 12px rgba(79,142,247,0.3)' }, '&:disabled': { opacity: 0.7 } }}
            >
                {mutation.isPending ? <><CircularProgress size={16} sx={{ color: '#fff' }} /> Creating account...</> : 'Create Account'}
            </Box>

            <Typography sx={{ textAlign: 'center', fontSize: 13, color: '#6B7280' }}>
                Already have an account?{' '}
                <Box component="button" type="button" onClick={onSwitchToLogin} sx={{ color: '#4F8EF7', fontWeight: 500, background: 'none', border: 'none', cursor: 'pointer', fontFamily: 'inherit', fontSize: 13, p: 0, '&:hover': { textDecoration: 'underline' } }}>
                    Sign in
                </Box>
            </Typography>
        </Box>
    )
}

export default RegisterForm
