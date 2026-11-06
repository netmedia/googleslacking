import { useEffect, useState, type DragEvent } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, type Me, type Section, type Space } from '../api'
import { keys } from '../cache'
import { useActivity, usePresence, useSettings, useSnoozed } from '../hooks'
import type { ConnectionStatus } from '../realtime'
import { setModal, setPanel, togglePanel, useUi } from '../store'
import { snoozePresets } from '../time'
import { NetmediaLogo } from '../brand'
import { Avatar } from './Avatar'
import { PresenceDot, spaceContextMenu } from './spaceMenus'
import { Menu, MenuButton, Popover } from './ui'

interface Props {
  me: Me
  spaces: Space[]
  sections: Section[]
  activeId: string | null
  onSelect: (id: string) => void
  status: ConnectionStatus
}

/** Sidebar groups in display order; also used for Alt+↑/↓ navigation. */
export function groupSpaces(spaces: Space[], sections: Section[]) {
  const byName = (a: Space, b: Space) => a.displayName.localeCompare(b.displayName, 'hr')
  const byRecent = (a: Space, b: Space) => (b.lastActiveTime ?? '').localeCompare(a.lastActiveTime ?? '')
  const inSection = (s: Space) => !!s.sectionId && sections.some((x) => x.id === s.sectionId)
  return [
    { key: 'starred', title: 'Označeno', items: spaces.filter((s) => s.isStarred).sort(byName), section: null as Section | null },
    ...sections.map((sec) => ({
      key: sec.id,
      title: sec.name,
      items: spaces.filter((s) => !s.isStarred && s.sectionId === sec.id).sort(byName),
      section: sec,
    })),
    { key: 'spaces', title: 'Prostori', items: spaces.filter((s) => !s.isStarred && !inSection(s) && s.kind === 0).sort(byName), section: null },
    { key: 'dms', title: 'Direktne poruke', items: spaces.filter((s) => !s.isStarred && !inSection(s) && s.kind !== 0).sort(byRecent), section: null },
  ]
}

export function Sidebar({ me, spaces, sections, activeId, onSelect, status }: Props) {
  const qc = useQueryClient()
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [ctx, setCtx] = useState<{ space: Space; x: number; y: number } | null>(null)
  const [dropTarget, setDropTarget] = useState<string | null>(null)
  const panel = useUi((s) => s.panel)
  const activity = useActivity().data
  const settings = useSettings().data
  const snoozed = useSnoozed()
  const presence = usePresence()
  const myPresence = presence.get(me.googleUserName)

  const totalUnread = spaces.filter((s) => !s.isMuted && (s.kind !== 0 || s.notifyLevel === 1)).reduce((n, s) => n + s.unreadCount, 0)
  const totalMentions = spaces.reduce((n, s) => n + s.mentionCount, 0)
  const badge = totalUnread + totalMentions
  useEffect(() => {
    document.title = badge ? `(${badge}) GoogleSlacking` : 'GoogleSlacking'
    // App badge on installed PWAs (Chrome/Edge desktop, Android, iOS 16.4+).
    const nav = navigator as Navigator & { setAppBadge?: (n?: number) => Promise<void>; clearAppBadge?: () => Promise<void> }
    if (badge) nav.setAppBadge?.(badge).catch(() => {})
    else nav.clearAppBadge?.().catch(() => {})
  }, [badge])

  const groups = groupSpaces(spaces, sections)
  const refresh = () => qc.invalidateQueries({ queryKey: keys.spaces })

  const onDrop = (e: DragEvent, groupKey: string, section: Section | null) => {
    e.preventDefault()
    setDropTarget(null)
    const id = e.dataTransfer.getData('text/space-id')
    const space = spaces.find((s) => s.id === id)
    if (!space) return
    const p =
      groupKey === 'starred'
        ? api.spacePrefs(id, { isStarred: true })
        : section
          ? api.spacePrefs(id, { sectionId: section.id, isStarred: false })
          : api.spacePrefs(id, { clearSection: true, isStarred: false })
    p.then(refresh)
  }

  const sectionMenu = (sec: Section) => [
    {
      label: 'Preimenuj…',
      onClick: () => {
        const name = prompt('Novi naziv sekcije', sec.name)?.trim()
        if (name) api.updateSection(sec.id, { name }).then(() => qc.invalidateQueries({ queryKey: keys.sections }))
      },
    },
    {
      label: 'Obriši sekciju',
      danger: true,
      onClick: () => {
        if (confirm(`Obrisati sekciju „${sec.name}”? Razgovori ostaju.`))
          api.deleteSection(sec.id).then(() => {
            qc.invalidateQueries({ queryKey: keys.sections })
            refresh()
          })
      },
    },
  ]

  return (
    <aside className="sidebar">
      <header className="sidebar-header">
        <span className="brand" title="Netmedia Chat">
          <NetmediaLogo height={22} />
        </span>
        <button className="icon compose" title="Novi razgovor (Ctrl+N)" onClick={() => setModal('new')}>
          ✎
        </button>
      </header>
      <div className="sidebar-me">
        <MenuButton
          className="me-button"
          title="Status i postavke"
          items={() => [
            { label: settings?.statusText ? `Status: ${settings.statusEmoji ?? ''} ${settings.statusText}` : 'Postavi status…', onClick: () => setModal('settings') },
            {
              label: settings?.forceAway ? 'Postavi se kao aktivan' : 'Postavi se kao odsutan',
              onClick: () => settings && api.saveSettings({ ...settings, forceAway: !settings.forceAway }).then(() => qc.invalidateQueries({ queryKey: keys.settings })),
            },
            {
              label: snoozed ? 'Nastavi obavijesti' : 'Pauziraj obavijesti',
              submenu: snoozed
                ? undefined
                : snoozePresets().map((p) => ({ label: p.label, onClick: () => api.snooze(p.minutes).then(() => qc.invalidateQueries({ queryKey: keys.settings })) })),
              onClick: snoozed ? () => api.snooze(null).then(() => qc.invalidateQueries({ queryKey: keys.settings })) : undefined,
            },
            { divider: true, label: '' },
            { label: 'Postavke', hint: '', onClick: () => setModal('settings') },
            { label: 'Tipkovničke prečice', hint: 'Ctrl+/', onClick: () => setModal('shortcuts') },
            { label: 'Ponovna sinkronizacija', onClick: () => api.resync() },
            { divider: true, label: '' },
            { label: 'Odjava', danger: true, onClick: () => api.logout().then(() => location.reload()) },
          ]}
        >
          <span className="avatar-wrap">
            <Avatar name={me.displayName ?? me.email} url={me.avatarUrl} size={30} />
            <PresenceDot state={myPresence?.state ?? (status === 'online' ? 'active' : 'offline')} />
          </span>
          <span className="me">
            <strong>
              {me.displayName ?? me.email} {settings?.statusEmoji}
            </strong>
            <span className={`status ${status}`}>
              {snoozed ? '🔕 obavijesti pauzirane' : status === 'online' ? settings?.statusText || 'povezano' : status === 'offline' ? 'offline' : 'spajanje…'}
            </span>
          </span>
        </MenuButton>
      </div>

      <button className="sidebar-search" onClick={() => setPanel({ type: 'search' })}>
        🔍 Pretraži <span className="hint">Ctrl+G</span>
      </button>

      <nav className="sidebar-nav">
        <button className={panel?.type === 'activity' ? 'active' : ''} onClick={() => togglePanel({ type: 'activity' })}>
          🔔 Aktivnost {activity?.unread ? <span className="badge">{activity.unread}</span> : null}
        </button>
        <button className={panel?.type === 'later' ? 'active' : ''} onClick={() => togglePanel({ type: 'later' })}>
          🔖 Kasnije
        </button>
        <button onClick={() => setPanel({ type: 'later', tab: 'scheduled' })}>🕒 Zakazano</button>
        <button onClick={() => setPanel({ type: 'later', tab: 'drafts' })}>✏️ Skice</button>
      </nav>

      <div className="sidebar-tools">
        <button className={`chip ${unreadOnly ? 'on' : ''}`} onClick={() => setUnreadOnly((v) => !v)}>
          Nepročitano
        </button>
        <button
          className="link"
          onClick={() => {
            const name = prompt('Naziv nove sekcije')?.trim()
            if (name) api.createSection(name).then(() => qc.invalidateQueries({ queryKey: keys.sections }))
          }}
        >
          + Sekcija
        </button>
      </div>

      <nav className="space-list">
        {groups.map((g) => {
          const items = g.items.filter((s) => !unreadOnly || s.unreadCount > 0 || s.mentionCount > 0 || s.id === activeId)
          const collapsed = g.section?.isCollapsed ?? false
          if (!items.length && (g.key === 'starred' || unreadOnly)) return null
          return (
            <section
              key={g.key}
              className={dropTarget === g.key ? 'drop-target' : ''}
              onDragOver={(e) => {
                if (e.dataTransfer.types.includes('text/space-id')) {
                  e.preventDefault()
                  setDropTarget(g.key)
                }
              }}
              onDragLeave={() => setDropTarget(null)}
              onDrop={(e) => onDrop(e, g.key, g.section)}
            >
              <h3>
                <button
                  className="section-toggle"
                  onClick={() => g.section && api.updateSection(g.section.id, { isCollapsed: !collapsed }).then(() => qc.invalidateQueries({ queryKey: keys.sections }))}
                >
                  {g.section ? (collapsed ? '▸ ' : '▾ ') : ''}
                  {g.title}
                </button>
                {g.section && (
                  <MenuButton className="icon tiny" title="Sekcija" items={sectionMenu(g.section)}>
                    ⋯
                  </MenuButton>
                )}
              </h3>
              {!collapsed &&
                items.map((s) => {
                  const p = s.dmUserName ? presence.get(s.dmUserName) : undefined
                  const showBadge = s.mentionCount > 0 || (s.unreadCount > 0 && !s.isMuted && (s.kind !== 0 || s.notifyLevel === 1))
                  const bold = s.unreadCount > 0 && !s.isMuted
                  return (
                    <button
                      key={s.id}
                      draggable
                      onDragStart={(e) => e.dataTransfer.setData('text/space-id', s.id)}
                      className={`space ${s.id === activeId ? 'active' : ''} ${bold ? 'unread' : ''} ${s.isMuted ? 'muted-space' : ''}`}
                      onClick={() => onSelect(s.id)}
                      onContextMenu={(e) => {
                        e.preventDefault()
                        setCtx({ space: s, x: e.clientX, y: e.clientY })
                      }}
                      title={s.displayName}
                    >
                      <span className="glyph">{s.kind === 0 ? '#' : p ? <PresenceDot state={p.state} /> : s.kind === 2 ? '●' : '◆'}</span>
                      <span className="name">{s.displayName}</span>
                      {p?.statusEmoji && <span className="status-emoji">{p.statusEmoji}</span>}
                      {s.isMuted && <span className="muted-icon">🔕</span>}
                      {showBadge && <span className="badge">{s.mentionCount > 0 ? `@${s.mentionCount}` : s.unreadCount > 99 ? '99+' : s.unreadCount}</span>}
                    </button>
                  )
                })}
              {g.key === 'dms' && !collapsed && (
                <button className="space add" onClick={() => setModal('new')}>
                  <span className="glyph">+</span>
                  <span className="name">Nova poruka</span>
                </button>
              )}
            </section>
          )
        })}
      </nav>

      {ctx && (
        <Popover anchor={{ x: ctx.x, y: ctx.y }} onClose={() => setCtx(null)}>
          <Menu items={spaceContextMenu(ctx.space, sections, qc)} onClose={() => setCtx(null)} />
        </Popover>
      )}
    </aside>
  )
}
