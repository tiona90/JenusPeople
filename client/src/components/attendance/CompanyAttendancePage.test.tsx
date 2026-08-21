import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { CompanyAttendance, UserInfo } from '../../lib/types'
import CompanyAttendancePage from './CompanyAttendancePage'
import DashboardHome from '../annual-leave/DashboardHome'

// An admin reaching Company Attendance has just scrolled past the dashboard's
// "Today's issues" and "Recent activity" cards, which read the same query. The
// two surfaces used to disagree about the same person: this page marked everyone
// without a check-in 🔴 and "flagged", while the dashboard's icon map tested
// 'checked in' before 'not check' and so painted them 🟢. Neither reading is
// defensible at 13:00, when the data cannot separate an unscheduled absence
// from a late start, so both pages now share one feed map and read neutral.
vi.mock('../../lib/api')
vi.mock('../../lib/mobx')

const api = vi.mocked(await import('../../lib/api'))
const mobx = vi.mocked(await import('../../lib/mobx'))

const ADMIN: UserInfo = {
    id: 'u-admin',
    userName: 'admin@annualleave.com',
    email: 'admin@annualleave.com',
    displayName: 'Admin User',
    imageUrl: '',
    roles: ['Admin'],
}

/* The 13:00 shape of one company day: two of twelve working, ten with no
   check-in, one of whom (Employee 2A) has since arrived late. */
const COMPANY: CompanyAttendance = {
    total: 12,
    in: 2,
    break: 0,
    out: 10,
    leave: 0,
    totalMinutesToday: 84,
    avgMinutesToday: 42,
    departments: [
        { name: 'Engineering', total: 6, in: 1, break: 0, out: 5, leave: 0, totalMinutes: 66, avgMinutes: 66 },
        { name: 'Finance', total: 6, in: 1, break: 0, out: 5, leave: 0, totalMinutes: 18, avgMinutes: 18 },
    ],
    recent: [
        { employeeName: 'Employee 2A', departmentName: 'Finance', action: 'Late check-in', at: '2026-08-21T15:59:00Z', minutesAgo: 21 },
        { employeeName: 'Employee 1A', departmentName: 'Engineering', action: 'Back from break', at: '2026-08-21T15:16:00Z', minutesAgo: 64 },
        // Synthetic rows carry no timestamp, which is the only thing separating
        // them from real events.
        { employeeName: 'Manager Two', departmentName: 'Finance', action: 'Not checked in', at: null, minutesAgo: null },
        { employeeName: 'Theodoros Iona', departmentName: 'Finance', action: 'Not checked in', at: null, minutesAgo: null },
        { employeeName: 'Employee 1D', departmentName: 'Engineering', action: 'Not checked in', at: null, minutesAgo: null },
    ],
    issues: [
        { severity: 'danger', title: '5 not checked in (Engineering)', detail: 'No check-in by 13:00 · likely unscheduled absence' },
        { severity: 'danger', title: '5 not checked in (Finance)', detail: 'No check-in by 13:00 · likely unscheduled absence' },
        { severity: 'warning', title: '1 late check-in', detail: 'Employee 2A (Finance) · 239 min late' },
        { severity: 'success', title: 'No unusual overtime', detail: 'All employees within healthy hour ranges' },
    ],
}

function newClient() {
    return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

function renderCompanyPage() {
    return render(
        <QueryClientProvider client={newClient()}>
            <CompanyAttendancePage />
        </QueryClientProvider>,
    )
}

function renderAdminDashboard() {
    return render(
        <MemoryRouter initialEntries={['/']}>
            <QueryClientProvider client={newClient()}>
                <DashboardHome />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}

/**
 * The feed row for one person, on either page: the smallest element carrying
 * both their name and a status glyph. The glyph sits beside the name on one page
 * and one level up on the other, so match on the row, not on structure.
 */
function feedRow(name: string) {
    const matches = screen.getAllByText((_content, el) => {
        const text = el?.textContent ?? ''
        return text.includes(name) && /[\u{1F7E2}\u{26AA}\u{1F534}\u{2615}]|\u{26A0}/u.test(text)
    })
    // getAllByText walks the DOM in document order, so ancestors come first and
    // the last match is the innermost element — the row itself.
    return matches[matches.length - 1]
}

beforeEach(() => {
    vi.clearAllMocks()
    api.getCompanyAttendance.mockResolvedValue(COMPANY)
    api.getAnnualLeaves.mockResolvedValue([])
    api.getTimesheets.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([])
    mobx.useStore.mockReturnValue({
        authStore: { user: ADMIN },
        uiStore: { navigateToCompanyAttendance: vi.fn() },
    } as never)
})

describe('not-checked-in reads the same on the dashboard and on Company Attendance', () => {
    it('marks a person with no check-in neutrally on Company Attendance', async () => {
        renderCompanyPage()
        await screen.findByText(/Theodoros Iona/)

        const row = feedRow('Theodoros Iona')
        expect(row.textContent).toContain('\u{26AA}')
        expect(row.textContent).not.toContain('\u{1F534}')
        // "flagged" asserted a judgement a timestamp-less row cannot support.
        expect(row.textContent).not.toMatch(/flagged/i)
    })

    it('marks the same person the same way on the admin dashboard', async () => {
        renderAdminDashboard()
        await screen.findByText(/Theodoros Iona/)

        const row = feedRow('Theodoros Iona')
        expect(row.textContent).toContain('\u{26AA}')
        expect(row.textContent).not.toContain('\u{1F534}')
        // Was 🟢: 'Not checked in' contains 'checked in', so the old map badged
        // exactly the people who had not arrived as working.
        expect(row.textContent).not.toContain('\u{1F7E2}')
    })

    it('marks a late arrival as late on Company Attendance', async () => {
        renderCompanyPage()
        await screen.findByText(/Employee 2A/)

        expect(feedRow('Employee 2A').textContent).toContain('\u{26A0}')
    })

    it('marks that late arrival as late on the dashboard too', async () => {
        renderAdminDashboard()
        // Named twice on this page: once in the feed, once in the late-check-in
        // issue detail in the card beside it.
        await screen.findAllByText(/Employee 2A/)

        const row = feedRow('Employee 2A')
        expect(row.textContent).toContain('\u{26A0}')
        expect(row.textContent).not.toContain('\u{1F7E2}')
    })

    it('states the not-checked-in count without prescribing a follow-up', async () => {
        renderCompanyPage()

        expect(await screen.findByText('Not Checked In')).toBeInTheDocument()
        expect(screen.getByText('no check-in recorded yet')).toBeInTheDocument()
        expect(screen.queryByText(/requires follow-up/i)).not.toBeInTheDocument()
    })
})

describe("Today's Issues lives on the dashboard only", () => {
    it('does not repeat the issue feed on Company Attendance', async () => {
        renderCompanyPage()

        // Everything else on the page survives: stat cards, department table,
        // activity log.
        expect(await screen.findByText('Recent Activity')).toBeInTheDocument()
        expect(screen.getByText('By Department')).toBeInTheDocument()

        expect(screen.queryByText(/Today's Issues/i)).not.toBeInTheDocument()
        expect(screen.queryByText('5 not checked in (Engineering)')).not.toBeInTheDocument()
        expect(screen.queryByText(/likely unscheduled absence/)).not.toBeInTheDocument()
    })

    it('still shows it on the dashboard', async () => {
        renderAdminDashboard()

        expect(await screen.findByText("Today's issues")).toBeInTheDocument()
        expect(screen.getByText('5 not checked in (Engineering)')).toBeInTheDocument()
    })
})
