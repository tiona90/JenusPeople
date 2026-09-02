import { describe, expect, it } from 'vitest'
import { activityOptionsFor, retainedActivityId } from './project-activities'
import type { Project, ProjectActivity, ProjectActivityType } from './types'

function activityType(id: number, name: string, isActive = true): ProjectActivityType {
    return { id, name, description: '', icon: '🏷️', colorKey: 'default', isActive, hoursYtd: 0, usedInProjects: 0 }
}

const DEVELOPMENT = activityType(1, 'Development')
const TESTING = activityType(2, 'Testing')
const DESIGN = activityType(3, 'Design')

const CATALOGUE = [DEVELOPMENT, TESTING, DESIGN]

function project(...activities: ProjectActivity[]): Project {
    return { id: 1, name: 'Apollo', activities } as Project
}

function assigned(type: ProjectActivityType): ProjectActivity {
    return { id: type.id, name: type.name, icon: type.icon, colorKey: type.colorKey }
}

describe('activityOptionsFor', () => {
    it('offers only what the project has assigned', () => {
        const options = activityOptionsFor(project(assigned(DEVELOPMENT), assigned(TESTING)), CATALOGUE)

        expect(options.map((a) => a.name)).toEqual(['Development', 'Testing'])
    })

    // Every project predating project-level assignment has none, and must keep
    // offering the whole catalogue rather than an empty dropdown.
    it('offers the whole catalogue when the project has assigned none', () => {
        expect(activityOptionsFor(project(), CATALOGUE)).toEqual(CATALOGUE)
    })

    it('offers the whole catalogue when no project is chosen yet', () => {
        expect(activityOptionsFor(undefined, CATALOGUE)).toEqual(CATALOGUE)
    })

    // The catalogue passed in is already filtered to active types, so a type
    // disabled org-wide drops out even while the project still references it.
    it('drops an assigned activity that is no longer in the catalogue', () => {
        const options = activityOptionsFor(
            project(assigned(DEVELOPMENT), assigned(DESIGN)),
            [DEVELOPMENT, TESTING],
        )

        expect(options.map((a) => a.name)).toEqual(['Development'])
    })
})

describe('retainedActivityId', () => {
    it('keeps the activity when the newly chosen project still offers it', () => {
        expect(retainedActivityId('1', project(assigned(DEVELOPMENT)), CATALOGUE)).toBe('1')
    })

    // Otherwise the row would submit an activity the project has not assigned,
    // which the server refuses -- the field is cleared instead of failing on save.
    it('clears the activity when the newly chosen project does not offer it', () => {
        expect(retainedActivityId('3', project(assigned(DEVELOPMENT)), CATALOGUE)).toBe('')
    })

    it('keeps the activity when the newly chosen project has assigned none', () => {
        expect(retainedActivityId('3', project(), CATALOGUE)).toBe('3')
    })

    it('leaves an empty activity empty', () => {
        expect(retainedActivityId('', project(assigned(DEVELOPMENT)), CATALOGUE)).toBe('')
    })
})
