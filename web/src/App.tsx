import { useCallback, useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, UnauthorizedError } from './api'
import { keys } from './cache'
import { useRealtime } from './realtime'
import { Login } from './components/Login'
import { Sidebar } from './components/Sidebar'
import { Channel } from './components/Channel'
import { ThreadPanel } from './components/ThreadPanel'
import { QuickSwitcher } from './components/QuickSwitcher'

function useHashSpace(): [string | null, (id: string) => void] {
  const read = () => location.hash.match(/^#\/space\/([0-9a-f-]{36})/)?.[1] ?? null
  const [id, setId] = useState<string | null>(read)
  useEffect(() => {
    const onHash = () => setId(read())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  return [id, (next) => (location.hash = `#/space/${next}`)]
}

export function App() {
  const qc = useQueryClient()
  const me = useQuery({
    queryKey: keys.me,
    queryFn: api.me,
    // Poll while the initial sync is running on the server.
    refetchInterval: (q) => (q.state.data && !q.state.data.syncCompleted ? 3000 : false),
  })
  const spaces = useQuery({ queryKey: keys.spaces, queryFn: api.spaces, enabled: !!me.data })
  const [activeId, setActiveId] = useHashSpace()
  const [thread, setThread] = useState<string | null>(null)
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const status = useRealtime(qc, me.data, activeId)

  // When the server finishes the first sync, load the spaces it found.
  useEffect(() => {
    if (me.data?.syncCompleted) qc.invalidateQueries({ queryKey: keys.spaces })
  }, [me.data?.syncCompleted, qc])

  // Default to the most recently active space.
  useEffect(() => {
    if (!activeId && spaces.data?.length) setActiveId(spaces.data[0].id)
  }, [activeId, spaces.data, setActiveId])

  useEffect(() => setThread(null), [activeId])

  const onKey = useCallback((e: KeyboardEvent) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault()
      setSwitcherOpen((v) => !v)
    }
  }, [])
  useEffect(() => {
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onKey])

  const active = useMemo(() => spaces.data?.find((s) => s.id === activeId) ?? null, [spaces.data, activeId])

  const authError = new URLSearchParams(location.search).get('authError')
  if (me.error instanceof UnauthorizedError || authError) return <Login error={authError} />
  if (!me.data) return <div className="splash">Učitavanje…</div>

  return (
    <div className={`layout ${thread ? 'with-thread' : ''}`}>
      <Sidebar me={me.data} spaces={spaces.data ?? []} activeId={activeId} onSelect={setActiveId} status={status} />
      <main className="main">
        {!me.data.syncCompleted && <div className="banner">Prva sinkronizacija s Google Chatom je u tijeku…</div>}
        {active ? (
          <Channel key={active.id} space={active} me={me.data} onOpenThread={setThread} />
        ) : (
          <div className="empty">Odaberi razgovor</div>
        )}
      </main>
      {active && thread && <ThreadPanel space={active} me={me.data} threadName={thread} onClose={() => setThread(null)} />}
      {switcherOpen && (
        <QuickSwitcher
          spaces={spaces.data ?? []}
          onSelect={(id) => {
            setActiveId(id)
            setSwitcherOpen(false)
          }}
          onClose={() => setSwitcherOpen(false)}
        />
      )}
    </div>
  )
}
