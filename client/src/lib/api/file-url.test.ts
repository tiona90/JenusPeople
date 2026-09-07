import { describe, expect, it } from 'vitest'
import { resolveFileUrl } from './file-url'

describe('resolveFileUrl', () => {
    it('returns undefined for an absent reference', () => {
        expect(resolveFileUrl(null)).toBeUndefined()
        expect(resolveFileUrl(undefined)).toBeUndefined()
        expect(resolveFileUrl('')).toBeUndefined()
        expect(resolveFileUrl('   ')).toBeUndefined()
    })

    it('leaves a legacy absolute Cloudinary URL untouched', () => {
        const cloudinary = 'https://res.cloudinary.com/demo/image/upload/v1/avatar.png'

        expect(resolveFileUrl(cloudinary)).toBe(cloudinary)
    })

    it('keeps a stored path as-is when the API is served from the same origin', () => {
        // The default setup: apiBaseUrl is the relative '/api', proxied in dev and
        // served from wwwroot in production, so the path already resolves.
        expect(resolveFileUrl('/api/files/abc-123', '/api')).toBe('/api/files/abc-123')
    })

    it('resolves a stored path against the API origin when one is configured', () => {
        // With VITE_API_BASE_URL pointing elsewhere, a bare '/api/...' path would
        // otherwise resolve against the SPA's origin and 404.
        expect(resolveFileUrl('/api/files/abc-123', 'https://api.example.test/api'))
            .toBe('https://api.example.test/api/files/abc-123')
    })

    it('trims incidental whitespace around a reference', () => {
        expect(resolveFileUrl('  /api/files/abc-123  ', '/api')).toBe('/api/files/abc-123')
    })
})
