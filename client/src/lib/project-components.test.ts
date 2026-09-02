import { describe, expect, it } from 'vitest'
import { componentOptionsFor, retainedComponentId } from './project-components'
import type { Project, ProjectComponent, ProjectComponentSummary } from './types'

function component(id: number, name: string, isActive = true): ProjectComponent {
    return { id, name, description: '', icon: '🧩', colorKey: 'default', isActive, usedInProjects: 0 }
}

const DM = component(1, 'DM')
const LASERNET = component(2, 'Lasernet')
const JDOCS = component(3, 'jDocs')

const CATALOGUE = [DM, LASERNET, JDOCS]

function declared(c: ProjectComponent): ProjectComponentSummary {
    return { id: c.id, name: c.name, icon: c.icon, colorKey: c.colorKey }
}

function project(...components: ProjectComponentSummary[]): Project {
    return { id: 1, name: 'Apollo', components } as Project
}

describe('componentOptionsFor', () => {
    it('offers only what the project is made up of', () => {
        const options = componentOptionsFor(project(declared(DM), declared(LASERNET)), CATALOGUE)

        expect(options.map((c) => c.name)).toEqual(['DM', 'Lasernet'])
    })

    // Every project predating component assignment has none, and must keep
    // offering the whole catalogue rather than an empty dropdown.
    it('offers the whole catalogue when the project has declared none', () => {
        expect(componentOptionsFor(project(), CATALOGUE)).toEqual(CATALOGUE)
    })

    it('offers the whole catalogue when no project is chosen yet', () => {
        expect(componentOptionsFor(undefined, CATALOGUE)).toEqual(CATALOGUE)
    })

    // The catalogue passed in is already filtered to active components, so one
    // the project declared but an admin has since disabled drops out with it.
    it('leaves out a declared component missing from the catalogue', () => {
        const options = componentOptionsFor(project(declared(DM), declared(JDOCS)), [DM, LASERNET])

        expect(options.map((c) => c.name)).toEqual(['DM'])
    })
})

describe('retainedComponentId', () => {
    it('keeps a component the new project is made up of', () => {
        expect(retainedComponentId('1', project(declared(DM)), CATALOGUE)).toBe('1')
    })

    it('drops a component the new project has not declared', () => {
        expect(retainedComponentId('2', project(declared(DM)), CATALOGUE)).toBe('')
    })

    it('keeps anything while the project has declared nothing', () => {
        expect(retainedComponentId('3', project(), CATALOGUE)).toBe('3')
    })

    it('stays empty when nothing is chosen', () => {
        expect(retainedComponentId('', project(declared(DM)), CATALOGUE)).toBe('')
    })
})
