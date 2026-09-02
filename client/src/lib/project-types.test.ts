import { describe, expect, it } from 'vitest'
import { projectOptionsFor, retainedProjectId, typeOptionsFrom } from './project-types'
import type { Project, ProjectType, ProjectTypeSummary } from './types'

function projectType(id: number, name: string): ProjectType {
    return { id, name, description: '', icon: '🗂️', colorKey: 'default', isActive: true, usedInProjects: 0 }
}

const SUPPORT = projectType(1, 'Support')
const INQUIRY = projectType(2, 'Inquiry')
const ISSUE = projectType(3, 'Issue')

const CATALOGUE = [SUPPORT, INQUIRY, ISSUE]

function carried(type: ProjectType): ProjectTypeSummary {
    return { id: type.id, name: type.name, icon: type.icon, colorKey: type.colorKey }
}

function project(id: number, name: string, ...types: ProjectTypeSummary[]): Project {
    return { id, name, types } as Project
}

const APOLLO = project(1, 'Apollo', carried(SUPPORT), carried(INQUIRY))
const BOREALIS = project(2, 'Borealis', carried(SUPPORT))
const UNCLASSIFIED = project(3, 'Cassini')

const PROJECTS = [APOLLO, BOREALIS, UNCLASSIFIED]

describe('typeOptionsFrom', () => {
    it('offers only the types some project is classified as', () => {
        expect(typeOptionsFrom(PROJECTS, CATALOGUE).map((t) => t.name)).toEqual(['Support', 'Inquiry'])
    })

    // Choosing Issue here would empty the project dropdown underneath it, so it
    // is never offered in the first place.
    it('leaves out a type no project carries', () => {
        expect(typeOptionsFrom(PROJECTS, CATALOGUE)).not.toContain(ISSUE)
    })

    it('offers nothing when no project is classified at all', () => {
        expect(typeOptionsFrom([UNCLASSIFIED], CATALOGUE)).toEqual([])
    })
})

describe('projectOptionsFor', () => {
    it('offers only projects classified as the chosen type', () => {
        expect(projectOptionsFor('1', PROJECTS).map((p) => p.name)).toEqual(['Apollo', 'Borealis'])
        expect(projectOptionsFor('2', PROJECTS).map((p) => p.name)).toEqual(['Apollo'])
    })

    // Unclassified projects are only reachable with the type left blank, which is
    // also what every row starts as.
    it('offers every project, unclassified included, when no type is chosen', () => {
        expect(projectOptionsFor('', PROJECTS)).toEqual(PROJECTS)
    })

    it('offers nothing for a type no project carries', () => {
        expect(projectOptionsFor('3', PROJECTS)).toEqual([])
    })
})

describe('retainedProjectId', () => {
    it('keeps a project the new type applies to', () => {
        expect(retainedProjectId('1', '2', PROJECTS)).toBe('1')
    })

    it('drops a project the new type does not apply to', () => {
        expect(retainedProjectId('2', '2', PROJECTS)).toBe('')
    })

    it('keeps whatever is chosen when the type is cleared', () => {
        expect(retainedProjectId('3', '', PROJECTS)).toBe('3')
    })

    it('stays empty when nothing is chosen', () => {
        expect(retainedProjectId('', '1', PROJECTS)).toBe('')
    })
})
