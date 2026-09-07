import { apiBaseUrl } from './client'

const ABSOLUTE_URL = /^https?:\/\//i

/**
 * Resolves a stored-file reference into something an `<img src>` or `<a href>`
 * can use.
 *
 * Two shapes coexist in `User.imageUrl` and `AnnualLeave.evidenceUrl`:
 *
 * - `/api/files/{id}` — files held in the database, uploaded since Cloudinary
 *   was removed. Same-origin, so the auth cookie is sent automatically.
 * - `https://res.cloudinary.com/...` — rows written before that change, kept
 *   working rather than migrated.
 *
 * A relative path needs no help in the default setup, where `apiBaseUrl` is
 * itself relative (`/api`, proxied by Vite in development and served from
 * wwwroot in production). It only needs rewriting when `VITE_API_BASE_URL`
 * points at a different origin, where a bare path would otherwise resolve
 * against the SPA's origin and 404.
 */
export const resolveFileUrl = (
    value?: string | null,
    baseUrl: string = apiBaseUrl,
): string | undefined => {
    const reference = value?.trim()
    if (!reference) return undefined

    if (ABSOLUTE_URL.test(reference)) return reference

    if (reference.startsWith('/') && ABSOLUTE_URL.test(baseUrl)) {
        return new URL(reference, baseUrl).toString()
    }

    return reference
}
