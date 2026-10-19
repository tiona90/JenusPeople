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
