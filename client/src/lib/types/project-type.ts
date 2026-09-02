export interface ProjectType {
    id: number
    name: string
    description: string
    icon: string
    colorKey: string
    isActive: boolean
    // How many projects are classified as this type. Also what makes a type
    // undeletable — the API refuses while it is above zero.
    usedInProjects: number
}
