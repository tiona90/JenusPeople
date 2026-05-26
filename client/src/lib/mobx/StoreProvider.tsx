/* eslint-disable react-refresh/only-export-components -- intentional: provider and useStore hook ship together */
import { createContext, useContext, useState, type PropsWithChildren } from 'react'
import RootStore from './rootStore'

const StoreContext = createContext<RootStore | null>(null)

export function StoreProvider({ children }: PropsWithChildren) {
    const [store] = useState(() => new RootStore())

    return <StoreContext.Provider value={store}>{children}</StoreContext.Provider>
}

export function useStore() {
    const store = useContext(StoreContext)

    if (!store) {
        throw new Error('useStore must be used within a StoreProvider')
    }

    return store
}