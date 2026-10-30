import { afterEach, describe, expect, it, vi } from 'vitest'
import apiClient from './client'
import { getOpenAnnualLeaves } from './annual-leaves'
import { getOpenTimesheets } from './timesheets'

/**
 * The bell's two lists ask the server for open rows only (`openOnly`), rather
 * than every leave and timesheet in scope.
 */
afterEach(() => vi.restoreAllMocks())

describe('the open-only lists', () => {
    it('ask for open leave only', async () => {
        const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: [] })
        await getOpenAnnualLeaves()
        expect(get).toHaveBeenCalledWith('/annualleaves', { params: { openOnly: true } })
    })

    it('ask for open timesheets only', async () => {
        const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: [] })
        await getOpenTimesheets()
        expect(get).toHaveBeenCalledWith('/timesheets', { params: { openOnly: true } })
    })
})
