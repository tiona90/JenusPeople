import { StrictMode, useMemo } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { observer } from 'mobx-react-lite'
import CssBaseline from '@mui/material/CssBaseline'
import { ThemeProvider } from '@mui/material/styles'
import './index.css'
import App from './App.tsx'
import { StoreProvider, useStore } from './lib/mobx'
import { queryClient } from './lib/react-query'
import { buildTheme } from './lib/theme'

const ThemedApp = observer(function ThemedApp() {
  const { uiStore } = useStore()
  const theme = useMemo(() => buildTheme(uiStore.themeMode), [uiStore.themeMode])
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  )
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StoreProvider>
      <QueryClientProvider client={queryClient}>
        <ThemedApp />
        <ReactQueryDevtools initialIsOpen={false} />
      </QueryClientProvider>
    </StoreProvider>
  </StrictMode>,
)
