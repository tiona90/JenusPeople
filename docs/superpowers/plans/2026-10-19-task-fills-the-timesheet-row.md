# The Task Fills the Timesheet Row — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the timesheet editor, the Type, Component and Activity columns appear only when their catalogue leaves something to choose, a project that leaves exactly one option has it filled in and locked, and a stored value outside the options still reads — so in the common case a row is Task, Project, Hours and a note.

**Architecture:** One new pure module, `client/src/lib/timesheet-columns.ts`, holds the rules (`narrowingColumns`, `narrowingOptions`, `fillSingleOptions`, `isLockedToSingle`, `retainedOption`) on top of the existing `typeOptionsFrom` / `componentOptionsFor` / `activityOptionsFor` helpers, with unit tests beside the other `lib/*.test.ts`. `NewTimesheetPage.tsx` renders them: the grid and header strip become functions of which columns are shown, `updateTask` fills blanks after its retention step, and the pickers lock or list a retained item. A new `NewTimesheetPage.test.tsx` drives the real editor through MUI selects. Client only; no API change.

**Tech Stack:** React 19, TypeScript, MUI 7 (`Select` renders a `role="combobox"` div; open it with `fireEvent.mouseDown`, pick with `fireEvent.click` on a `role="option"`), TanStack Query, Vitest + Testing Library. Run tests with `node_modules/.bin/vitest` from `client/` (never bare `npx vitest`).

**Spec:** `docs/superpowers/specs/2026-10-19-simpler-tasks-and-timesheets-design.md`, section "Phase 2 — The task fills the timesheet row".

## Global Constraints

- Client only. No API, DTO or server rule changes. `TimesheetEntryValidator` on the server stays the authority; the client may under-refuse, never over-refuse.
- "Shown" is decided **once per sheet** from the active catalogues (two or more entries), never per row or per day, so the header strip and every row share one grid.
- The fill rule **fills blanks only** and runs **after** the existing retention step (`retainedProjectId` / `retainedActivityId` / `retainedComponentId` / `retainedWorkTaskId`) in `updateTask`. It never overwrites a value already on the row and never runs on rows loaded from the server.
- "The options a project leaves": Type = `typeOptionsFrom([project], activeProjectTypes)` (an unclassified project leaves **zero** types, so nothing is filled — picking a type would clear the project through `retainedProjectId`, as today); Component = `componentOptionsFor(project, activeComponents)`; Activity = `activityOptionsFor(project, activeActivityTypes)` (both fall back to the whole catalogue for a project that declared none, as today). With no project chosen, Type lists every carried type, as today.
- User-facing copy: the editor's rows are **entries**, not tasks: "+ Add entry", "Please fix the highlighted entries before saving.", "Add at least one entry before submitting.", "1 entry" / "N entries", "Add entries as you go", "Enter hours for this entry.", "Select a project for this entry." Internal names (`Task`, `onAddTask`, `TASK_FIELD_SX`) may stay.
- Timesheet dates compare on the first ten characters (`isoDateOnly`); tests build dates from local calendar parts, never `toISOString()`.
- Commit after every task, message in the repository's style, ending with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

Inputs the spec implies but does not spell out, each pinned by a test in the task that owns the code:

1. **A row loaded from the server with a stored type the project no longer leaves** (Apollo carries only Support, the entry says Issue) must show "Issue" and stay editable, not lock onto Support or blank out. (Task 3)
2. **Switching project after a fill**: Apollo filled DM; moving to Borealis (declares none, so both components are offered) must keep DM and unlock the picker; moving on to Cassini (declares jDocs only) must clear DM and fill jDocs. Retention first, then fill. (Task 3)
3. **A hidden column whose catalogue has exactly one entry** must still write that entry's id on save, so the server's stored row says what the organisation's one component is. (Task 3)
4. **Picking a Task** must fill the project *and then* the single options that project leaves, in one change. (Task 3)
5. **Empty catalogues** must leave the header strip and every row with the same columns (Task, Project, Hours, remove) — no header over a missing picker, no picker under a missing header. (Task 2)

---

### Task 1: The rules in `lib/timesheet-columns.ts`

**Files:**
- Create: `client/src/lib/timesheet-columns.ts`
- Test: `client/src/lib/timesheet-columns.test.ts`

**Interfaces:**
- Consumes: `typeOptionsFrom` (`./project-types`), `componentOptionsFor` (`./project-components`), `activityOptionsFor` (`./project-activities`); types `Project`, `ProjectActivityType`, `ProjectComponent`, `ProjectType` from `./types`.
- Produces (all exported, used verbatim by Tasks 2 and 3):
  - `interface NarrowingCatalogues { types: ProjectType[]; components: ProjectComponent[]; activities: ProjectActivityType[] }`
  - `interface NarrowingColumns { type: boolean; component: boolean; activity: boolean }`
  - `narrowingColumns(c: NarrowingCatalogues): NarrowingColumns`
  - `narrowingOptions(project: Project | undefined, c: NarrowingCatalogues): { types: ProjectType[]; components: ProjectComponent[]; activities: ProjectActivityType[] }`
  - `fillSingleOptions<T extends NarrowingValues>(row: T, project: Project | undefined, c: NarrowingCatalogues): T` where `interface NarrowingValues { projectTypeId: string; projectComponentId: string; activityTypeId: string }`
  - `isLockedToSingle(options: readonly { id: number }[], value: string): boolean`
  - `retainedOption<T extends { id: number }>(options: readonly T[], value: string, catalogue: readonly T[]): T | null`

- [ ] **Step 1: Write the failing tests**

Create `client/src/lib/timesheet-columns.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { fillSingleOptions, isLockedToSingle, narrowingColumns, narrowingOptions, retainedOption } from './timesheet-columns'
import type { Project, ProjectActivityType, ProjectComponent, ProjectType } from './types'

const type = (id: number, name: string): ProjectType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const component = (id: number, name: string): ProjectComponent => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const activity = (id: number, name: string): ProjectActivityType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 } as ProjectActivityType)
const summary = (x: { id: number; name: string }) => ({ id: x.id, name: x.name, icon: '', colorKey: 'default' })

const SUPPORT = type(1, 'Support')
const ISSUE = type(2, 'Issue')
const DM = component(1, 'DM')
const JDOCS = component(2, 'jDocs')
const DEV = activity(1, 'Development')
const REVIEW = activity(2, 'Review')

const ALL = { types: [SUPPORT, ISSUE], components: [DM, JDOCS], activities: [DEV, REVIEW] }

function project(over: Partial<Project>): Project {
    return { id: 10, name: 'Apollo', types: [], components: [], activities: [], ...over } as Project
}
const APOLLO = project({ types: [summary(SUPPORT)], components: [summary(DM)], activities: [summary(DEV), summary(REVIEW)] })
const UNCLASSIFIED = project({ id: 11, name: 'Borealis' })

const blank = { projectTypeId: '', projectComponentId: '', activityTypeId: '' }

describe('narrowingColumns', () => {
    it('shows a column only when its catalogue has two or more entries', () => {
        expect(narrowingColumns(ALL)).toEqual({ type: true, component: true, activity: true })
        expect(narrowingColumns({ types: [SUPPORT], components: [], activities: [DEV, REVIEW] })).toEqual({ type: false, component: false, activity: true })
    })
})

describe('narrowingOptions', () => {
    it('narrows each column to what the project leaves', () => {
        const o = narrowingOptions(APOLLO, ALL)
        expect(o.types.map((t) => t.name)).toEqual(['Support'])
        expect(o.components.map((c) => c.name)).toEqual(['DM'])
        expect(o.activities.map((a) => a.name)).toEqual(['Development', 'Review'])
    })

    it('leaves no type for an unclassified project, and the whole catalogue of components and activities', () => {
        const o = narrowingOptions(UNCLASSIFIED, ALL)
        expect(o.types).toEqual([])
        expect(o.components).toEqual(ALL.components)
        expect(o.activities).toEqual(ALL.activities)
    })

    it('lists every carried type when no project is chosen yet', () => {
        expect(narrowingOptions(undefined, ALL).types).toEqual(ALL.types)
    })
})

describe('fillSingleOptions', () => {
    it('fills the blanks the project leaves exactly one answer for', () => {
        expect(fillSingleOptions(blank, APOLLO, ALL)).toEqual({ projectTypeId: '1', projectComponentId: '1', activityTypeId: '' })
    })

    it('never overwrites a value already on the row', () => {
        expect(fillSingleOptions({ ...blank, projectComponentId: '2' }, APOLLO, ALL).projectComponentId).toBe('2')
    })

    it('fills a single-entry catalogue even when the project declared none', () => {
        const one = { ...ALL, components: [DM] }
        expect(fillSingleOptions(blank, UNCLASSIFIED, one).projectComponentId).toBe('1')
    })

    it('fills nothing without a project, and no type for an unclassified one', () => {
        expect(fillSingleOptions(blank, undefined, { ...ALL, components: [DM] })).toEqual(blank)
        expect(fillSingleOptions(blank, UNCLASSIFIED, ALL).projectTypeId).toBe('')
    })

    it('keeps every other field on the row', () => {
        const row = { ...blank, _id: 'r1', projectId: '10', hours: '2', notes: 'n' }
        expect(fillSingleOptions(row, APOLLO, ALL)).toEqual({ ...row, projectTypeId: '1', projectComponentId: '1' })
    })
})

describe('isLockedToSingle', () => {
    it('locks only when one option is left and the row holds it', () => {
        expect(isLockedToSingle([DM], '1')).toBe(true)
        expect(isLockedToSingle([DM], '')).toBe(false)
        expect(isLockedToSingle([DM], '2')).toBe(false)
        expect(isLockedToSingle([DM, JDOCS], '1')).toBe(false)
    })
})

describe('retainedOption', () => {
    it('names a stored value the project no longer offers, from the full catalogue', () => {
        expect(retainedOption([DM], '2', [DM, JDOCS])).toEqual(JDOCS)
    })

    it('returns nothing for an empty value, an offered value, or one the catalogue has lost', () => {
        expect(retainedOption([DM], '', [DM, JDOCS])).toBeNull()
        expect(retainedOption([DM], '1', [DM, JDOCS])).toBeNull()
        expect(retainedOption([DM], '9', [DM, JDOCS])).toBeNull()
    })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

From `client/`:

```bash
node_modules/.bin/vitest run src/lib/timesheet-columns.test.ts
```

Expected: FAIL — the module `./timesheet-columns` does not exist.

- [ ] **Step 3: Implement**

Create `client/src/lib/timesheet-columns.ts`:

```ts
import { activityOptionsFor } from './project-activities'
import { componentOptionsFor } from './project-components'
import { typeOptionsFrom } from './project-types'
import type { Project, ProjectActivityType, ProjectComponent, ProjectType } from './types'

/**
 * Which of the timesheet editor's three narrowing columns — Type, Component,
 * Activity — are worth asking about, and what a project leaves to choose in each.
 *
 * The editor used to ask all three on every row, so an organisation that never
 * set up components saw an empty "Component…" dropdown on every entry, and a
 * project made up of one component still asked which. A column is shown only
 * when its catalogue leaves something to choose, and a single answer is filled
 * in, not asked. The server's rules are unchanged; this only decides what the
 * form puts in front of somebody.
 */
export interface NarrowingCatalogues {
    /** The active types some visible project carries (`typeOptionsFrom`). */
    types: ProjectType[]
    /** The active components. */
    components: ProjectComponent[]
    /** The active activity types. */
    activities: ProjectActivityType[]
}

export interface NarrowingColumns {
    type: boolean
    component: boolean
    activity: boolean
}

/** The three narrowing fields of a row, as the editor stores them: ids as strings, '' for none. */
export interface NarrowingValues {
    projectTypeId: string
    projectComponentId: string
    activityTypeId: string
}

/**
 * A column is shown only when its catalogue has two or more active entries. One
 * entry leaves nothing to choose (and is filled in silently, see
 * {@link fillSingleOptions}); none leaves nothing to say. Decided once per sheet,
 * so the header strip and every row share one grid.
 */
export function narrowingColumns(c: NarrowingCatalogues): NarrowingColumns {
    return {
        type: c.types.length >= 2,
        component: c.components.length >= 2,
        activity: c.activities.length >= 2,
    }
}

/**
 * What a project leaves to choose in each column. Type follows `typeOptionsFrom`
 * — an unclassified project carries no type, so it leaves none (picking one would
 * clear the project, as `retainedProjectId` does today); with no project chosen
 * yet, every carried type is on offer. Component and Activity follow their own
 * helpers, which fall back to the whole catalogue for a project that declared none.
 */
export function narrowingOptions(project: Project | undefined, c: NarrowingCatalogues) {
    return {
        types: project ? typeOptionsFrom([project], c.types) : c.types,
        components: componentOptionsFor(project, c.components),
        activities: activityOptionsFor(project, c.activities),
    }
}

function singleId(options: readonly { id: number }[]): string | null {
    return options.length === 1 ? String(options[0].id) : null
}

/**
 * Fills the blanks the project leaves exactly one answer for. Blanks only: a
 * value already on the row is kept whatever the options say — the row's own
 * retention step decides what survives a project change, and this runs after it.
 * No project, nothing to fill.
 */
export function fillSingleOptions<T extends NarrowingValues>(row: T, project: Project | undefined, c: NarrowingCatalogues): T {
    if (!project) return row
    const o = narrowingOptions(project, c)
    return {
        ...row,
        projectTypeId: row.projectTypeId || (singleId(o.types) ?? ''),
        projectComponentId: row.projectComponentId || (singleId(o.components) ?? ''),
        activityTypeId: row.activityTypeId || (singleId(o.activities) ?? ''),
    }
}

/**
 * A picker is locked when the project leaves one option and the row holds it:
 * there is nothing to choose, and the value is on show so the user sees what will
 * be stored. A row holding something else — a loaded row from before the project
 * was narrowed — stays editable.
 */
export function isLockedToSingle(options: readonly { id: number }[], value: string): boolean {
    const single = singleId(options)
    return single != null && single === value
}

/**
 * The catalogue entry for a stored value the project no longer offers, so a
 * loaded row still reads what it says instead of a blank select. Null when the
 * value is empty, is among the options already, or the catalogue has lost it.
 */
export function retainedOption<T extends { id: number }>(options: readonly T[], value: string, catalogue: readonly T[]): T | null {
    if (!value) return null
    if (options.some((o) => String(o.id) === value)) return null
    return catalogue.find((o) => String(o.id) === value) ?? null
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/lib/timesheet-columns.test.ts
```

Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add client/src/lib/timesheet-columns.ts client/src/lib/timesheet-columns.test.ts
git commit -m "Timesheet editor: the rules for which narrowing columns to ask and what a project fills in, as pure helpers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Columns follow catalogue use

**Files:**
- Modify: `client/src/components/timesheet/NewTimesheetPage.tsx` (`TASK_GRID`/`TASK_HEADERS` ~lines 68–85; the catalogue queries ~lines 297–312; the `DayCard` call ~lines 826–862; `DayCardProps` ~lines 909–935; `DayCard` ~lines 989–1000 and the row render ~lines 1090–1225)
- Create: `client/src/components/timesheet/NewTimesheetPage.test.tsx`

**Interfaces:**
- Consumes: `narrowingColumns`, `NarrowingCatalogues`, `NarrowingColumns` from Task 1.
- Produces: `DayCardProps.shown: NarrowingColumns` and `DayCardProps.catalogues: NarrowingCatalogues` (replacing `activeActivityTypes`, `activeProjectTypes`, `activeComponents`); page-level `catalogues` memo and `shown` memo that Task 3 reuses; every picker carries an `aria-label` (`Task`, `Project`, `Type`, `Component`, `Activity`) so tests and screen readers can name them; functions `taskGrid(shown)` and `taskHeaders(shown)`.

- [ ] **Step 1: Write the failing tests**

Create `client/src/components/timesheet/NewTimesheetPage.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Project, ProjectActivityType, ProjectComponent, ProjectType, TimesheetTaskOption, UserInfo } from '../../lib/types'
import type { Timesheet } from '../../lib/types/timesheet'
import type { TimesheetEntry } from '../../lib/types/timesheet-entry'
import NewTimesheetPage from './NewTimesheetPage'

vi.mock('../../lib/api')
// The attendance hooks import their API module directly, not through the index.
vi.mock('../../lib/api/attendance')
vi.mock('../../lib/mobx')

const api = vi.mocked(await import('../../lib/api'))
const attendanceApi = vi.mocked(await import('../../lib/api/attendance'))
const mobx = vi.mocked(await import('../../lib/mobx'))

/* ─── catalogues ─────────────────────────────────────────────────────────── */

const type = (id: number, name: string): ProjectType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const component = (id: number, name: string): ProjectComponent => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 })
const activity = (id: number, name: string): ProjectActivityType => ({ id, name, description: '', icon: '', colorKey: 'default', isActive: true, usedInProjects: 0 } as ProjectActivityType)
const summary = (x: { id: number; name: string }) => ({ id: x.id, name: x.name, icon: '', colorKey: 'default' })

const SUPPORT = type(1, 'Support')
const ISSUE = type(2, 'Issue')
const DM = component(1, 'DM')
const JDOCS = component(2, 'jDocs')
const DEV = activity(1, 'Development')
const REVIEW = activity(2, 'Review')

function project(id: number, name: string, over: Partial<Project> = {}): Project {
    return { id, name, code: name.slice(0, 3).toUpperCase(), isActive: true, types: [], components: [], activities: [], ...over } as Project
}
// Apollo leaves one type and one component, two activities.
const APOLLO = project(10, 'Apollo', { types: [summary(SUPPORT)], components: [summary(DM)], activities: [summary(DEV), summary(REVIEW)] })
// Borealis carries both types and declares no component or activity: the whole catalogue of each.
const BOREALIS = project(11, 'Borealis', { types: [summary(SUPPORT), summary(ISSUE)] })
// Cassini is unclassified and made of jDocs alone.
const CASSINI = project(12, 'Cassini', { components: [summary(JDOCS)] })

/* ─── dates: local calendar parts, never toISOString ─────────────────────── */

function iso(d: Date) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function thisWeek() {
    const today = new Date()
    today.setHours(0, 0, 0, 0)
    const monday = new Date(today)
    const dow = monday.getDay()
    monday.setDate(monday.getDate() + (dow === 0 ? -6 : 1 - dow))
    const sunday = new Date(monday)
    sunday.setDate(sunday.getDate() + 6)
    return { today: iso(today), monday: iso(monday), sunday: iso(sunday) }
}

/** A Draft for this week with one entry on today's date, so today's open card shows that row. */
function sheetWith(entry: Partial<TimesheetEntry>): Timesheet {
    const { today, monday, sunday } = thisWeek()
    const row: TimesheetEntry = {
        id: 'e1', timesheetId: 'ts-1', projectId: 10, date: `${today}T00:00:00`, hoursWorked: 2, notes: null,
        projectTypeId: null, projectComponentId: null, activityTypeId: null, workTaskId: null, ...entry,
    }
    return {
        id: 'ts-1', employeeId: 'p1', employeeName: 'Me', departmentId: 1, status: 'Draft',
        periodStart: `${monday}T00:00:00`, periodEnd: `${sunday}T00:00:00`, totalHours: 2,
        submittedAt: null, approvedAt: null, createdAt: `${monday}T08:00:00`, projectSummaries: [], dailyHours: [], entries: [row],
    } as Timesheet
}

/* ─── render ─────────────────────────────────────────────────────────────── */

interface Setup {
    projects?: Project[]
    types?: ProjectType[]
    components?: ProjectComponent[]
    activities?: ProjectActivityType[]
    taskOptions?: TimesheetTaskOption[]
    sheet?: Timesheet
}

async function renderPage(s: Setup = {}) {
    mobx.useStore.mockReturnValue({ uiStore: { consumePendingWeekStart: () => null, navigateToTimesheets: vi.fn() } } as never)
    api.getProjects.mockResolvedValue(s.projects ?? [APOLLO, BOREALIS, CASSINI])
    api.getProjectTypes.mockResolvedValue(s.types ?? [SUPPORT, ISSUE])
    api.getProjectComponents.mockResolvedValue(s.components ?? [DM, JDOCS])
    api.getProjectActivityTypes.mockResolvedValue(s.activities ?? [DEV, REVIEW])
    api.getTimesheetTaskOptions.mockResolvedValue(s.taskOptions ?? [])
    api.getMyTimesheets.mockResolvedValue(s.sheet ? [s.sheet] : [])
    if (s.sheet) api.getTimesheet.mockResolvedValue(s.sheet)
    api.createTimesheet.mockResolvedValue({ id: 'ts-new' } as Timesheet)
    api.createTimesheetEntry.mockResolvedValue({} as TimesheetEntry)
    attendanceApi.getAttendanceToday.mockResolvedValue({ status: 'out' } as never)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={queryClient}>
            <NewTimesheetPage user={{ id: 'u1', roles: ['Employee'] } as UserInfo} />
        </QueryClientProvider>,
    )
    // Today's card opens by itself; its one row is the row under test.
    await screen.findByRole('combobox', { name: 'Project' })
}

const combo = (name: string) => screen.getByRole('combobox', { name })

async function pick(name: string, option: string) {
    fireEvent.mouseDown(combo(name))
    fireEvent.click(await screen.findByRole('option', { name: option }))
}

/** The header strip of the one open day card: the column captions in order. */
function headerLabels() {
    const row = combo('Project').closest('[data-testid="entry-row"]')!
    const strip = row.parentElement!.previousElementSibling!
    return Array.from(strip.children).map((el) => el.textContent)
}

beforeEach(() => {
    vi.clearAllMocks()
})

describe('NewTimesheetPage — which columns are asked', () => {
    it('asks Type, Component and Activity when each catalogue has two or more entries', async () => {
        await renderPage()
        expect(combo('Type')).toBeInTheDocument()
        expect(combo('Component')).toBeInTheDocument()
        expect(combo('Activity')).toBeInTheDocument()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Component', 'Activity', 'Hours', ''])
    })

    it('drops a column whose catalogue is empty, header and picker together', async () => {
        await renderPage({ components: [], activities: [] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        expect(screen.queryByRole('combobox', { name: 'Activity' })).toBeNull()
        expect(combo('Type')).toBeInTheDocument()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Hours', ''])
    })

    it('drops a column whose catalogue has one entry — there is nothing to choose', async () => {
        await renderPage({ components: [DM] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        expect(headerLabels()).toEqual(['Task', 'Project', 'Type', 'Activity', 'Hours', ''])
    })

    it('leaves a row of Task, Project and Hours when no catalogue is in use', async () => {
        await renderPage({ types: [], components: [], activities: [] })
        expect(screen.getAllByRole('combobox').map((c) => c.getAttribute('aria-label'))).toEqual(['Task', 'Project'])
        expect(headerLabels()).toEqual(['Task', 'Project', 'Hours', ''])
    })
})
```

Note the `data-testid="entry-row"` the header helper relies on: Step 3 adds it to each row's card `<Box>`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx
```

Expected: FAIL — `getByRole('combobox', { name: 'Project' })` finds nothing (only the Task select has an `aria-label` today), so `renderPage` times out.

- [ ] **Step 3: Implement**

In `client/src/components/timesheet/NewTimesheetPage.tsx`:

1. Import the rules:

```ts
import { narrowingColumns, type NarrowingCatalogues, type NarrowingColumns } from '../../lib/timesheet-columns'
```

2. Replace the `TASK_GRID` and `TASK_HEADERS` constants (keep their comment about `minmax(0, …)`) with functions of the shown columns:

```ts
function taskGrid(shown: NarrowingColumns): string {
    return [
        'minmax(0, 1.6fr)', // Task
        'minmax(0, 1.6fr)', // Project
        ...(shown.type ? ['minmax(0, 1fr)'] : []),
        ...(shown.component ? ['minmax(0, 1fr)'] : []),
        ...(shown.activity ? ['minmax(0, 1fr)'] : []),
        '72px',             // Hours
        '42px',             // Remove
    ].join(' ')
}
function taskHeaders(shown: NarrowingColumns): { label: string; align?: 'center' }[] {
    return [
        { label: 'Task' },
        { label: 'Project' },
        ...(shown.type ? [{ label: 'Type' }] : []),
        ...(shown.component ? [{ label: 'Component' }] : []),
        ...(shown.activity ? [{ label: 'Activity' }] : []),
        { label: 'Hours', align: 'center' as const },
        { label: '' },
    ]
}
```

3. In the page component, after `activeProjectTypes` is computed, add:

```ts
    // The three narrowing columns: shown only when there is something to choose.
    const catalogues: NarrowingCatalogues = useMemo(
        () => ({ types: activeProjectTypes, components: activeComponents, activities: activeActivityTypes }),
        [activeProjectTypes, activeComponents, activeActivityTypes],
    )
    const shown = useMemo(() => narrowingColumns(catalogues), [catalogues])
```

`activeComponents` and `activeActivityTypes` are plain `.filter(...)` results recomputed each render, so the memo re-runs each render too; that is fine — `narrowingColumns` is three length checks. (If you would rather, wrap those two filters in `useMemo` as well; not required.)

4. In the `DayCard` call, replace the three props `activeActivityTypes={…} activeProjectTypes={…} activeComponents={…}` with:

```tsx
                                catalogues={catalogues}
                                shown={shown}
```

5. In `DayCardProps`, replace

```ts
    activeActivityTypes: ProjectActivityType[]
    activeProjectTypes: ProjectType[]
    activeComponents: ProjectComponent[]
```

with

```ts
    catalogues: NarrowingCatalogues
    shown: NarrowingColumns
```

and the matching names in `DayCard`'s destructuring. Remove the now-unused `ProjectActivityType`, `ProjectType`, `ProjectComponent` type imports if nothing else in the file uses them (lint will say).

6. In `DayCard`, at the top of the body:

```ts
    const grid = taskGrid(shown)
    const headers = taskHeaders(shown)
```

Replace `gridTemplateColumns: TASK_GRID` (two places: the header strip and the row) with `gridTemplateColumns: grid`, and `TASK_HEADERS.map(` with `headers.map(`.

7. Per row, add `data-testid="entry-row"` to the row's card `<Box key={t._id} sx={{ p: TASK_CARD_PAD, …` and compute the project once above the selects (Task 3 reuses it):

```tsx
                        {tasks.map((t) => {
                            const err = taskErrors[t._id]
                            const project = activeProjects.find((p) => String(p.id) === t.projectId)
                            return (
                            <Box key={t._id} data-testid="entry-row" sx={{ …
```

8. Give every picker an `aria-label` and gate the three narrowing ones on `shown`:

- Project `<Select>`: add `inputProps={{ 'aria-label': 'Project' }}`.
- Type `<Select>`: wrap in `{shown.type && ( … )}`, add `inputProps={{ 'aria-label': 'Type' }}`.
- Component `<Select>`: wrap in `{shown.component && ( … )}`, add `inputProps={{ 'aria-label': 'Component' }}`, and replace `componentOptionsFor(activeProjects.find(…), activeComponents)` with `componentOptionsFor(project, catalogues.components)`.
- Activity `<Select>`: wrap in `{shown.activity && ( … )}`, add `inputProps={{ 'aria-label': 'Activity' }}`, and replace `activityOptionsFor(activeProjects.find(…), activeActivityTypes)` with `activityOptionsFor(project, catalogues.activities)`.
- Type `<Select>`'s options: `activeProjectTypes.map(` becomes `catalogues.types.map(` (Task 3 narrows this further).

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Lint and the rest of the timesheet tests**

```bash
node_modules/.bin/vitest run src/components/timesheet
npm run lint
```

Expected: every test PASS; lint 0 errors (5 pre-existing warnings live in `TaskSettingsPanel.tsx` and `TaskDialog.tsx`, none in this file).

- [ ] **Step 6: Commit**

```bash
git add client/src/components/timesheet/NewTimesheetPage.tsx client/src/components/timesheet/NewTimesheetPage.test.tsx
git commit -m "Timesheet editor: ask Type, Component and Activity only when the catalogue leaves something to choose

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: A single option is filled in, not asked

**Files:**
- Modify: `client/src/components/timesheet/NewTimesheetPage.tsx` (`updateTask` ~lines 436–480; the `DayCard` call; `DayCardProps`; the three narrowing `<Select>`s in `DayCard`)
- Test: `client/src/components/timesheet/NewTimesheetPage.test.tsx`

**Interfaces:**
- Consumes: `fillSingleOptions`, `isLockedToSingle`, `narrowingOptions`, `retainedOption` from Task 1; `catalogues`, `shown`, `project` (per row) and the `aria-label`s from Task 2.
- Produces: `DayCardProps.retainFrom: NarrowingCatalogues` — the **unfiltered** catalogues (`projectTypes`, `components`, `activityTypes` query data, inactive included), used only to name a stored value the options no longer offer.

- [ ] **Step 1: Write the failing tests**

Append to `client/src/components/timesheet/NewTimesheetPage.test.tsx`:

```tsx
describe('NewTimesheetPage — a single option is filled in', () => {
    it('fills and locks what the project leaves one answer for, and leaves the rest to choose', async () => {
        await renderPage()
        await pick('Project', 'Apollo')

        expect(combo('Type')).toHaveTextContent('Support')
        expect(combo('Type')).toHaveAttribute('aria-disabled', 'true')
        expect(combo('Component')).toHaveTextContent('DM')
        expect(combo('Component')).toHaveAttribute('aria-disabled', 'true')
        // Two activities: still a choice.
        expect(combo('Activity')).not.toHaveAttribute('aria-disabled', 'true')
        expect(combo('Activity')).toHaveTextContent('Activity…')
    })

    it('saves what it filled in', async () => {
        await renderPage()
        await pick('Project', 'Apollo')
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '2' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

        await waitFor(() => expect(api.createTimesheetEntry).toHaveBeenCalledTimes(1))
        expect(api.createTimesheetEntry.mock.calls[0][1]).toEqual(expect.objectContaining({
            projectId: 10, projectTypeId: 1, projectComponentId: 1, activityTypeId: null, hoursWorked: 2,
        }))
    })

    it('leaves a project with several options to choose, unlocked and blank', async () => {
        await renderPage()
        await pick('Project', 'Borealis')
        expect(combo('Type')).not.toHaveAttribute('aria-disabled', 'true')
        expect(combo('Type')).toHaveTextContent('Any type…')
        expect(combo('Component')).not.toHaveAttribute('aria-disabled', 'true')
    })

    it('writes a hidden single-entry catalogue silently', async () => {
        await renderPage({ components: [DM] })
        expect(screen.queryByRole('combobox', { name: 'Component' })).toBeNull()
        await pick('Project', 'Borealis')
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '1' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

        await waitFor(() => expect(api.createTimesheetEntry).toHaveBeenCalledTimes(1))
        expect(api.createTimesheetEntry.mock.calls[0][1]).toEqual(expect.objectContaining({ projectId: 11, projectComponentId: 1 }))
    })

    it('fills in after a task sets the project', async () => {
        await renderPage({ taskOptions: [{ id: 7, title: 'Fix login', projectId: 10, projectCode: 'APO', targetHours: null, loggedHours: 0, isClosed: false }] })
        await pick('Task', /Fix login/)
        expect(combo('Project')).toHaveTextContent('Apollo')
        expect(combo('Type')).toHaveTextContent('Support')
        expect(combo('Type')).toHaveAttribute('aria-disabled', 'true')
        expect(combo('Component')).toHaveTextContent('DM')
    })

    it('keeps a filled value the next project still offers, and refills when it does not', async () => {
        await renderPage()
        await pick('Project', 'Apollo')
        expect(combo('Component')).toHaveTextContent('DM')

        // Borealis declares none, so both components are offered: DM stays, unlocked.
        await pick('Project', 'Borealis')
        expect(combo('Component')).toHaveTextContent('DM')
        expect(combo('Component')).not.toHaveAttribute('aria-disabled', 'true')

        // Cassini is made of jDocs alone: DM goes, jDocs is filled in and locked.
        await pick('Project', 'Cassini')
        expect(combo('Component')).toHaveTextContent('jDocs')
        expect(combo('Component')).toHaveAttribute('aria-disabled', 'true')
    })

    it('never overwrites a choice already made', async () => {
        await renderPage()
        await pick('Project', 'Borealis')
        await pick('Component', 'jDocs')
        // Borealis → Apollo: Apollo offers DM only, so jDocs is dropped by retention and DM filled — that is the
        // retention rule, not an overwrite. Pick Borealis again: DM survives (offered) and is not replaced.
        await pick('Project', 'Apollo')
        expect(combo('Component')).toHaveTextContent('DM')
        await pick('Project', 'Borealis')
        expect(combo('Component')).toHaveTextContent('DM')
    })
})

describe('NewTimesheetPage — a stored value the project no longer offers', () => {
    it('still reads, and the picker stays editable rather than locking onto the one option', async () => {
        // Apollo carries Support only; this row was saved as Issue.
        await renderPage({ sheet: sheetWith({ projectTypeId: 2 }) })
        expect(combo('Type')).toHaveTextContent('Issue')
        expect(combo('Type')).not.toHaveAttribute('aria-disabled', 'true')
    })

    it('names a stored component the project does not declare', async () => {
        // Apollo is made of DM; this row was saved against jDocs.
        await renderPage({ sheet: sheetWith({ projectComponentId: 2 }) })
        expect(combo('Component')).toHaveTextContent('jDocs')
    })

    it('does not fill a loaded row — blanks stay as they were saved', async () => {
        await renderPage({ sheet: sheetWith({}) })
        expect(combo('Type')).toHaveTextContent('Any type…')
        expect(combo('Component')).toHaveTextContent('Component…')
    })
})
```

The Task option's label is built by `taskOptionLabel` (the title, with a "(closed)" or "(no longer yours)" suffix where that applies), so the test matches it with a regex rather than the exact string.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx
```

Expected: the four Task 2 tests still PASS; the new ones FAIL — nothing is filled after picking Apollo (`Type` reads "Any type…"), nothing is `aria-disabled`, the loaded "Issue" row renders blank (MUI warns about an out-of-range value).

- [ ] **Step 3: Implement**

In `client/src/components/timesheet/NewTimesheetPage.tsx`:

1. Extend the import from Task 2:

```ts
import {
    fillSingleOptions, isLockedToSingle, narrowingColumns, narrowingOptions, retainedOption,
    type NarrowingCatalogues, type NarrowingColumns,
} from '../../lib/timesheet-columns'
```

2. In `updateTask`, inside the block `if (field === 'projectId' || field === 'projectTypeId' || field === 'workTaskId') { … }`, after the three retention lines, return the filled row:

```ts
                    if (field === 'projectId' || field === 'projectTypeId' || field === 'workTaskId') {
                        const project = activeProjects.find((p) => String(p.id) === next.projectId)
                        next.activityTypeId = retainedActivityId(next.activityTypeId, project, activeActivityTypes)
                        next.projectComponentId = retainedComponentId(next.projectComponentId, project, activeComponents)
                        next.workTaskId = retainedWorkTaskId(next.workTaskId, next.projectId, taskOptions)
                        // What the project leaves exactly one answer for is filled in, not asked —
                        // blanks only, after retention has decided what survives the change.
                        return fillSingleOptions(next, project, catalogues)
                    }
                    return next
```

3. Add the unfiltered catalogues for retained values. In the page, after `catalogues`:

```ts
    // The whole catalogues, inactive included: only to name a stored value the options no longer offer.
    const retainFrom: NarrowingCatalogues = useMemo(
        () => ({ types: projectTypes, components, activities: activityTypes }),
        [projectTypes, components, activityTypes],
    )
```

Pass it to `DayCard` as `retainFrom={retainFrom}`, add `retainFrom: NarrowingCatalogues` to `DayCardProps`, and destructure it in `DayCard`.

4. Per row, under the `project` line from Task 2, compute the options and any retained values:

```ts
                            const options = narrowingOptions(project, catalogues)
                            const keptType = retainedOption(options.types, t.projectTypeId, retainFrom.types)
                            const keptComponent = retainedOption(options.components, t.projectComponentId, retainFrom.components)
                            const keptActivity = retainedOption(options.activities, t.activityTypeId, retainFrom.activities)
```

5. Rewrite the three narrowing selects to list `options.*`, lock on a single option, and show a retained value as a disabled item. Type:

```tsx
                                {shown.type && (
                                <Select
                                    size="small"
                                    displayEmpty
                                    value={t.projectTypeId}
                                    onChange={(e) => onUpdateTask(t._id, 'projectTypeId', e.target.value)}
                                    disabled={disabled || isLockedToSingle(options.types, t.projectTypeId)}
                                    sx={TASK_FIELD_SX}
                                    inputProps={{ 'aria-label': 'Type' }}
                                >
                                    <MenuItem value="">
                                        <Box component="em" sx={{ color: 'text.disabled' }}>Any type…</Box>
                                    </MenuItem>
                                    {options.types.map((pt) => (
                                        <MenuItem key={pt.id} value={String(pt.id)}>
                                            {pt.icon ? `${pt.icon} ${pt.name}` : pt.name}
                                        </MenuItem>
                                    ))}
                                    {/* A stored value the project no longer offers still reads, but cannot be picked again. */}
                                    {keptType && (
                                        <MenuItem value={String(keptType.id)} disabled>
                                            {keptType.icon ? `${keptType.icon} ${keptType.name}` : keptType.name}
                                        </MenuItem>
                                    )}
                                </Select>
                                )}
```

Component — same shape with `options.components`, `t.projectComponentId`, `'projectComponentId'`, `'Component'`, placeholder `Component…`, `keptComponent`. Activity — same shape with `options.activities`, `t.activityTypeId`, `'activityTypeId'`, `'Activity'`, placeholder `Activity…`, `keptActivity`.

Remove the direct `componentOptionsFor` / `activityOptionsFor` calls these replace; if the imports are then unused in this file, drop them (the `retained*Id` imports stay — `updateTask` uses them).

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx
```

Expected: PASS, 14 tests, and no MUI "out-of-range value" warning in the output for the loaded-row tests.

- [ ] **Step 5: Lint and the rest of the timesheet tests**

```bash
node_modules/.bin/vitest run src/components/timesheet src/lib/timesheet-columns.test.ts
npm run lint
```

Expected: PASS; lint 0 errors.

- [ ] **Step 6: Commit**

```bash
git add client/src/components/timesheet/NewTimesheetPage.tsx client/src/components/timesheet/NewTimesheetPage.test.tsx
git commit -m "Timesheet editor: fill in and lock the one option a project leaves; keep a stored value the project no longer offers readable

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Entries, not tasks — wording, CLAUDE.md, full run

**Files:**
- Modify: `client/src/components/timesheet/NewTimesheetPage.tsx` (strings: `'+ Add task'` ~line 1310, `'Please fix the highlighted tasks before saving.'` ~line 517, `'Add at least one task before submitting.'` ~line 525, `taskCount === 1 ? '1 task' : \`${taskCount} tasks\`` ~line 998, the hint `Add tasks as you go` ~line 815)
- Modify: `client/src/lib/validation/timesheet.ts` (two messages)
- Modify: `CLAUDE.md` (the `TimesheetEntry` row of the Domain Model table)
- Test: `client/src/components/timesheet/NewTimesheetPage.test.tsx`

**Interfaces:** none new.

- [ ] **Step 1: Write the failing test**

Append to `client/src/components/timesheet/NewTimesheetPage.test.tsx`:

```tsx
describe('NewTimesheetPage — rows are entries', () => {
    it('calls a row an entry, not a task, which is the picker beside it', async () => {
        await renderPage()
        expect(screen.getByRole('button', { name: '+ Add entry' })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: '+ Add task' })).toBeNull()

        // Hours with no project: the row is flagged and the message names entries.
        fireEvent.change(screen.getByPlaceholderText('0'), { target: { value: '2' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))
        expect(await screen.findByText('Please fix the highlighted entries before saving.')).toBeInTheDocument()
        expect(screen.getByText('Select a project for this entry.')).toBeInTheDocument()
    })
})
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx -t "rows are entries"
```

Expected: FAIL — the button is still "+ Add task".

- [ ] **Step 3: Implement**

In `NewTimesheetPage.tsx` change exactly these strings:

| Was | Becomes |
|---|---|
| `+ Add task` | `+ Add entry` |
| `Please fix the highlighted tasks before saving.` | `Please fix the highlighted entries before saving.` |
| `Add at least one task before submitting.` | `Add at least one entry before submitting.` |
| `taskCount === 1 ? '1 task' : \`${taskCount} tasks\`` | `taskCount === 1 ? '1 entry' : \`${taskCount} entries\`` |
| `Click a day to expand it. Add tasks as you go — …` | `Click a day to expand it. Add entries as you go — …` |

In `client/src/lib/validation/timesheet.ts`:

| Was | Becomes |
|---|---|
| `Enter hours for this task.` | `Enter hours for this entry.` |
| `Select a project for this task.` | `Select a project for this entry.` |

Also update the comment at the top of that file ("Per-task (timesheet entry) rules") to "Per-entry rules". Grep `client/src` for `for this task.` and `highlighted tasks` afterwards to be sure nothing else quotes the old strings:

```bash
grep -rn "for this task\.\|highlighted tasks\|Add at least one task\|Add task" client/src --include=*.ts --include=*.tsx
```

Expected: no matches outside git history.

- [ ] **Step 4: Run the test to verify it passes**

```bash
node_modules/.bin/vitest run src/components/timesheet/NewTimesheetPage.test.tsx
```

Expected: PASS, 15 tests.

- [ ] **Step 5: CLAUDE.md**

In `CLAUDE.md`, in the Domain Model table's `TimesheetEntry` row, append to the cell (before the closing `|`):

```
 **The editor asks only what there is to choose** (`client/src/lib/timesheet-columns.ts`): the Type, Component and Activity columns are shown only when their active catalogue has two or more entries (`narrowingColumns`, decided once per sheet so the header strip and the rows share one grid); after a project or task is picked, the one option the project leaves is filled in and the picker locked (`fillSingleOptions` — blanks only, after the row's retention step — and `isLockedToSingle`; an unclassified project leaves no type and is not filled, since picking a type would clear it); a hidden column with exactly one catalogue entry writes that entry silently; and a stored value the project no longer offers is listed as a disabled item so a loaded row still reads (`retainedOption`, from the unfiltered catalogue). The editor's rows are "entries" in every message — "task" is the picker beside them. `NewTimesheetPage.test.tsx` drives the real MUI selects; `timesheet-columns.test.ts` pins the rules.
```

- [ ] **Step 6: Full run, lint, build**

From `client/`:

```bash
node_modules/.bin/vitest run src/components/timesheet src/lib
npm run lint
npm run build
```

Expected: every test PASS; lint 0 errors (the 5 known warnings only); `tsc -b && vite build` exit 0. Do not run the whole client suite in one process — it exhausts memory and still exits 0; the batches above plus the earlier directories are the reliable check.

- [ ] **Step 7: Commit**

```bash
git add client/src/components/timesheet/NewTimesheetPage.tsx client/src/components/timesheet/NewTimesheetPage.test.tsx client/src/lib/validation/timesheet.ts CLAUDE.md
git commit -m "Timesheet editor: a row is an entry, not a task; CLAUDE.md records the column rules

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage.** 2.1 (visibility by catalogue, once per sheet, grid and headers as functions) → Task 2. 2.2 (fill blanks after retention; lock a single option; hidden single entry written silently; zero options stay empty; never overwrite) → Tasks 1 and 3. 2.3 (common case is Task · Project · Hours) → Task 2's "no catalogue in use" test. 2.4 (wording) → Task 4. Spec testing list: empty component catalogue hides header and picker (Task 2); two components show them (Task 2); a project carrying one type shows the picker disabled with that type and saves it (Task 3); a project carrying two leaves it enabled and empty (Task 3); a stored legacy value outside the options still displays (Task 3); header strip columns match the row (Task 2, `headerLabels` against the comboboxes).
- **Type consistency.** `NarrowingCatalogues`, `NarrowingColumns`, `narrowingColumns`, `narrowingOptions`, `fillSingleOptions`, `isLockedToSingle`, `retainedOption` are named identically in Tasks 1–3. `DayCardProps` gains `catalogues`, `shown` (Task 2) and `retainFrom` (Task 3) and loses the three `active*` props (Task 2). `data-testid="entry-row"` is added in Task 2 and read by `headerLabels` in the test.
- **Review Focus.** 1 → Task 3 (`'still reads, and the picker stays editable…'`). 2 → Task 3 (`'keeps a filled value the next project still offers, and refills when it does not'`). 3 → Task 3 (`'writes a hidden single-entry catalogue silently'`). 4 → Task 3 (`'fills in after a task sets the project'`). 5 → Task 2 (`'leaves a row of Task, Project and Hours when no catalogue is in use'` and the `headerLabels` assertions).
- **One judgment call recorded here for the executor:** when a project is chosen, the Type picker now lists only the types that project carries (`narrowingOptions(...).types`) rather than every carried type. Before, picking a type the project lacked cleared the project through `retainedProjectId`; listing only compatible types removes that dead end. An unclassified project therefore lists no types (only "Any type…"), which matches the spec's "zero options" reading.
