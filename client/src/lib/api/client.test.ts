import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AxiosAdapter } from 'axios'
import apiClient from './client'
import { API_ERROR_EVENT } from './error-events'
import { SESSION_EXPIRED_EVENT } from './session-expiry'

/** An adapter that answers every request with `status`, the way axios sees a server reply. */
const answering = (status: number): AxiosAdapter => (config) =>
    Promise.reject(Object.assign(new Error(`Request failed with status code ${status}`), {
        isAxiosError: true, config, response: { status, data: {}, headers: {}, config, statusText: '' },
    }))

function listen(name: string) {
    const seen = vi.fn()
    window.addEventListener(name, seen)
    return { seen, stop: () => window.removeEventListener(name, seen) }
}

afterEach(() => vi.restoreAllMocks())

describe('a 401 on an ordinary request ends the session', () => {
    it('announces the session as expired instead of raising an error snackbar', async () => {
        const expired = listen(SESSION_EXPIRED_EVENT)
        const errors = listen(API_ERROR_EVENT)

        await expect(apiClient.get('/annualleaves', { adapter: answering(401) })).rejects.toBeTruthy()

        expect(expired.seen).toHaveBeenCalledTimes(1)
        expect(errors.seen).not.toHaveBeenCalled()
        expired.stop(); errors.stop()
    })

    it('leaves a 401 from the sign-in call to the sign-in screen', async () => {
        const expired = listen(SESSION_EXPIRED_EVENT)

        await expect(apiClient.post('/account/login', {}, { adapter: answering(401) })).rejects.toBeTruthy()

        expect(expired.seen).not.toHaveBeenCalled()
        expired.stop()
    })

    it('still raises the snackbar for other failures', async () => {
        const expired = listen(SESSION_EXPIRED_EVENT)
        const errors = listen(API_ERROR_EVENT)

        await expect(apiClient.get('/annualleaves', { adapter: answering(500) })).rejects.toBeTruthy()

        expect(expired.seen).not.toHaveBeenCalled()
        expect(errors.seen).toHaveBeenCalledTimes(1)
        expired.stop(); errors.stop()
    })
})
