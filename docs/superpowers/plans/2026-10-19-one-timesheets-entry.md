# One Timesheets Entry — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Employees and Managers see one **Timesheets** entry in the sidebar instead of "Submit Timesheet" and "My Timesheets"; it stays highlighted in the editor; the page title agrees; and the My Timesheets page's week button says what it will do.

**Architecture:** Client only. `Sidebar.tsx` loses one item per role and the remaining one is active on both `/timesheets` and `/new-timesheet`; sidebar items gain `aria-current="page"` so the active one is something a test and a screen reader can see. `Topbar.tsx` titles both routes "Timesheets" for those roles. A small pure helper, `thisWeekActionLabel` in a new `client/src/lib/timesheet-week.ts`, picks the button's label from the current week's status. The `/new-timesheet` route and every link to it stay.

**Tech Stack:** React 19, TypeScript, MUI 7, react-router-dom, Vitest + Testing Library. Run tests with `node_modules/.bin/vitest` from `client/` (never bare `npx vitest`).

**Spec:** `docs/superpowers/specs/2026-10-19-simpler-tasks-and-timesheets-design.md`, section "Phase 4 — One Timesheets entry in the sidebar".

## Global Constraints

- Applies to the **Employee** and **Manager** sidebars only. The HR Administrator's and System Administrator's navigation have no such pair and do not change; neither does the Manager's "Approvals" entry (`/timesheets-management`).
- The surviving entry is labelled **Timesheets**, keeps the `AccessTimeRoundedIcon`, navigates with `uiStore.navigateToTimesheets()`, and is active on `/timesheets` **and** `/new-timesheet`. `onPage` already matches a route exactly or as a prefix followed by `/`, so `/timesheets-management` does not light it.
- The `/new-timesheet` route, `uiStore.navigateToNewTimesheet(weekStart?)`, the dashboard shortcuts and Phase 3's snackbar link are unchanged.
- **Spec 4.2 is mostly already built.** `MyTimesheetPage` already has an "📝 Open this week" button above the list, a current-week card with "Continue filling" / "Open week", and an empty card with "📝 Start this week". This plan does not add a second button; it makes the existing one's label follow the week: **Start this week** (no sheet), **Continue this week** (Draft or Rejected), **View this week** (Submitted, Resubmitted, Approved). It keeps navigating to the editor's current week.
- Topbar titles: `/timesheets` reads **Timesheets** for anyone who is not an administrator (was "My Timesheets"), and `/new-timesheet` reads **Timesheets** (was "Submit Timesheet"). Administrator titles are unchanged.
- Commit after every task, message in the repository's style, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A Manager in the editor** must see Timesheets highlighted, not nothing and not Approvals. (Task 1)
2. **A Manager on Approvals** (`/timesheets-management`) must not see Timesheets highlighted as well. (Task 1)
3. **An HR Administrator's sidebar** must be unchanged — no Timesheets entry appears for them. (Task 1)
4. **A Rejected current week** must read "Continue this week", the action that lets them fix it, not "View". (Task 3)
5. **A week with no sheet yet** must read "Start this week". (Task 3)

---

### Task 1: One Timesheets entry in the sidebar

**Files:**
- Modify: `client/src/components/layout/Sidebar.tsx` (Manager entries ~lines 289–290; Employee entries ~lines 308–309; the item `<Box>` ~line 384; the `NoteAddRoundedIcon` import ~line 30)
- Test: `client/src/components/layout/Sidebar.test.tsx`

**Interfaces:**
- Produces: each sidebar item carries `aria-current="page"` when active, nothing otherwise.

- [ ] **Step 1: Write the failing tests**

In `client/src/components/layout/Sidebar.test.tsx`:

1. Give the module-level `renderSidebarAs` (the one at ~line 137) a path parameter:

```tsx
function renderSidebarAs(user: UserInfo, path = '/dashboard') {
    mobx.useStore.mockReturnValue({ authStore: { user }, uiStore: { sidebarMode: 'light' } } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <MemoryRouter initialEntries={[path]}>
            <QueryClientProvider client={queryClient}>
                <Sidebar />
            </QueryClientProvider>
        </MemoryRouter>,
    )
}
```

2. In the test `'puts Tasks under Collaboration for an Employee too, after their own pages'`, change `screen.getByText('My Timesheets')` to `screen.getByText('Timesheets')`.

3. Append:

```tsx
describe('One Timesheets entry for the people who keep one', () => {
    const MANAGER: UserInfo = { ...EMPLOYEE, roles: ['Manager'] }
    const HR: UserInfo = { ...EMPLOYEE, departmentId: null, roles: ['HR Administrator'] }

    it.each([['Employee', EMPLOYEE], ['Manager', MANAGER]])('gives a %s one Timesheets entry and no Submit Timesheet', (_role, user) => {
        renderSidebarAs(user)
        expect(screen.getAllByText('Timesheets')).toHaveLength(1)
        expect(screen.queryByText('Submit Timesheet')).toBeNull()
        expect(screen.queryByText('My Timesheets')).toBeNull()
    })

    it.each([['/timesheets'], ['/new-timesheet']])('highlights Timesheets on %s', (path) => {
        renderSidebarAs(MANAGER, path)
        expect(screen.getByText('Timesheets').closest('[aria-current]')).toHaveAttribute('aria-current', 'page')
        expect(screen.getByText('Approvals').closest('[aria-current]')).toBeNull()
    })

    it("does not highlight Timesheets on a Manager's Approvals", () => {
        renderSidebarAs(MANAGER, '/timesheets-management')
        expect(screen.getByText('Approvals').closest('[aria-current]')).toHaveAttribute('aria-current', 'page')
        expect(screen.getByText('Timesheets').closest('[aria-current]')).toBeNull()
    })

    it("leaves an HR Administrator's navigation as it was", () => {
        renderSidebarAs(HR)
        expect(screen.queryByText('Submit Timesheet')).toBeNull()
        expect(screen.queryByText('My Timesheets')).toBeNull()
        // HR's own entry under Leave & Time is "Timesheets" (All Timesheets), as before.
        expect(screen.getAllByText('Timesheets')).toHaveLength(1)
    })
})
```

Note on the last test: the HR Administrator's sidebar already has a "Timesheets" item (Leave & Time → `/timesheets-management`). The assertion pins that it is still exactly one — the change must not add a second. Read `Sidebar.tsx` around the HR block (~line 249) to confirm before running; if HR's label differs, assert on that label instead and keep the `Submit Timesheet` / `My Timesheets` absences.

- [ ] **Step 2: Run them to verify they fail**

```bash
node_modules/.bin/vitest run src/components/layout/Sidebar.test.tsx
```

Expected: FAIL — "Submit Timesheet" and "My Timesheets" are present for the Employee and Manager; no item carries `aria-current`.

- [ ] **Step 3: Implement**

In `client/src/components/layout/Sidebar.tsx`:

1. In the Manager block, replace the two lines

```tsx
            { kind: 'item', label: 'Submit Timesheet', icon: <NoteAddRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToNewTimesheet(), active: onPage('/new-timesheet') },
            { kind: 'item', label: 'My Timesheets', icon: <AccessTimeRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTimesheets(), active: onPage('/timesheets') },
```

with

```tsx
            // One entry for the week and its history: the page opens on the current week,
            // and the editor is a step from it, not a sibling in the menu.
            { kind: 'item', label: 'Timesheets', icon: <AccessTimeRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToTimesheets(), active: onPage('/timesheets', '/new-timesheet') },
```

2. Make the same replacement in the Employee block (same two lines, ~lines 308–309).

3. Remove the now-unused `import NoteAddRoundedIcon from '@mui/icons-material/NoteAddRounded'` if nothing else in the file uses it (`grep -n NoteAddRounded` to check).

4. On the item `<Box key={\`item-${i}\`} onClick={entry.onClick} …>`, add:

```tsx
                                aria-current={entry.active ? 'page' : undefined}
```

5. Update the Manager block's comment above the entries, which mentions "My Timesheets": change `"My Leave" sat next to "Team Leave" and "My Timesheets" next to "Team Timesheets".` to `"My Leave" sat next to "Team Leave" and the manager's own timesheets next to the team's.`

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/layout
```

Expected: PASS across `Sidebar`, `Topbar`, `AttendanceWidget`, `ChildrenSection`.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/layout/Sidebar.tsx client/src/components/layout/Sidebar.test.tsx
git commit -m "Sidebar: one Timesheets entry for Employees and Managers, lit in the editor too

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The page title agrees

**Files:**
- Modify: `client/src/components/layout/Topbar.tsx` (~lines 385–386)
- Test: `client/src/components/layout/Topbar.test.tsx`

**Interfaces:** none.

- [ ] **Step 1: Write the failing tests**

In `client/src/components/layout/Topbar.test.tsx`, give `renderTopbarAs` a path parameter, defaulting to today's `/dashboard`:

```tsx
function renderTopbarAs(user: UserInfo, path = '/dashboard') {
    …
        <MemoryRouter initialEntries={[path]}>
    …
}
```

and append (reusing whatever Employee/Manager `UserInfo` fixtures the file already defines — read its top; if it has none, build one from an existing fixture with `roles: ['Employee']`):

```tsx
describe('Topbar title for the one Timesheets entry', () => {
    it.each([['/timesheets'], ['/new-timesheet']])('titles %s "Timesheets" for an Employee', (path) => {
        renderTopbarAs({ ...EMPLOYEE_FIXTURE, roles: ['Employee'] }, path)
        expect(screen.getByText('Timesheets')).toBeInTheDocument()
        expect(screen.queryByText('Submit Timesheet')).toBeNull()
        expect(screen.queryByText('My Timesheets')).toBeNull()
    })
})
```

Replace `EMPLOYEE_FIXTURE` with the file's actual fixture name.

- [ ] **Step 2: Run them to verify they fail**

```bash
node_modules/.bin/vitest run src/components/layout/Topbar.test.tsx -t "one Timesheets entry"
```

Expected: FAIL — the titles read "My Timesheets" and "Submit Timesheet".

- [ ] **Step 3: Implement**

In `Topbar.tsx`:

```tsx
    else if (path.startsWith('/timesheets')) pageTitle = isAdminUser ? 'All Timesheets' : 'Timesheets'
    else if (path.startsWith('/new-timesheet')) pageTitle = 'Timesheets'
```

- [ ] **Step 4: Run them to verify they pass**

```bash
node_modules/.bin/vitest run src/components/layout
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/layout/Topbar.tsx client/src/components/layout/Topbar.test.tsx
git commit -m "Topbar: title the timesheet pages after the one sidebar entry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The week button says what it will do; CLAUDE.md; full run

**Files:**
- Create: `client/src/lib/timesheet-week.ts`
- Test: `client/src/lib/timesheet-week.test.ts`
- Modify: `client/src/components/timesheet/MyTimesheetPage.tsx` (the "📝 Open this week" button ~lines 270–281)
- Modify: `CLAUDE.md` (the `Timesheet` row of the Domain Model table)

**Interfaces:**
- Produces: `thisWeekActionLabel(status: TimesheetStatus | null | undefined): 'Start this week' | 'Continue this week' | 'View this week'`.

- [ ] **Step 1: Write the failing test**

Create `client/src/lib/timesheet-week.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { thisWeekActionLabel } from './timesheet-week'

describe('thisWeekActionLabel', () => {
    it('starts a week with no sheet', () => {
        expect(thisWeekActionLabel(null)).toBe('Start this week')
        expect(thisWeekActionLabel(undefined)).toBe('Start this week')
    })

    it('continues a week the employee can still edit, a rejected one included', () => {
        expect(thisWeekActionLabel('Draft')).toBe('Continue this week')
        expect(thisWeekActionLabel('Rejected')).toBe('Continue this week')
    })

    it('views a week that is with the approver or done', () => {
        expect(thisWeekActionLabel('Submitted')).toBe('View this week')
        expect(thisWeekActionLabel('Resubmitted')).toBe('View this week')
        expect(thisWeekActionLabel('Approved')).toBe('View this week')
    })
})
```

- [ ] **Step 2: Run it to verify it fails**

```bash
node_modules/.bin/vitest run src/lib/timesheet-week.test.ts
```

Expected: FAIL — the module does not exist.

- [ ] **Step 3: Implement the helper and use it**

Create `client/src/lib/timesheet-week.ts`:

```ts
import type { TimesheetStatus } from './types/timesheet'

/**
 * The My Timesheets button that opens the editor on the current week, worded for
 * what the editor will let them do there: start a week with no sheet, continue
 * one they can still edit (a rejected one is theirs to fix), or view one that is
 * with the approver or done. The same editability the editor uses (`isEditable`
 * in NewTimesheetPage: Draft, Rejected, or no sheet).
 */
export function thisWeekActionLabel(status: TimesheetStatus | null | undefined): 'Start this week' | 'Continue this week' | 'View this week' {
    if (!status) return 'Start this week'
    return status === 'Draft' || status === 'Rejected' ? 'Continue this week' : 'View this week'
}
```

In `MyTimesheetPage.tsx`, import it and change the button's text from `📝 Open this week` to:

```tsx
                    📝 {thisWeekActionLabel(currentTimesheet?.status)}
```

Update the comment above that block, `{/* Tabs + Open this week */}`, to `{/* Tabs + the current week's button */}`.

- [ ] **Step 4: Run it to verify it passes**

```bash
node_modules/.bin/vitest run src/lib/timesheet-week.test.ts src/components/timesheet
```

Expected: PASS.

- [ ] **Step 5: CLAUDE.md**

In the Domain Model table's `Timesheet` row, append before the closing `|`:

```
 **One sidebar entry** — Employees and Managers have a single **Timesheets** item (`/timesheets`, My Timesheets), lit on the editor (`/new-timesheet`) too, and the topbar titles both "Timesheets"; "Submit Timesheet" is gone from the menu, not from the app — the route, `navigateToNewTimesheet(weekStart?)` and every shortcut to it stay. The page's week button is worded by `thisWeekActionLabel` (`lib/timesheet-week.ts`): Start / Continue (Draft or Rejected) / View this week. `Sidebar.test.tsx` and `Topbar.test.tsx` pin it.
```

- [ ] **Step 6: Full client run, lint, build**

From `client/`, one directory per process (a single run exhausts memory and still exits 0):

```bash
for d in src/components/admin src/components/annual-leave src/components/attendance src/components/auth src/components/layout src/components/tasks src/components/timesheet src/lib; do node_modules/.bin/vitest run $d || echo "FAILED: $d"; done
npm run lint
npm run build
```

Expected: no `FAILED:` line (a timeout in `AdminUsersPanel.test.tsx` under batch load is known — rerun that file alone to confirm); lint 0 errors; build exit 0.

- [ ] **Step 7: Commit**

```bash
git add client/src/lib/timesheet-week.ts client/src/lib/timesheet-week.test.ts client/src/components/timesheet/MyTimesheetPage.tsx CLAUDE.md
git commit -m "My Timesheets: the week button says Start, Continue or View; CLAUDE.md records the one entry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage.** 4.1 → Task 1 (both roles, renamed entry, `/timesheets` target, HR unchanged). 4.2 → Task 3 (status-following label on the existing button; the button already existed, so none is added). "The `/new-timesheet` route stays" → Global Constraints; nothing touches it. Spec testing: Sidebar lists Timesheets once and no Submit Timesheet for Employee and Manager (Task 1); the button label follows the week's status (Task 3, as a helper test rather than a page test — the page has no test file today and needs half a dozen API mocks to render; the wiring is one line).
- **Beyond the spec, deliberately.** The topbar title change (Task 2) and `aria-current` on sidebar items (Task 1): without the first, the menu says "Timesheets" and the page says "Submit Timesheet"; without the second, "highlighted in the editor" cannot be tested and is invisible to a screen reader.
- **Type consistency.** `thisWeekActionLabel` and `renderSidebarAs(user, path)` / `renderTopbarAs(user, path)` are used as defined.
- **Review Focus.** 1, 2 → Task 1's `highlights Timesheets on %s` and `does not highlight … Approvals`. 3 → Task 1's HR test. 4, 5 → Task 3's helper tests.
