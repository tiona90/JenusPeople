import { afterEach, describe, expect, it, vi } from 'vitest'
import apiClient from './client'
import { getOpenAnnualLeaves } from './annual-leaves'
import { getOpenTimesheets } from './timesheets'
import { getRecentLeaveStatusHistories } from './leave-status-histories'
import { getRecentTimesheetStatusHistories } from './timesheet-status-histories'

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

/**
 * The employee's bell shows the eight newest changes and counts the unread ones
 * from the last week, but downloaded every status change the person ever had.
 * It reads the newest 50 instead — the endpoints page newest first in SQL.
 */
describe('the recent status histories', () => {
    it('ask for the newest leave changes only', async () => {
        const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: [] })
        await getRecentLeaveStatusHistories()
        expect(get).toHaveBeenCalledWith('/leavestatushistories', { params: { page: 1, pageSize: 50 } })
    })

    it('ask for the newest timesheet changes only', async () => {
        const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: [] })
        await getRecentTimesheetStatusHistories()
        expect(get).toHaveBeenCalledWith('/timesheetstatushistories', { params: { page: 1, pageSize: 50 } })
    })
})
