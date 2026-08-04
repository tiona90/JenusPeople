import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { StoreProvider } from './lib/mobx'

// Unauthenticated visitor: getCurrentUser fails, so AuthGate renders the public
// auth screen rather than redirecting into the app shell.
vi.mock('./lib/api/account', () => ({
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn().mockRejectedValue(new Error('unauthenticated')),
    forgotPassword: vi.fn(),
    resetPassword: vi.fn(),
    updateProfile: vi.fn(),
    uploadProfileImage: vi.fn(),
}))

function renderAppAt(path: string) {
    window.history.pushState({}, '', path)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    return render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <App />
            </QueryClientProvider>
        </StoreProvider>,
    )
}

describe('the retired /register route', () => {
    beforeEach(() => {
        window.history.pushState({}, '', '/')
    })

    it('redirects to the sign-in page', async () => {
        renderAppAt('/register')

        await waitFor(() => {
            expect(window.location.pathname).toBe('/login')
        })

        expect(await screen.findByText('Welcome back')).toBeInTheDocument()
    })

    it('renders no registration form at the old URL', async () => {
        renderAppAt('/register')

        await waitFor(() => {
            expect(window.location.pathname).toBe('/login')
        })

        expect(screen.queryByText(/create your account/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/create account/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/first name/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/confirm password/i)).not.toBeInTheDocument()
    })

    it('still serves the sign-in page at /login', async () => {
        renderAppAt('/login')

        expect(await screen.findByText('Welcome back')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /^sign in$/i })).toBeInTheDocument()
        expect(window.location.pathname).toBe('/login')
    })
})
