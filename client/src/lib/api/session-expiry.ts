export const SESSION_EXPIRED_EVENT = 'annualleave:session-expired'

/** The calls whose job is signing in: a 401 from them is an answer, not an expired session. */
const SIGN_IN_PATHS = ['/account/login', '/account/user-info', '/account/currentuser', '/account/forgot-password', '/account/reset-password']

/**
 * Whether a failed request means the session has ended — the cookie expired, or
 * the account was deactivated and its security stamp rotated. Every ordinary
 * request then answers 401, so the app has to sign the user out rather than keep
 * polling into a snackbar every 15 seconds.
 */
export function isSessionExpiry(status: number | undefined, url: string | undefined): boolean {
    if (status !== 401) return false
    const path = String(url ?? '').toLowerCase()
    return !SIGN_IN_PATHS.some((signIn) => path.includes(signIn))
}

export function emitSessionExpired() {
    window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT))
}
