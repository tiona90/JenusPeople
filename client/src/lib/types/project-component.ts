export interface ProjectComponent {
    id: number
    name: string
    description: string
    icon: string
    colorKey: string
    isActive: boolean
    // How many projects have declared this component.
    usedInProjects: number
}
