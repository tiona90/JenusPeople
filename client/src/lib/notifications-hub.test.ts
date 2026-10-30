import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HubConnectionState } from '@microsoft/signalr'
import { keepConnected, reconnectDelayMs, type HubLike } from './notifications-hub'

/**
 * withAutomaticReconnect() on its defaults retries at 0, 2, 10 and 30 seconds
 * and then gives up for good, and a first start() that failed was swallowed.
 * Any API restart longer than about 42 seconds — a deploy, an app-pool recycle,
 * the API starting before SQL Server — left the tab without live updates until
 * the page was reloaded, and pages that do not poll went quietly stale.
 */
function fakeHub(startResults: Array<'fail' | 'ok'>) {
    const handlers: { close?: () => void; reconnected?: () => void } = {}
    const hub: HubLike & { fireClose: () => void; fireReconnected: () => void } = {
        state: HubConnectionState.Disconnected,
        start: vi.fn(async () => {
            const next = startResults.shift() ?? 'ok'
            if (next === 'fail') throw new Error('API down')
            hub.state = HubConnectionState.Connected
        }),
        stop: vi.fn(async () => { hub.state = HubConnectionState.Disconnected }),
        onclose: (cb) => { handlers.close = cb },
        onreconnected: (cb) => { handlers.reconnected = cb },
        fireClose: () => { hub.state = HubConnectionState.Disconnected; handlers.close?.() },
        fireReconnected: () => handlers.reconnected?.(),
    }
    return hub
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('reconnectDelayMs', () => {
    it('backs off and never gives up', () => {
        expect(reconnectDelayMs(0)).toBe(0)
        expect(reconnectDelayMs(1)).toBe(2_000)
        expect(reconnectDelayMs(3)).toBe(30_000)
        expect(reconnectDelayMs(50)).toBe(60_000)
    })
})

describe('keepConnected', () => {
    it('keeps trying a first start that fails', async () => {
        const hub = fakeHub(['fail', 'fail', 'ok'])
        keepConnected(hub, vi.fn())

        await vi.advanceTimersByTimeAsync(0)
        await vi.advanceTimersByTimeAsync(2_000)
        await vi.advanceTimersByTimeAsync(10_000)

        expect(hub.start).toHaveBeenCalledTimes(3)
        expect(hub.state).toBe(HubConnectionState.Connected)
    })

    it('starts again after the connection closes for good, and catches up when it does', async () => {
        const hub = fakeHub(['ok', 'ok'])
        const onReconnected = vi.fn()
        keepConnected(hub, onReconnected)
        await vi.advanceTimersByTimeAsync(0)

        hub.fireClose()
        await vi.advanceTimersByTimeAsync(0)

        expect(hub.start).toHaveBeenCalledTimes(2)
        expect(onReconnected).toHaveBeenCalledTimes(1)
    })

    it('catches up after the client reconnects on its own', async () => {
        const hub = fakeHub(['ok'])
        const onReconnected = vi.fn()
        keepConnected(hub, onReconnected)
        await vi.advanceTimersByTimeAsync(0)

        hub.fireReconnected()

        expect(onReconnected).toHaveBeenCalledTimes(1)
    })

    it('stops for good when told to', async () => {
        const hub = fakeHub(['fail'])
        const stop = keepConnected(hub, vi.fn())
        await vi.advanceTimersByTimeAsync(0)

        stop()
        await vi.advanceTimersByTimeAsync(120_000)

        expect(hub.start).toHaveBeenCalledTimes(1)
        expect(hub.stop).toHaveBeenCalled()
    })
})
