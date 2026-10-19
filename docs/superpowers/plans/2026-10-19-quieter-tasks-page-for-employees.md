# Quieter Tasks Page for Employees — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Employee opens a Tasks page with one summary line instead of stat tiles, no layout toggle, and cards that carry only what tells them something; Managers and HR Administrators keep their page.

**Architecture:** Two pure rules go into `client/src/lib/work-tasks.ts` — `employeeSummary` (the one-line replacement for the tiles) and `cardBadges` (which chips a card shows for this viewer) — with unit tests beside the existing helpers. `TaskCard.tsx` renders `cardBadges` and builds its facts row as a list that may be empty. `TasksPage.tsx` gates the tiles and the layout toggle on `canManageTasks` and renders the summary line for everyone else. Client only; no API change.

**Tech Stack:** React 19, TypeScript, MUI 7, Vitest + Testing Library. Run tests with `node_modules/.bin/vitest` from `client/` (a bare `npx vitest` resolves the wrong version on this machine).

**Spec:** `docs/superpowers/specs/2026-10-19-simpler-tasks-and-timesheets-design.md`, section "Phase 1 — A quieter Tasks page for Employees".

## Global Constraints

- "Employee" means a viewer for whom `canManageTasks(roles)` (`client/src/lib/roles.ts`) is false. Never test the role string directly.
- Remove from the view, never from the model: no field on `WorkTask`, no Task Setting and no API call changes.
- The card chip rules apply to every viewer; the tile and layout rules apply to Employees only.
- The `tasks-layout` localStorage key is still read and written for viewers who have the toggle. For an Employee it is ignored, not cleared.
- Spec 1.4 (hide the priority filter when priority is hidden) is **already implemented** at `TasksPage.tsx` (`isShown(settings, 'priority') && <SelectFilter ariaLabel="Priority filter" …/>`) and pinned by the test "hides the priority chip and filter when priority is hidden". No task touches it.
- "Created by" already appears in `TaskDetailsDialog.tsx` ("From: {createdByName}" plus a Created fact). Nothing is added there; the card only stops repeating it.
- Commit after every task with a message in the repository's style (a sentence, present tense, what changed and for whom), ending with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

Inputs the spec implies but does not spell out, each pinned by a test in the task that owns the code:

1. **An Employee with nothing open** must read "No open tasks", not "0 open". (Task 1)
2. **Due date and Target hours both hidden on an open task** must leave no empty bordered facts strip at the bottom of the card. (Task 2)
3. **A `Low` priority** is not the default and must still show; `Normal` is the default and must not. A **null `isBillable`** (a legacy row) shows no chip at all. (Task 1)
4. **An Employee whose browser remembers `tasks-layout: "board"`** (set while they were a Manager, or by an older build) must see cards, and the stored value must survive untouched. (Task 3)
5. **A Done task** must keep its "Completed" fact after "Created" is dropped, on the grid card and on the board card. (Task 2)

---

### Task 1: The two pure rules in `lib/work-tasks.ts`

**Files:**
- Modify: `client/src/lib/work-tasks.ts` (`taskStats` at ~line 152; add two exports after it)
- Test: `client/src/lib/work-tasks.test.ts`

**Interfaces:**
- Consumes: `WorkTask`, `WorkTaskSettings`, `isShown(settings, field)` from `./task-settings`, `isAwaitingConfirmation`, `isOpenTask` (all already imported or defined in the file).
- Produces:
  - `taskStats(...)` gains one field: `awaitingConfirmation: number` — every task in `AwaitingConfirmation`, whoever reviews it (distinct from the existing `awaitingMyConfirmation`, which counts `canConfirm`).
  - `employeeSummary(stats: ReturnType<typeof taskStats>, confirmationOn: boolean): string[]` — parts to join with " · "; the first is the headline.
  - `cardBadges(task: WorkTask, settings: WorkTaskSettings, viewerManages: boolean): { department: boolean; priority: boolean; billable: boolean }`.

- [ ] **Step 1: Write the failing tests**

Append to `client/src/lib/work-tasks.test.ts`, inside the top-level `describe('work-tasks helpers', …)` block is fine, but a new block is clearer — add at the end of the file:

```ts
describe('employeeSummary', () => {
    const stats = (over: Partial<ReturnType<typeof taskStats>>) => ({
        total: 0, open: 0, inProgress: 0, overdue: 0, doneThisMonth: 0, assignedToMeOpen: 0, awaitingMyConfirmation: 0, awaitingConfirmation: 0, ...over,
    })

    it('leads with the open count and adds only the parts that are not zero', () => {
        expect(employeeSummary(stats({ open: 2, overdue: 1, awaitingConfirmation: 1 }), true)).toEqual(['2 open', '1 overdue', '1 waiting for confirmation'])
        expect(employeeSummary(stats({ open: 2 }), true)).toEqual(['2 open'])
        expect(employeeSummary(stats({ open: 1, overdue: 1 }), true)).toEqual(['1 open', '1 overdue'])
    })

    it('says so when nothing is open', () => {
        expect(employeeSummary(stats({}), true)).toEqual(['No open tasks'])
    })

    it('says nothing about confirmation when the Task Settings do not ask for it', () => {
        expect(employeeSummary(stats({ open: 1, awaitingConfirmation: 1 }), false)).toEqual(['1 open'])
    })
})

describe('cardBadges', () => {
    it('shows the department to a viewer who manages tasks and not to an Employee', () => {
        expect(cardBadges(t({}), DEFAULT_TASK_SETTINGS, true).department).toBe(true)
        expect(cardBadges(t({}), DEFAULT_TASK_SETTINGS, false).department).toBe(false)
    })

    it('shows a priority only when it is not the default', () => {
        expect(cardBadges(t({ priority: 'Normal' }), DEFAULT_TASK_SETTINGS, true).priority).toBe(false)
        expect(cardBadges(t({ priority: 'High' }), DEFAULT_TASK_SETTINGS, true).priority).toBe(true)
        expect(cardBadges(t({ priority: 'Low' }), DEFAULT_TASK_SETTINGS, true).priority).toBe(true)
        expect(cardBadges(t({ priority: 'High' }), { ...DEFAULT_TASK_SETTINGS, showPriority: false }, true).priority).toBe(false)
    })

    it('badges a billable task and says nothing about a non-billable or unanswered one', () => {
        expect(cardBadges(t({ isBillable: true }), DEFAULT_TASK_SETTINGS, true).billable).toBe(true)
        expect(cardBadges(t({ isBillable: false }), DEFAULT_TASK_SETTINGS, true).billable).toBe(false)
        expect(cardBadges(t({ isBillable: null }), DEFAULT_TASK_SETTINGS, true).billable).toBe(false)
        expect(cardBadges(t({ isBillable: true }), { ...DEFAULT_TASK_SETTINGS, billableRequirement: 'Hidden' }, true).billable).toBe(false)
    })
})
```

Add `cardBadges, employeeSummary` to the import list on line 4 of the test file.

Also extend the existing `taskStats` expectation (around line 95) so the new field is part of the shape:

```ts
        expect(taskStats(tasks, 'me', '2026-09-28')).toEqual({
            total: 5, open: 2, inProgress: 1, overdue: 1, doneThisMonth: 1, assignedToMeOpen: 1, awaitingMyConfirmation: 0, awaitingConfirmation: 0,
        })
```

And in the `describe('awaiting confirmation', …)` block (around line 182), beside the `awaitingMyConfirmation` assertion, add:

```ts
        expect(taskStats(tasks, 'me', '2026-09-29').awaitingConfirmation).toBe(2)
```

(That block's `tasks` holds two `AwaitingConfirmation` tasks — one the viewer may confirm, one not — and one `ToDo`; `awaitingConfirmation` counts both waiting ones, where `awaitingMyConfirmation` counts the one.)

- [ ] **Step 2: Run the tests to verify they fail**

Run from `client/`:

```bash
node_modules/.bin/vitest run src/lib/work-tasks.test.ts
```

Expected: FAIL — `employeeSummary` and `cardBadges` are not exported; the `taskStats` `toEqual` fails on the missing `awaitingConfirmation` key.

- [ ] **Step 3: Implement**

In `client/src/lib/work-tasks.ts`, inside `taskStats`, add the field after `awaitingMyConfirmation`:

```ts
        awaitingMyConfirmation: tasks.filter((t) => t.canConfirm === true).length,
        // Every task waiting on somebody's confirmation, the viewer's own Done among them.
        awaitingConfirmation: tasks.filter(isAwaitingConfirmation).length,
```

Then add, directly after `taskStats`:

```ts
/**
 * The Employee's one line in place of the stat tiles: the open count first, then
 * only the parts with something in them. "Waiting for confirmation" is their own
 * Done awaiting a reviewer, and is said only while the Task Settings ask for one —
 * switching confirmation off sweeps those tasks to Done, so the figure would be 0
 * anyway, but the words would still promise a stage that no longer exists.
 */
export function employeeSummary(stats: ReturnType<typeof taskStats>, confirmationOn: boolean): string[] {
    const parts = [stats.open === 0 ? 'No open tasks' : `${stats.open} open`]
    if (stats.overdue > 0) parts.push(`${stats.overdue} overdue`)
    if (confirmationOn && stats.awaitingConfirmation > 0) parts.push(`${stats.awaitingConfirmation} waiting for confirmation`)
    return parts
}

/**
 * Which chips a card shows. Each says something only when it is not the default
 * state: a department to somebody who sees more than one, a priority other than
 * Normal, a task that *is* billable. A chip that is on every card tells nobody
 * anything — it is the card's job to make two tasks look different.
 */
export function cardBadges(task: WorkTask, settings: WorkTaskSettings, viewerManages: boolean): { department: boolean; priority: boolean; billable: boolean } {
    return {
        department: viewerManages,
        priority: isShown(settings, 'priority') && task.priority !== 'Normal',
        billable: isShown(settings, 'billable') && task.isBillable === true,
    }
}
```

`WorkTaskSettings` is already imported as a type in this file (it is used by `taskFacts` and `tasksToCsv`); if the import is missing, add `import type { WorkTaskSettings } from './api/work-task-settings'`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/lib/work-tasks.test.ts
```

Expected: PASS, every test in the file.

- [ ] **Step 5: Commit**

```bash
git add client/src/lib/work-tasks.ts client/src/lib/work-tasks.test.ts
git commit -m "Tasks: the Employee's summary line and the card's chip rule, as pure helpers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: A thinner card

**Files:**
- Modify: `client/src/components/tasks/TaskCard.tsx` (`TaskItemProps` ~line 22; the chips block ~lines 236–246; the facts row ~lines 265–288)
- Modify: `client/src/components/tasks/TasksPage.tsx` (`itemProps` ~line 226: add `viewerManages: manages`)
- Test: `client/src/components/tasks/TasksPage.test.tsx`

**Interfaces:**
- Consumes: `cardBadges` from Task 1.
- Produces: `TaskItemProps.viewerManages: boolean` — every card, board card and list row is handed it through `TasksPage`'s `itemProps`, so `TaskBoard` and `TaskList` need no change. A facts row with `data-testid="task-facts"`, rendered only when it has a cell.

- [ ] **Step 1: Write the failing tests**

In `client/src/components/tasks/TasksPage.test.tsx`, replace the test `'badges each card billable or non-billable'` (around line 258) with:

```tsx
    it('badges a billable card and says nothing about a non-billable one', async () => {
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('Billable')).toBeInTheDocument()
        expect(within(await cardFor('I asked for this')).queryByText('Non-billable')).toBeNull()
        expect(within(await cardFor('I asked for this')).queryByText('Billable')).toBeNull()
    })

    it('shows the department to a Manager and not to an Employee, whose tasks are all in one', async () => {
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('Sales')).toBeInTheDocument()
    })

    it('hides the department chip from an Employee', async () => {
        renderPage(['Employee'])
        expect(within(await cardFor('Mine to do')).queryByText('Sales')).toBeNull()
    })

    it('shows a priority chip only when the priority is not Normal', async () => {
        api.getWorkTasks.mockResolvedValue([base, { ...base, id: 5, title: 'Ordinary', priority: 'Normal' }, { ...base, id: 6, title: 'Minor', priority: 'Low' }])
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('High priority')).toBeInTheDocument()
        expect(within(await cardFor('Ordinary')).queryByText(/priority/)).toBeNull()
        expect(within(await cardFor('Minor')).getByText('Low priority')).toBeInTheDocument()
    })

    it('drops "Created by" from the card but keeps "Completed" on a finished task', async () => {
        api.getWorkTasks.mockResolvedValue([base, { ...base, id: 7, title: 'Finished', status: 'Done', completedAtUtc: '2026-09-15T08:00:00' }])
        renderPage()
        await cards()
        fireEvent.change(screen.getByRole('combobox', { name: 'Status filter' }), { target: { value: 'any' } })

        const open = await cardFor('Mine to do')
        expect(within(open).queryByText('Created')).toBeNull()
        expect(within(open).queryByText(/by Boss/)).toBeNull()
        expect(within(open).getByText('Due')).toBeInTheDocument()

        const finished = await cardFor('Finished')
        expect(within(finished).getByText('Completed')).toBeInTheDocument()
        expect(within(finished).queryByText('Created')).toBeNull()
    })

    it('draws no facts strip when there is no fact to show', async () => {
        api.getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Hidden', targetHoursRequirement: 'Hidden' })
        renderPage()
        const card = await cardFor('Mine to do')
        expect(within(card).queryByTestId('task-facts')).toBeNull()
    })
```

In the `describe('TasksPage layouts', …)` block, extend the board test (`'lays every task out by status on the board…'`) with a Done task so the board card's Completed fact is pinned. Replace its first line and add two assertions at the end:

```tsx
        api.getWorkTasks.mockResolvedValue([TASKS[0], { ...TASKS[1], status: 'InProgress' }, { ...base, id: 8, title: 'Shipped', status: 'Done', completedAtUtc: '2026-09-15T08:00:00' }])
        renderPage()
        await cards()
        fireEvent.change(screen.getByRole('combobox', { name: 'Status filter' }), { target: { value: 'any' } })
        fireEvent.click(screen.getByRole('button', { name: /Board/ }))
        …(existing assertions, except the one expecting no 'Done' region — with the filter on 'any' and a Done task present, that column exists now)…
        const doneColumn = screen.getByRole('region', { name: 'Done' })
        expect(within(doneColumn).getByText('Completed')).toBeInTheDocument()
        expect(within(doneColumn).queryByText('Created')).toBeNull()
```

Keep the existing assertion `expect(screen.queryByRole('region', { name: 'Assigned to you' })).toBeNull()`. Remove `expect(screen.queryByRole('region', { name: 'Done' })).toBeNull()` — it no longer holds with the filter on 'any'. (If you would rather keep that assertion, write the Done-column check as a separate test with its own render; either is fine.)

- [ ] **Step 2: Run the tests to verify they fail**

```bash
node_modules/.bin/vitest run src/components/tasks/TasksPage.test.tsx
```

Expected: FAIL — 'Non-billable' is still rendered, 'Sales' shows for the Employee, 'Normal priority' is rendered, 'Created' is on every card, `task-facts` does not exist yet (that one fails as "found" rather than "not found" only once the testid exists; before that the queryByTestId test passes vacuously — that is acceptable; it starts guarding once Step 3 adds the testid). TypeScript will also complain that `viewerManages` is missing wherever `TaskItemProps` is built once Step 3 adds it; that is expected mid-task.

- [ ] **Step 3: Implement the card**

In `client/src/components/tasks/TaskCard.tsx`:

1. Import the rule — extend the `../../lib/work-tasks` import:

```ts
import {
    PRIORITY_LABELS, SETTABLE_STATUSES, STATUS_LABELS, cardBadges, formatTaskDate as formatDate,
    isAwaitingConfirmation, isOpenTask, nextStatusAction, plural, taskFacts,
} from '../../lib/work-tasks'
```

2. Add the prop to `TaskItemProps`, after `settings`:

```ts
    /** Whether the viewer manages tasks (a Manager or HR Administrator). An Employee's tasks are all in their one department, so the department chip says nothing to them. */
    viewerManages: boolean
```

3. In `TaskCard`, after `const f = taskFacts(task, today, settings)`, add:

```ts
    const badges = cardBadges(task, settings, props.viewerManages)
    const showChipRow = badges.department || badges.priority || badges.billable || (!!task.projectName && !board)
```

4. Replace the chips block (the `<Box sx={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>` holding `<Chip>{task.departmentName}</Chip>` …) with:

```tsx
                {/* Chips carry only the non-default state (`cardBadges`); the project name tells two tasks apart. */}
                {showChipRow && (
                    <Box sx={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
                        {badges.department && <Chip>{task.departmentName}</Chip>}
                        {badges.priority && <PriorityChip task={task} />}
                        {badges.billable && <Chip tone="success">Billable</Chip>}
                        {task.projectName && !board && (
                            <Box sx={{ fontSize: 11, color: 'text.secondary', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{task.projectName}</Box>
                        )}
                    </Box>
                )}
```

5. Replace the facts row (the `<Box sx={{ display: 'grid', gap: '12px', mt: 'auto', … }}>` holding the three `<Fact …/>`) with a list that may be empty. First, above the `return`, build it:

```ts
    // Due, target and — once finished — when. "Created by" is the details dialog's
    // and the section header's; repeating it on every card said nothing new.
    const facts: { label: string; value: string; sub?: string; valueColor?: string }[] = []
    if (f.showDue) facts.push({
        label: 'Due',
        value: task.dueDate ? formatDate(task.dueDate) : '—',
        sub: task.dueDate ? (f.late > 0 ? 'past due' : f.closed ? 'closed' : 'on track') : 'no date set',
        valueColor: f.late > 0 ? 'error.main' : undefined,
    })
    if (f.showTarget && !board) facts.push({
        label: 'Target',
        value: task.targetHours != null ? `${task.targetHours}h` : '—',
        sub: task.targetHours == null && f.progress.text === 'nothing logged' ? 'no target set' : f.progress.text,
        valueColor: f.progress.over ? 'error.main' : undefined,
    })
    if (done) facts.push({ label: 'Completed', value: formatDate(task.completedAtUtc!), sub: 'finished' })
```

(`done` is already defined just above as `task.status === 'Done' && task.completedAtUtc`.) Then render:

```tsx
                {facts.length > 0 && (
                    <Box data-testid="task-facts" sx={{
                        display: 'grid', gap: '12px', mt: 'auto', pt: '10px', borderTop: '1px solid', borderTopColor: 'divider',
                        gridTemplateColumns: `repeat(${facts.length}, minmax(0, 1fr))`,
                    }}>
                        {facts.map((x) => <Fact key={x.label} {...x} />)}
                    </Box>
                )}
```

6. In `client/src/components/tasks/TasksPage.tsx`, in `itemProps`, add the prop:

```ts
    const itemProps = (task: WorkTask): TaskItemProps => ({
        task,
        today,
        settings,
        viewerManages: manages,
        statusPending: moveStatus.isPending,
        …
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/tasks
```

Expected: PASS across the whole `tasks` directory (TasksPage, TaskDialog, TaskDetailsDialog, TaskAttachments). If `TaskDetailsDialog.test.tsx` or another file builds a `TaskItemProps` literal, it now needs `viewerManages`; grep for `statusPending:` in `client/src` to find any.

- [ ] **Step 5: Lint**

```bash
npm run lint
```

Expected: clean. If `PRIORITY_LABELS` or anything else is now unused in `TaskCard.tsx`, remove it from the import.

- [ ] **Step 6: Commit**

```bash
git add client/src/components/tasks/TaskCard.tsx client/src/components/tasks/TasksPage.tsx client/src/components/tasks/TasksPage.test.tsx
git commit -m "Task card: chips only for the non-default state, and no Created-by fact

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Tiles and layout toggle for Managers and HR; a summary line for Employees

**Files:**
- Modify: `client/src/components/tasks/TasksPage.tsx` (stats row ~lines 256–314; toolbar ~lines 316–372; `handedToMeOpen` ~line 188; `layout` state ~line 196; the render branches ~lines 377 and 404)
- Test: `client/src/components/tasks/TasksPage.test.tsx`

**Interfaces:**
- Consumes: `employeeSummary` from Task 1; `manages` (already in the component).
- Produces: a `data-testid="stat-summary"` element for Employees; no `stat-*` tiles and no `role="group"` named "Layout" for them.

- [ ] **Step 1: Write the failing tests**

In `client/src/components/tasks/TasksPage.test.tsx`, in the test `"puts the work handed to an Employee first and their own tasks apart underneath"` (around line 276), replace the last assertion

```tsx
        expect(within(screen.getByTestId('stat-mine')).getByText(/From Manager & HR/)).toBeInTheDocument()
```

with

```tsx
        // Both tasks inherit base's 2020 due date, so both are overdue.
        expect(screen.getByTestId('stat-summary')).toHaveTextContent('2 open · 2 overdue')
        expect(screen.queryByTestId('stat-mine')).toBeNull()
```

Then add these tests to the main `describe('TasksPage', …)` block:

```tsx
    it('gives an Employee one summary line instead of the tiles, and no layout toggle', async () => {
        renderPage(['Employee'])
        await cards()
        expect(screen.queryByTestId('stat-open')).toBeNull()
        expect(screen.queryByTestId('stat-overdue')).toBeNull()
        expect(screen.queryByTestId('stat-done')).toBeNull()
        expect(screen.queryByRole('group', { name: 'Layout' })).toBeNull()
        expect(screen.getByTestId('stat-summary')).toBeInTheDocument()
    })

    it("counts an Employee's own Done awaiting confirmation in the summary line", async () => {
        api.getWorkTasks.mockResolvedValue([{ ...base, dueDate: null, status: 'AwaitingConfirmation' }])
        renderPage(['Employee'])
        await cards()
        expect(screen.getByTestId('stat-summary')).toHaveTextContent('1 open · 1 waiting for confirmation')
    })

    it('tells an Employee with nothing open so', async () => {
        api.getWorkTasks.mockResolvedValue([{ ...base, status: 'Done', completedAtUtc: '2026-09-15T08:00:00' }])
        renderPage(['Employee'])
        await screen.findByTestId('stat-summary')
        expect(screen.getByTestId('stat-summary')).toHaveTextContent('No open tasks')
    })

    it('keeps the tiles and the layout toggle for a Manager', async () => {
        renderPage()
        await cards()
        expect(screen.getByTestId('stat-open')).toBeInTheDocument()
        expect(screen.getByRole('group', { name: 'Layout' })).toBeInTheDocument()
        expect(screen.queryByTestId('stat-summary')).toBeNull()
    })
```

And in `describe('TasksPage layouts', …)`:

```tsx
    it('shows an Employee cards whatever layout the browser remembers, and leaves the memory alone', async () => {
        window.localStorage.setItem('tasks-layout', 'board')
        renderPage(['Employee'])
        expect(await cards()).toHaveLength(3)
        expect(screen.queryByRole('region', { name: 'To do' })).toBeNull()
        expect(screen.getByRole('region', { name: 'From your manager & HR' })).toBeInTheDocument()
        expect(window.localStorage.getItem('tasks-layout')).toBe('board')
    })
```

Note: `renderPage(['Employee'])` with the default `TASKS` renders all three tasks as cards (the server would only send an Employee their own, but the mock sends all three; the page does not filter by assignee itself).

- [ ] **Step 2: Run the tests to verify they fail**

```bash
node_modules/.bin/vitest run src/components/tasks/TasksPage.test.tsx
```

Expected: FAIL — `stat-summary` does not exist; `stat-open` and the Layout group render for the Employee; the stored `board` layout renders a board.

- [ ] **Step 3: Implement**

In `client/src/components/tasks/TasksPage.tsx`:

1. Extend the `../../lib/work-tasks` import with `employeeSummary`, and remove `isOpenTask` from it once `handedToMeOpen` is gone (step 3 below).

2. Replace the `handedToMeOpen` memo (the `useMemo(() => all.filter((t) => t.createdById !== userId && isOpenTask(t)).length, …)`) with:

```ts
    // The Employee's line in place of the tiles.
    const summary = useMemo(
        () => employeeSummary(stats, settings.requireCompletionConfirmation),
        [stats, settings.requireCompletionConfirmation],
    )
```

3. After the `layout` state, add the role gate:

```ts
    const [layout, setLayoutState] = useState<TaskLayout>(() => readStored(LAYOUT_KEY, parseLayout, 'cards'))
    const setLayout = (next: TaskLayout) => { setLayoutState(next); writeStored(LAYOUT_KEY, next) }
    // An Employee has few tasks and no toggle: always cards. The stored choice is kept
    // for the day they have the toggle again.
    const effectiveLayout: TaskLayout = manages ? layout : 'cards'
```

4. Wrap the stats row in `{manages && ( … )}`: the whole `<Box sx={{ display: 'grid', gridTemplateColumns: …` block through its closing `</Box>` after the `stat-mine` tile. Inside it, the `stat-mine` tile's Employee branch (`<StatCard label="📥 From Manager & HR" …/>`) can no longer be reached; simplify that tile to the Manager case:

```tsx
                {!isHr && <Box data-testid="stat-mine">
                    <StatCard
                        label="👤 Assigned To Me"
                        value={String(stats.assignedToMeOpen)}
                        valueColor="primary.main"
                        sub="open tasks on your plate"
                    />
                </Box>}
```

5. In the toolbar, as its **first** child (before the search box), add:

```tsx
                {!manages && (
                    <Box data-testid="stat-summary" sx={{ fontSize: 13, color: 'text.secondary', whiteSpace: 'nowrap' }}>
                        <Box component="strong" sx={{ color: 'text.primary', fontWeight: 700 }}>{summary[0]}</Box>
                        {summary.slice(1).map((part) => <Box component="span" key={part}>{` · ${part}`}</Box>)}
                    </Box>
                )}
```

6. Gate the toggle: `<LayoutToggle value={layout} onChange={setLayout} />` becomes `{manages && <LayoutToggle value={layout} onChange={setLayout} />}`.

7. Use `effectiveLayout` in both render branches: `{layout === 'board' ? (` → `{effectiveLayout === 'board' ? (` and `) : layout === 'list' ? (` → `) : effectiveLayout === 'list' ? (`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/tasks
```

Expected: PASS. The Manager tests `'sums the tasks up in the tiles'`, `'counts the tasks waiting for my confirmation in a tile'`, `'drops the To Confirm tile when confirmation is off'` and `'shows no Overdue tile…'` all render as Manager and must still pass unchanged.

- [ ] **Step 5: Lint**

```bash
npm run lint
```

Expected: clean — in particular no unused `isOpenTask` import or `handedToMeOpen` variable.

- [ ] **Step 6: Commit**

```bash
git add client/src/components/tasks/TasksPage.tsx client/src/components/tasks/TasksPage.test.tsx
git commit -m "Tasks page: one summary line and no layout toggle for an Employee; tiles stay for Managers and HR

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Record the rule and run everything once

**Files:**
- Modify: `CLAUDE.md` (the **Tasks** bullet under "Backend Patterns", the sentence beginning "The client mirrors it with `TASK_ROLES` / `canUseTasks`")

- [ ] **Step 1: Add the client rule to CLAUDE.md**

In the Tasks bullet, after the sentence ending "…(`TaskDialog`'s `personal` prop hides it for an Employee).", insert:

```
**An Employee's page is quieter**: no stat tiles — one line in the toolbar from `employeeSummary` in `lib/work-tasks.ts` ("2 open · 1 overdue · 1 waiting for confirmation", or "No open tasks") — and no Cards / List / Board toggle (always cards; the stored `tasks-layout` is kept, not cleared), both gated on `canManageTasks`. Every card, for every viewer, carries only the non-default state (`cardBadges`): the department chip only to a viewer who manages tasks, a priority chip only when it is not Normal, a Billable chip only when true (no "Non-billable"), and no "Created … by" fact — that is the details dialog's, and the section header already says who handed the task over. A Done task keeps its "Completed" fact; a card with nothing to put in the facts row draws none (`data-testid="task-facts"` absent). `TasksPage.test.tsx` and `work-tasks.test.ts` pin it.
```

- [ ] **Step 2: Run the whole tasks area and the helper tests**

From `client/`:

```bash
node_modules/.bin/vitest run src/components/tasks src/lib/work-tasks.test.ts src/lib/task-settings.test.ts
npm run lint
```

Expected: every test PASS; lint clean. (Do not run the entire client suite in one go — it runs out of memory and still exits 0; the per-directory batches above are the reliable check.)

- [ ] **Step 3: Type-check the client build**

```bash
npm run build
```

Expected: `tsc -b` and `vite build` both succeed. This is the only full type-check of the client; the test runner alone does not fail on a type error.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "CLAUDE.md: the Employee's quieter Tasks page and the card's chip rule

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage.** 1.1 → Task 3 (summary line, tiles gated). 1.2 → Task 3 (toggle gated, `effectiveLayout`, storage untouched). 1.3 → Tasks 1 and 2 (department / priority / billable / facts / project name kept). 1.4 → already implemented, stated in Global Constraints. Testing section → each task's tests plus Task 4's run.
- **Type consistency.** `cardBadges(task, settings, viewerManages)` and `employeeSummary(stats, confirmationOn): string[]` are used with those exact names and shapes in Tasks 2 and 3. `TaskItemProps.viewerManages` is set in `itemProps` (Task 2) and read in `TaskCard` only. `taskStats` gains `awaitingConfirmation`, read by `employeeSummary`.
- **Review Focus.** Item 1 → Task 1 (`'says so when nothing is open'`) and Task 3 (`'tells an Employee with nothing open so'`). Item 2 → Task 2 (`'draws no facts strip…'`). Item 3 → Task 1 (`cardBadges` priority and billable cases). Item 4 → Task 3 (stored `board`). Item 5 → Task 2 (grid and board Completed assertions).
