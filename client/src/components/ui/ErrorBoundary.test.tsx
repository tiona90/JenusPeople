import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ErrorBoundary from './ErrorBoundary'

/**
 * One render error — an unexpected null from an older API, say — used to blank
 * the whole app to a white screen, sidebar and all, with no way back but a
 * reload the user had to think of. The boundary keeps the error to the page it
 * happened on and offers the way back.
 */
function Boom({ explode }: { explode: boolean }) {
    if (explode) throw new Error('Cannot read properties of null')
    return <div>Page content</div>
}

beforeEach(() => {
    // React logs the caught error; keep the test output readable.
    vi.spyOn(console, 'error').mockImplementation(() => {})
})
afterEach(() => vi.restoreAllMocks())

describe('ErrorBoundary', () => {
    it('renders its children when nothing goes wrong', () => {
        render(<ErrorBoundary resetKey="/a"><Boom explode={false} /></ErrorBoundary>)

        expect(screen.getByText('Page content')).toBeTruthy()
    })

    it('replaces a crashed page with a message and a reload button', () => {
        render(<ErrorBoundary resetKey="/a"><Boom explode /></ErrorBoundary>)

        expect(screen.getByText('Something went wrong on this page.')).toBeTruthy()
        expect(screen.getByRole('button', { name: 'Reload' })).toBeTruthy()
        expect(screen.queryByText('Cannot read properties of null')).toBeNull()
    })

    it('recovers when the user navigates elsewhere', () => {
        const { rerender } = render(<ErrorBoundary resetKey="/a"><Boom explode /></ErrorBoundary>)

        rerender(<ErrorBoundary resetKey="/b"><Boom explode={false} /></ErrorBoundary>)

        expect(screen.getByText('Page content')).toBeTruthy()
    })

    it('reloads the page from the button', () => {
        const reload = vi.fn()
        vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, reload } as Location)
        render(<ErrorBoundary resetKey="/a"><Boom explode /></ErrorBoundary>)

        fireEvent.click(screen.getByRole('button', { name: 'Reload' }))

        expect(reload).toHaveBeenCalled()
    })
})
