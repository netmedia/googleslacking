import { useCallback, useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, newClientId, UnauthorizedError, type Message } from './api'
import { keys, setSpaceRead } from './cache'
import { useSections } from './hooks'
import { resyncPush } from './push'
import { useRealtime } from './realtime'
import { setModal, setPanel, toast, togglePanel, ui, useUi } from './store'
import { Channel } from './components/Channel'
import { Login } from './components/Login'
import { NewConversationModal, SettingsModal, ShortcutsModal } from './components/Modals'
import { ActivityPanel, LaterPanel, MembersPanel, PinsPanel, SearchPanel } from './components/Panels'
import { QuickSwitcher } from './components/QuickSwitcher'
import { groupSpaces, Sidebar } from './components/Sidebar'
import { ThreadPanel } from './components/ThreadPanel'
import { Toasts } from './components/ui'

/** Routes: #/space/{id} · #/space/{id}/t/{threadName} · #/space/{id}/m/{messageId} */
function parseHash() {
  const m = location.hash.match(/^#\/space\/([0-9a-f-]{36})(?:\/(t|m)\/(.+))?/)
  return m ? { spaceId: m[1], kind: m[2] as 't' | 'm' | undefined, arg: m[3] ? decodeURIComponent(m[3]) : undefined } : null
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
  const sections = useSections()
  const [activeId, setActiveIdState] = useState<string | null>(() => parseHash()?.spaceId ?? null)
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const panel = useUi((s) => s.panel)
  const modal = useUi((s) => s.modal)
  const forward = useUi((s) => s.forward)
  const status = useRealtime(qc, me.data, activeId)

  const setActiveId = useCallback((id: string) => {
    location.hash = `#/space/${id}`
  }, [])

  // Hash routing (also used by notifications and links).
  useEffect(() => {
    const apply = () => {
      const r = parseHash()
      setActiveIdState(r?.spaceId ?? null)
      if (!r) return
      if (r.kind === 't' && r.arg) setPanel({ type: 'thread', threadName: r.arg })
      else if (ui.get().panel?.type === 'thread') setPanel(null)
      if (r.kind === 'm' && r.arg) {
        const messageId = r.arg
        api.message(messageId).then(
          (m) => ui.set((s) => ({ ...s, jumpTo: { spaceId: m.spaceId, messageId: m.id, createTime: m.createTime } })),
          () => toast('Poruka nije dostupna.', 'error'),
        )
        history.replaceState(null, '', `#/space/${r.spaceId}`)
      }
    }
    apply()
    window.addEventListener('hashchange', apply)
    return () => window.removeEventListener('hashchange', apply)
  }, [])

  // Service worker asks us to navigate (notification click on an open window).
  useEffect(() => {
    const onMsg = (e: MessageEvent) => {
      if (e.data?.type === 'navigate' && typeof e.data.url === 'string') location.hash = e.data.url.replace(/^\/?#?/, '#').replace(/^##/, '#')
    }
    navigator.serviceWorker?.addEventListener('message', onMsg)
    return () => navigator.serviceWorker?.removeEventListener('message', onMsg)
  }, [])

  useEffect(() => {
    if (me.data) void resyncPush()
  }, [me.data])

  // When the server finishes the first sync, load the spaces it found.
  useEffect(() => {
    if (me.data?.syncCompleted) qc.invalidateQueries({ queryKey: keys.spaces })
  }, [me.data?.syncCompleted, qc])

  // Desktop: default to the most recently active space. Mobile starts on the space list.
  useEffect(() => {
    if (!activeId && spaces.data?.length && window.matchMedia('(min-width: 761px)').matches) setActiveId(spaces.data[0].id)
  }, [activeId, spaces.data, setActiveId])

  const ordered = useMemo(
    () => groupSpaces(spaces.data ?? [], sections.data ?? []).flatMap((g) => (g.section?.isCollapsed ? [] : g.items)),
    [spaces.data, sections.data],
  )

  // Global keyboard shortcuts.
  useEffect(() => {
    const move = (dir: 1 | -1, unreadOnly: boolean) => {
      const list = unreadOnly ? ordered.filter((s) => s.id === activeId || s.unreadCount > 0 || s.mentionCount > 0) : ordered
      if (!list.length) return
      const i = list.findIndex((s) => s.id === activeId)
      const next = list[(i + dir + list.length) % list.length]
      if (next && next.id !== activeId) setActiveId(next.id)
    }
    const onKey = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey
      const k = e.key.toLowerCase()
      if (mod && k === 'k') {
        e.preventDefault()
        setSwitcherOpen((v) => !v)
      } else if (mod && k === 'g') {
        e.preventDefault()
        setPanel({ type: 'search' })
      } else if (mod && k === '/') {
        e.preventDefault()
        setModal('shortcuts')
      } else if (mod && e.shiftKey && k === 'a') {
        e.preventDefault()
        togglePanel({ type: 'activity' })
      } else if (mod && e.shiftKey && k === 'l') {
        e.preventDefault()
        togglePanel({ type: 'later' })
      } else if (mod && !e.shiftKey && k === 'n') {
        e.preventDefault()
        setModal('new')
      } else if (e.altKey && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
        e.preventDefault()
        move(e.key === 'ArrowUp' ? -1 : 1, e.shiftKey)
      } else if (e.key === 'Escape' && e.shiftKey && !ui.get().modal) {
        e.preventDefault()
        for (const s of spaces.data ?? []) if (s.unreadCount && s.lastActiveTime) setSpaceRead(qc, s.id, s.lastActiveTime)
        api.readAll().then(() => toast('Sve označeno pročitanim.'))
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [ordered, activeId, setActiveId, spaces.data, qc])

  const active = useMemo(() => spaces.data?.find((s) => s.id === activeId) ?? null, [spaces.data, activeId])

  const doForward = async (target: string, m: Message) => {
    ui.set((s) => ({ ...s, forward: null }))
    const from = m.sender.displayName ?? m.sender.email ?? 'Netko'
    const text = `↪ Proslijeđeno od ${from}:\n` + (m.text ?? '').split('\n').map((l) => `> ${l}`).join('\n')
    try {
      await api.send(target, text, newClientId())
      toast('Poruka proslijeđena.')
    } catch {
      toast('Prosljeđivanje nije uspjelo.', 'error')
    }
  }

  const authError = new URLSearchParams(location.search).get('authError')
  if (me.error instanceof UnauthorizedError || authError) return <Login error={authError} />
  if (!me.data) return <div className="splash">Učitavanje…</div>

  const sideOpen = !!panel && (panel.type !== 'thread' && panel.type !== 'pins' && panel.type !== 'members' ? true : !!active)

  return (
    <div className={`layout ${sideOpen ? 'with-panel' : ''} ${active ? 'has-space' : ''}`}>
      <Sidebar me={me.data} spaces={spaces.data ?? []} sections={sections.data ?? []} activeId={activeId} onSelect={setActiveId} status={status} />
      <main className="main">
        {!me.data.syncCompleted && <div className="banner">Prva sinkronizacija s Google Chatom je u tijeku…</div>}
        {active ? (
          <Channel key={active.id} space={active} me={me.data} onBack={() => (location.hash = '')} />
        ) : (
          <div className="empty">Odaberi razgovor</div>
        )}
      </main>

      {panel?.type === 'thread' && active && <ThreadPanel key={panel.threadName} space={active} me={me.data} threadName={panel.threadName} />}
      {panel?.type === 'search' && <SearchPanel me={me.data} initial={panel.query} />}
      {panel?.type === 'activity' && <ActivityPanel me={me.data} />}
      {panel?.type === 'later' && <LaterPanel me={me.data} tab={panel.tab} spaces={spaces.data ?? []} />}
      {panel?.type === 'pins' && active && <PinsPanel space={active} me={me.data} />}
      {panel?.type === 'members' && active && <MembersPanel space={active} />}

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
      {forward && (
        <QuickSwitcher
          title="Proslijedi poruku"
          placeholder="Kome proslijediti?"
          spaces={spaces.data ?? []}
          onSelect={(id) => doForward(id, forward)}
          onClose={() => ui.set((s) => ({ ...s, forward: null }))}
        />
      )}
      {modal === 'settings' && <SettingsModal />}
      {modal === 'new' && <NewConversationModal onCreated={setActiveId} />}
      {modal === 'shortcuts' && <ShortcutsModal />}
      <Toasts />
    </div>
  )
}
