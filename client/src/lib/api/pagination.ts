import type { AxiosResponse } from 'axios'

/** Optional pagination inputs. Omit both to fetch the full list (unpaged). */
export interface PageParams {
    page?: number
    pageSize?: number
}

/** A page of results plus the total count read from response headers. */
export interface Paged<T> {
    items: T[]
    total: number
    page: number
    pageSize: number
}

/**
 * The list endpoints keep returning a plain JSON array and advertise pagination
 * via headers (X-Total-Count / X-Page / X-Page-Size). This wraps such a response
 * into a {@link Paged} object, falling back to the array length when the headers
 * are absent (e.g. an unpaged request).
 */
export function toPaged<T>(res: AxiosResponse<T[]>, requested?: PageParams): Paged<T> {
    const items = res.data ?? []
    const total = Number(res.headers['x-total-count'] ?? items.length)
    const page = Number(res.headers['x-page'] ?? requested?.page ?? 1)
    const pageSize = Number(res.headers['x-page-size'] ?? requested?.pageSize ?? items.length)
    return { items, total, page, pageSize }
}
