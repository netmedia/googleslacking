import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient } from '@tanstack/react-query'
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client'
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister'
import { get, set, del } from 'idb-keyval'
import { UnauthorizedError } from './api'
import { App } from './App'
import { typingReceived } from './typing'
import '@fontsource-variable/inter'
import '@fontsource-variable/plus-jakarta-sans'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 1000 * 60 * 60 * 24 * 7, // keep cached timelines for a week (instant start, offline reading)
      retry: (count, err) => !(err instanceof UnauthorizedError) && count < 2,
      refetchOnWindowFocus: true,
    },
  },
})

// Persist the query cache to IndexedDB so the app paints instantly from cache on start.
const persister = createAsyncStoragePersister({
  storage: { getItem: get, setItem: set, removeItem: del },
  key: 'googleslacking-cache-v1',
  throttleTime: 2000,
})

// Dev helper: simulate "… tipka" from the console, e.g. __typing(spaceId, null, 'users/2')
if (import.meta.env.DEV) (window as unknown as { __typing: typeof typingReceived }).__typing = typingReceived

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <PersistQueryClientProvider client={queryClient} persistOptions={{ persister, maxAge: 1000 * 60 * 60 * 24 * 7, buster: 'v1' }}>
      <App />
    </PersistQueryClientProvider>
  </React.StrictMode>,
)
