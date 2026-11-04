import { useEffect, useState } from 'react'
import { api, type Me, type Space } from '../api'
import type { ConnectionStatus } from '../realtime'
import { Avatar } from './Avatar'

interface Props {
  me: Me
  spaces: Space[]
  activeId: string | null
  onSelect: (id: string) => void
  status: ConnectionStatus
}

const sections: { title: string; filter: (s: Space) => boolean }[] = [
  { title: 'Označeno', filter: (s) => s.isStarred },
  { title: 'Prostori', filter: (s) => !s.isStarred && s.kind === 0 },
  { title: 'Poruke', filter: (s) => !s.isStarred && s.kind !== 0 },
]

export function Sidebar({ me, spaces, activeId, onSelect, status }: Props) {
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [canNotify, setCanNotify] = useState(typeof Notification !== 'undefined' && Notification.permission === 'default')

  const totalUnread = spaces.filter((s) => !s.isMuted).reduce((n, s) => n + s.unreadCount, 0)
  useEffect(() => {
    document.title = totalUnread ? `(${totalUnread}) GoogleSlacking` : 'GoogleSlacking'
    // App badge on installed PWAs (Chrome/Edge desktop, Android, iOS 16.4+).
    const nav = navigator as Navigator & { setAppBadge?: (n?: number) => Promise<void>; clearAppBadge?: () => Promise<void> }
    if (totalUnread) nav.setAppBadge?.(totalUnread).catch(() => {})
    else nav.clearAppBadge?.().catch(() => {})
  }, [totalUnread])

  return (
    <aside className="sidebar">
      <header className="sidebar-header">
        <Avatar name={me.displayName ?? me.email} url={me.avatarUrl} size={28} />
        <div className="me">
          <strong>{me.displayName ?? me.email}</strong>
          <span className={`status ${status}`}>{status === 'online' ? 'povezano' : status === 'offline' ? 'offline' : 'spajanje…'}</span>
        </div>
      </header>

      <div className="sidebar-tools">
        <button className={`chip ${unreadOnly ? 'on' : ''}`} onClick={() => setUnreadOnly((v) => !v)}>
          Nepročitano
        </button>
        <span className="hint">Ctrl+K</span>
      </div>

      {canNotify && (
        <button className="notify-cta" onClick={() => Notification.requestPermission().then(() => setCanNotify(false))}>
          Uključi obavijesti na ovom uređaju
        </button>
      )}

      <nav className="space-list">
        {sections.map(({ title, filter }) => {
          const items = spaces.filter(filter).filter((s) => !unreadOnly || s.unreadCount > 0 || s.id === activeId)
          if (!items.length) return null
          return (
            <section key={title}>
              <h3>{title}</h3>
              {items.map((s) => (
                <button
                  key={s.id}
                  className={`space ${s.id === activeId ? 'active' : ''} ${s.unreadCount && !s.isMuted ? 'unread' : ''}`}
                  onClick={() => onSelect(s.id)}
                  title={s.displayName}
                >
                  <span className="glyph">{s.kind === 0 ? '#' : s.kind === 2 ? '●' : '◆'}</span>
                  <span className="name">{s.displayName}</span>
                  {s.unreadCount > 0 && !s.isMuted && <span className="badge">{s.unreadCount > 99 ? '99+' : s.unreadCount}</span>}
                </button>
              ))}
            </section>
          )
        })}
      </nav>

      <footer className="sidebar-footer">
        <button className="link" onClick={() => api.resync()}>Ponovna sinkronizacija</button>
        <button className="link" onClick={() => api.logout().then(() => location.reload())}>Odjava</button>
      </footer>
    </aside>
  )
}
