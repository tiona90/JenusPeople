import type { HubConnectionState, IRetryPolicy } from '@microsoft/signalr'

/** The part of a HubConnection this module drives, so a test can stand in for it. */
export interface HubLike {
    state: HubConnectionState
    start(): Promise<void>
    stop(): Promise<void>
    onclose(callback: () => void): void
    onreconnected(callback: () => void): void
}

const BACKOFF_MS = [0, 2_000, 10_000, 30_000]
const MAX_DELAY_MS = 60_000

/**
 * How long to wait before retry number <paramref name="attempt"/>: the SDK's own
 * opening steps, then once a minute for as long as it takes. The SDK's default
 * gives up after the fourth retry, about 42 seconds in, which is shorter than a
 * deploy or an app-pool recycle.
 */
export function reconnectDelayMs(attempt: number): number {
    return BACKOFF_MS[attempt] ?? MAX_DELAY_MS
}

/** For `withAutomaticReconnect`: never answers null, so the client never stops retrying. */
export const reconnectPolicy: IRetryPolicy = {
    nextRetryDelayInMilliseconds: ({ previousRetryCount }) => reconnectDelayMs(previousRetryCount),
}

/**
 * Starts the hub and keeps it started: a first start that fails is retried on
 * the same backoff, a connection that closes for good (the automatic reconnect
 * exhausted, or the server ending it) is started again, and every return after a
 * gap calls <paramref name="onReconnected"/> — events sent while the tab was
 * disconnected are gone, so the caller refetches. Returns a stop function.
 */
export function keepConnected(hub: HubLike, onReconnected: () => void): () => void {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let attempt = 0
    let hasConnected = false

    const startSoon = () => {
        if (stopped) return
        timer = setTimeout(async () => {
            if (stopped) return
            try {
                await hub.start()
                attempt = 0
                if (hasConnected) onReconnected()
                hasConnected = true
            } catch {
                attempt += 1
                startSoon()
            }
        }, reconnectDelayMs(attempt))
    }

    hub.onreconnected(() => onReconnected())
    hub.onclose(() => {
        attempt = 0
        startSoon()
    })
    startSoon()

    return () => {
        stopped = true
        if (timer) clearTimeout(timer)
        void hub.stop()
    }
}
