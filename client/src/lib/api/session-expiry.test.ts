import { describe, expect, it } from 'vitest'
import { isSessionExpiry } from './session-expiry'

/**
 * A 401 from an ordinary request means the cookie no longer signs anyone in — it
 * expired, or the account was deactivated and its security stamp rotated. The app
 * used to stay in the shell with every poll failing and an error snackbar every
 * 15 seconds. Only the calls that are *about* signing in answer 401 as part of
 * their normal job, and those are left to their own screens.
 */
describe('isSessionExpiry', () => {
    it('reads a 401 from an ordinary request as the session ending', () => {
        expect(isSessionExpiry(401, '/annualleaves')).toBe(true)
        expect(isSessionExpiry(401, '/timesheets?includeAttendance=true')).toBe(true)
    })

    it('leaves the sign-in calls to answer for themselves', () => {
        expect(isSessionExpiry(401, '/account/login')).toBe(false)
        expect(isSessionExpiry(401, '/account/user-info')).toBe(false)
        expect(isSessionExpiry(401, '/Account/CurrentUser')).toBe(false)
        expect(isSessionExpiry(401, '/account/forgot-password')).toBe(false)
        expect(isSessionExpiry(401, '/account/reset-password')).toBe(false)
    })

    it('is only about 401', () => {
        expect(isSessionExpiry(403, '/annualleaves')).toBe(false)
        expect(isSessionExpiry(500, '/annualleaves')).toBe(false)
        expect(isSessionExpiry(undefined, '/annualleaves')).toBe(false)
    })
})
