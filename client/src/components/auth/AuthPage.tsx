import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Typography from '@mui/material/Typography'
import { ThemeProvider } from '@mui/material/styles'
import { buildTheme } from '../../lib/theme'
import LoginForm from './LoginForm'
import RegisterForm from './RegisterForm'
import ForgotPasswordForm from './ForgotPasswordForm'
import ResetPasswordForm from './ResetPasswordForm'

// Auth screens use their own designed-for-light visual identity (dark navy
// brand panel on the left, white form panel on the right). Bypass the user's
// app-wide theme preference so signed-out visitors always see this layout
// readable — without a logged-in profile to remember a preference against,
// dark mode here just makes hardcoded text invisible.
const authTheme = buildTheme('light')

const features = [
    { icon: '📅', title: 'Annual Leave Management', sub: 'Submit, approve and track leave requests' },
    { icon: '🕐', title: 'Timesheet Tracking', sub: 'Log daily hours against projects' },
    { icon: '🏢', title: 'Department Scoping', sub: 'Role-based visibility per department' },
    { icon: '🔔', title: 'Instant Notifications', sub: 'Email alerts for every status change' },
]

function LeftPanel() {
    return (
        <Box
            sx={{
                width: 420,
                flexShrink: 0,
                bgcolor: '#1A1A2E',
                display: { xs: 'none', md: 'flex' },
                flexDirection: 'column',
                justifyContent: 'space-between',
                p: 5,
                position: 'relative',
                overflow: 'hidden',
            }}
        >
            <Box sx={{ position: 'absolute', top: -80, right: -80, width: 300, height: 300, borderRadius: '50%', bgcolor: 'rgba(79,142,247,0.12)', pointerEvents: 'none' }} />
            <Box sx={{ position: 'absolute', bottom: -60, left: -60, width: 250, height: 250, borderRadius: '50%', bgcolor: 'rgba(34,196,122,0.08)', pointerEvents: 'none' }} />

            <Box sx={{ position: 'relative', zIndex: 1 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 6 }}>
                    <Box sx={{ width: 38, height: 38, bgcolor: '#4F8EF7', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 18 }}>
                        📋
                    </Box>
                    <Typography sx={{ fontSize: 20, fontWeight: 700, color: '#fff', letterSpacing: '0.01em' }}>Jenus People</Typography>
                </Box>

                <Typography sx={{ fontSize: 28, fontWeight: 700, color: '#fff', lineHeight: 1.35, mb: 2 }}>
                    Manage Leave &<br />
                    <Box component="span" sx={{ color: '#4F8EF7' }}>Timesheets</Box> with ease
                </Typography>
                <Typography sx={{ fontSize: 14, color: '#7B7B9A', lineHeight: 1.7 }}>
                    A single platform for your team to submit, track, and approve annual leave and timesheet hours — all in one place.
                </Typography>

                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mt: 5 }}>
                    {features.map((f) => (
                        <Box key={f.icon} sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                            <Box sx={{ width: 36, height: 36, borderRadius: '8px', bgcolor: 'rgba(255,255,255,0.07)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 16, flexShrink: 0 }}>
                                {f.icon}
                            </Box>
                            <Box>
                                <Typography sx={{ fontSize: 13, fontWeight: 500, color: '#E5E7EB' }}>{f.title}</Typography>
                                <Typography sx={{ fontSize: 12, color: '#6B7280', mt: 0.15 }}>{f.sub}</Typography>
                            </Box>
                        </Box>
                    ))}
                </Box>
            </Box>

            <Typography sx={{ position: 'relative', zIndex: 1, fontSize: 11, color: '#4B4B6A' }}>
                © 2026 Jenus People. All rights reserved.
            </Typography>
        </Box>
    )
}

interface AuthPageProps {
    authView: 'login' | 'register' | 'forgot-password' | 'reset-password'
    authNotice: { severity: 'success' | 'info' | 'error'; message: string } | null
    onClearNotice: () => void
    onSwitchToLogin: () => void
    onSwitchToRegister: () => void
    onForgotPassword: () => void
    onBackToLogin: () => void
    onRequestNewLink: () => void
}

const toggleBtnSx = (active: boolean) => ({
    flex: 1,
    py: '8px',
    borderRadius: '6px',
    fontSize: 13,
    fontWeight: 500,
    cursor: 'pointer',
    border: 'none',
    fontFamily: 'inherit',
    bgcolor: active ? '#fff' : 'transparent',
    color: active ? '#1A1A2E' : '#6B7280',
    boxShadow: active ? '0 1px 3px rgba(0,0,0,0.1)' : 'none',
    transition: 'all 0.2s',
    '&:hover': { color: '#1A1A2E' },
})

function AuthPage({ authView, authNotice, onClearNotice, onSwitchToLogin, onSwitchToRegister, onForgotPassword, onBackToLogin, onRequestNewLink }: AuthPageProps) {
    const showToggle = authView === 'login' || authView === 'register'

    return (
        <ThemeProvider theme={authTheme}>
            <Box sx={{ minHeight: '100vh', display: 'flex' }}>
                <LeftPanel />

                <Box sx={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', p: '40px 20px', bgcolor: '#F4F5F7', overflowY: 'auto' }}>
                    <Box sx={{ width: '100%', maxWidth: 420 }}>
                        {authNotice && (
                            <Alert severity={authNotice.severity} onClose={onClearNotice} sx={{ mb: 2.5, borderRadius: '8px', fontSize: 13 }}>
                                {authNotice.message}
                            </Alert>
                        )}

                        {showToggle && (
                            <Box sx={{ display: 'flex', bgcolor: '#E4E6EA', borderRadius: '8px', p: '4px', mb: 3.5 }}>
                                <Box component="button" onClick={onSwitchToLogin} sx={toggleBtnSx(authView === 'login')}>
                                    Sign In
                                </Box>
                                <Box component="button" onClick={onSwitchToRegister} sx={toggleBtnSx(authView === 'register')}>
                                    Create Account
                                </Box>
                            </Box>
                        )}

                        {authView === 'login' && (
                            <LoginForm onForgotPassword={onForgotPassword} onSwitchToRegister={onSwitchToRegister} />
                        )}
                        {authView === 'register' && (
                            <RegisterForm onSwitchToLogin={onSwitchToLogin} />
                        )}
                        {authView === 'forgot-password' && (
                            <ForgotPasswordForm onBackToLogin={onBackToLogin} />
                        )}
                        {authView === 'reset-password' && (
                            <ResetPasswordForm onBackToLogin={onBackToLogin} onRequestNewLink={onRequestNewLink} />
                        )}
                    </Box>
                </Box>
            </Box>
        </ThemeProvider>
    )
}

export default AuthPage
