import { Component, type ErrorInfo, type ReactNode } from 'react'
import { Box, Button, Stack, Typography } from '@mui/material'

interface Props {
    children: ReactNode
    /**
     * The current route. A change clears a caught error, so navigating away from
     * a page that crashed shows the next page rather than the same message.
     */
    resetKey: string
}

interface State {
    error: Error | null
    errorKey: string | null
}

/**
 * Keeps a render error to the page it happened on. Without it, one unexpected
 * null from an older API blanked the whole app — sidebar and all — to a white
 * screen. The error itself is logged to the console, not shown: its text is for
 * a developer, not the person using the page.
 */
export default class ErrorBoundary extends Component<Props, State> {
    state: State = { error: null, errorKey: null }

    static getDerivedStateFromError(error: Error): Partial<State> {
        return { error }
    }

    static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
        if (state.error && state.errorKey !== null && state.errorKey !== props.resetKey) {
            return { error: null, errorKey: null }
        }
        if (state.error && state.errorKey === null) {
            return { errorKey: props.resetKey }
        }
        return null
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error('Page crashed', error, info.componentStack)
    }

    render() {
        if (!this.state.error) return this.props.children

        return (
            <Box role="alert" sx={{ py: 8, display: 'grid', placeItems: 'center' }}>
                <Stack spacing={2} alignItems="center" sx={{ maxWidth: 420, textAlign: 'center' }}>
                    <Typography variant="h6">Something went wrong on this page.</Typography>
                    <Typography color="text.secondary">
                        Reload to try again, or pick another page from the menu.
                    </Typography>
                    <Button variant="contained" onClick={() => window.location.reload()}>
                        Reload
                    </Button>
                </Stack>
            </Box>
        )
    }
}
