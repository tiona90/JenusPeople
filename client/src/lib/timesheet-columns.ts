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
