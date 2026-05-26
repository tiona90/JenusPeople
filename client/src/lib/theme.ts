import { createTheme, type Theme } from '@mui/material/styles'

export type ThemeMode = 'light' | 'dark'

const lightPalette = {
    primary: { main: '#0f766e', dark: '#115e59', light: '#2dd4bf' },
    secondary: { main: '#c2410c', dark: '#9a3412', light: '#fdba74' },
    success: { main: '#2f855a' },
    warning: { main: '#d97706' },
    error: { main: '#dc2626' },
    info: { main: '#0369a1' },
    text: { primary: '#0f172a', secondary: '#475569' },
    background: { default: '#f4f5f2', paper: '#ffffff' },
    divider: 'rgba(15, 23, 42, 0.12)',
} as const

const darkPalette = {
    primary: { main: '#2dd4bf', dark: '#14b8a6', light: '#5eead4' },
    secondary: { main: '#fb923c', dark: '#ea580c', light: '#fdba74' },
    success: { main: '#34d399' },
    warning: { main: '#fbbf24' },
    error: { main: '#f87171' },
    info: { main: '#60a5fa' },
    text: { primary: '#f1f5f9', secondary: '#94a3b8' },
    background: { default: '#0b1220', paper: '#111827' },
    divider: 'rgba(148, 163, 184, 0.18)',
} as const

export function buildTheme(mode: ThemeMode): Theme {
    const isDark = mode === 'dark'
    return createTheme({
        palette: {
            mode,
            ...(isDark ? darkPalette : lightPalette),
        },
        shape: {
            borderRadius: 16,
        },
        typography: {
            fontFamily: "'Aptos', 'Segoe UI Variable', 'Segoe UI', Tahoma, 'Trebuchet MS', sans-serif",
            h1: { fontWeight: 800, letterSpacing: '-0.03em' },
            h2: { fontWeight: 800, letterSpacing: '-0.03em' },
            h3: { fontWeight: 800, letterSpacing: '-0.03em' },
            h4: { fontWeight: 800, letterSpacing: '-0.02em' },
            h5: { fontWeight: 750, letterSpacing: '-0.015em' },
            h6: { fontWeight: 750, letterSpacing: '-0.01em' },
            subtitle1: { fontWeight: 600 },
            body1: { lineHeight: 1.55 },
            body2: { lineHeight: 1.5 },
            button: { textTransform: 'none', fontWeight: 700, letterSpacing: '-0.01em' },
        },
        components: {
            MuiCssBaseline: {
                styleOverrides: {
                    body: {
                        backgroundImage: isDark
                            ? 'radial-gradient(circle at 15% 0%, rgba(45,212,191,0.06), transparent 35%), radial-gradient(circle at 85% 0%, rgba(251,146,60,0.06), transparent 35%)'
                            : 'radial-gradient(circle at 15% 0%, rgba(15,118,110,0.08), transparent 35%), radial-gradient(circle at 85% 0%, rgba(194,65,12,0.08), transparent 35%)',
                    },
                },
            },
            MuiAppBar: {
                styleOverrides: {
                    root: {
                        backgroundImage: 'none',
                        borderBottom: isDark
                            ? '1px solid rgba(148, 163, 184, 0.18)'
                            : '1px solid rgba(15, 23, 42, 0.1)',
                        backdropFilter: 'blur(8px)',
                    },
                },
            },
            MuiPaper: {
                styleOverrides: {
                    rounded: { borderRadius: 16 },
                    root: { backgroundImage: 'none' },
                },
            },
            MuiButton: {
                defaultProps: { disableElevation: true },
                styleOverrides: {
                    root: { borderRadius: 999, paddingInline: 18, minHeight: 40 },
                },
            },
            MuiChip: {
                styleOverrides: {
                    root: { borderRadius: 999, fontWeight: 600 },
                },
            },
            MuiTextField: {
                defaultProps: { size: 'small' },
            },
            MuiOutlinedInput: {
                styleOverrides: {
                    root: { borderRadius: 14 },
                },
            },
            MuiTabs: {
                styleOverrides: {
                    indicator: { height: 3, borderRadius: 999 },
                },
            },
        },
    })
}

// Default export retained for any caller that still imports the static theme.
const theme = buildTheme('light')
export default theme
