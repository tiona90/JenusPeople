import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import ResetPasswordForm from './ResetPasswordForm'

vi.mock('../../lib/api', () => ({
    resetPassword: vi.fn(),
}))

// The admin welcome email points at this same screen with `welcome=1`, because
// it carries the same kind of token. The recipient has never had a password
// though, so the copy has to stop talking about resetting one.
function renderAt(search: string) {
    window.history.pushState({}, '', `/reset-password${search}`)

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    return render(
        <QueryClientProvider client={queryClient}>
            <ResetPasswordForm onBackToLogin={() => {}} onRequestNewLink={() => {}} />
        </QueryClientProvider>,
    )
}

const LINK = '?email=newjoiner%40example.test&token=abc123'

describe('ResetPasswordForm', () => {
    it('asks a new joiner to set a password, not reset one', () => {
        renderAt(`${LINK}&welcome=1`)

        expect(screen.getByText('Set your password')).toBeInTheDocument()
        expect(screen.getByText(/activate your jenus people account/i)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /^set password$/i })).toBeInTheDocument()

        expect(screen.queryByText('Choose a new password')).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /^reset password$/i })).not.toBeInTheDocument()
        // An invite is not a "reset", so the recovery link shouldn't call it one.
        expect(screen.queryByText(/request a new reset email/i)).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: /email me a new link/i })).toBeInTheDocument()
    })

    it('keeps the reset wording for an ordinary forgot-password link', () => {
        renderAt(LINK)

        expect(screen.getByText('Choose a new password')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /^reset password$/i })).toBeInTheDocument()
        expect(screen.queryByText('Set your password')).not.toBeInTheDocument()
    })

    it('prefills the email from the link so the token and address always match', () => {
        renderAt(`${LINK}&welcome=1`)

        expect(screen.getByLabelText(/email address/i)).toHaveValue('newjoiner@example.test')
    })

    it('tells an invited user how to recover an expired invitation', () => {
        renderAt('?welcome=1')

        expect(screen.getByText(/this invitation link is incomplete or has expired/i)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /^set password$/i })).toBeDisabled()
    })
})
