import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import { login } from '../../lib/api/account'
import AuthPage from './AuthPage'
import { StoreProvider } from '../../lib/mobx'

// The auth screen must offer sign-in only: accounts are created by an
// administrator, so no public self-registration UI may reach the DOM.
vi.mock('../../lib/api/account', () => ({
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn().mockRejectedValue(new Error('unauthenticated')),
    forgotPassword: vi.fn(),
    resetPassword: vi.fn(),
    updateProfile: vi.fn(),
    uploadProfileImage: vi.fn(),
}))

function renderAuthPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    return render(
        <StoreProvider>
            <QueryClientProvider client={queryClient}>
                <AuthPage
                    authView="login"
                    authNotice={null}
                    onClearNotice={() => {}}
                    onForgotPassword={() => {}}
                    onBackToLogin={() => {}}
                    onRequestNewLink={() => {}}
                />
            </QueryClientProvider>
        </StoreProvider>,
    )
}

describe('AuthPage', () => {
    it('renders the sign-in form', () => {
        renderAuthPage()

        expect(screen.getByText('Welcome back')).toBeInTheDocument()
        expect(screen.getByLabelText(/email address/i)).toBeInTheDocument()
        expect(screen.getByLabelText(/password/i)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /^sign in$/i })).toBeInTheDocument()
    })

    it('does not offer a "Create Account" tab', () => {
        renderAuthPage()

        expect(screen.queryByText(/create account/i)).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /create account/i })).not.toBeInTheDocument()
    })

    it('does not render the public registration form', () => {
        renderAuthPage()

        expect(screen.queryByText(/create your account/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/join jenus people/i)).not.toBeInTheDocument()

        // Fields unique to the retired registration form.
        expect(screen.queryByLabelText(/first name/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/last name/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/confirm password/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/date of birth/i)).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/department/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/terms of service/i)).not.toBeInTheDocument()
    })

    it('offers no social sign-up or sign-in options', () => {
        renderAuthPage()

        expect(screen.queryByText(/sign up with github/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/sign up with google/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/continue with github/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/continue with google/i)).not.toBeInTheDocument()

        // Nothing may link out to the removed OAuth start endpoints.
        const externalLinks = document.querySelectorAll('a[href*="external-login"]')
        expect(externalLinks).toHaveLength(0)
    })

    it('does not link unauthenticated visitors to a registration page', () => {
        renderAuthPage()

        expect(screen.queryByText(/don't have an account/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/create one/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/sign up/i)).not.toBeInTheDocument()

        const registerLinks = document.querySelectorAll('a[href*="register"]')
        expect(registerLinks).toHaveLength(0)
    })

    it('keeps the forgot-password path available', () => {
        renderAuthPage()

        expect(screen.getByRole('button', { name: /forgot password/i })).toBeInTheDocument()
    })

    it('tells visitors how to obtain an account instead of offering signup', () => {
        renderAuthPage()

        expect(screen.getByText(/accounts are created by your administrator/i)).toBeInTheDocument()
    })

    // "Keep me signed in" replaced a hardcoded rememberMe: false, so assert the
    // checkbox actually reaches the login request rather than just rendering.
    it('sends the "keep me signed in" choice to the login request', async () => {
        renderAuthPage()

        fireEvent.change(screen.getByLabelText(/email address/i), { target: { value: 'admin@example.com' } })
        fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'correct-horse' } })
        fireEvent.click(screen.getByRole('checkbox', { name: /keep me signed in/i }))
        fireEvent.click(screen.getByRole('button', { name: /^sign in$/i }))

        await waitFor(() => {
            expect(login).toHaveBeenCalledWith({
                email: 'admin@example.com',
                password: 'correct-horse',
                rememberMe: true,
            })
        })
    })
})
