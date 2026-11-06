import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ActivityKind, type Me, type Member, type Message, type Space } from '../api'
import { keys } from '../cache'
import { useActivity, useMembers, usePresence } from '../hooks'
import { jumpTo, setPanel, toast } from '../store'
import { fmtRelative, fmtWhen, reminderPresets } from '../time'
import { Avatar } from './Avatar'
import { PersonPicker } from './PersonPicker'
import { RichText } from './RichText'
import { PresenceDot } from './spaceMenus'
import { Popover, WhenPicker } from './ui'

export function PanelShell({ title, children, actions }: { title: ReactNode; children: ReactNode; actions?: ReactNode }) {
  return (
    <aside className="side-panel">
      <header className="channel-header">
        <h2>{title}</h2>
        <div className="header-actions">
          {actions}
          <button className="icon" onClick={() => setPanel(null)} aria-label="Zatvori">
            ✕
          </button>
        </div>
      </header>
      <div className="panel-body">{children}</div>
    </aside>
  )
}

/** Compact message preview; click jumps to it. */
export function MessageCard({ m, me, spaceName, extra, meta }: { m: Message; me: Me; spaceName?: string; extra?: ReactNode; meta?: ReactNode }) {
  const name = m.sender.displayName ?? m.sender.email ?? 'Nepoznato'
  return (
    <div className="card" role="button" tabIndex={0} onClick={() => jumpTo(m.spaceId, m.id, m.createTime)}>
      <div className="card-meta">
        {spaceName && <span className="card-space">{spaceName}</span>}
        {meta}
        <span className="muted">{fmtRelative(m.createTime)}</span>
      </div>
      <div className="card-body">
        <Avatar name={name} url={m.sender.avatarUrl} size={28} />
        <div>
          <strong>{name}</strong>
          <div className="card-text">{m.isDeleted ? <em className="muted">obrisano</em> : <RichText text={m.text ?? ''} annotations={m.annotations} me={me.googleUserName} />}</div>
        </div>
      </div>
      {extra && (
        <div className="card-extra" onClick={(e) => e.stopPropagation()}>
          {extra}
        </div>
      )}
    </div>
  )
}

// ---------------- Search ----------------
export function SearchPanel({ me, initial }: { me: Me; initial?: string }) {
  const [input, setInput] = useState(initial ?? '')
  const [query, setQuery] = useState(initial?.trim() ?? '')
  useEffect(() => {
    setInput(initial ?? '')
    setQuery(initial?.trim() ?? '')
  }, [initial])
  useEffect(() => {
    const t = setTimeout(() => setQuery(input.trim()), 300)
    return () => clearTimeout(t)
  }, [input])
  const q = useQuery({ queryKey: keys.search(query), queryFn: () => api.search(query), enabled: query.length >= 2 })

  return (
    <PanelShell title="Pretraga">
      <div className="panel-search">
        <input autoFocus placeholder="Traži poruke…" value={input} onChange={(e) => setInput(e.target.value)} />
        <div className="search-hints">
          {['in:#', 'from:@', 'after:', 'before:', 'has:link', 'has:file', 'is:thread', 'is:pinned', 'is:saved'].map((h) => (
            <button key={h} className="chip" onClick={() => setInput((v) => (v ? v.trimEnd() + ' ' : '') + h)}>
              {h}
            </button>
          ))}
        </div>
      </div>
      {q.isFetching && <div className="loading-older">Tražim…</div>}
      {q.data?.length === 0 && <div className="empty-panel">Nema rezultata za „{query}”.</div>}
      {q.data?.map((r) => <MessageCard key={r.message.id} m={r.message} me={me} spaceName={r.spaceName} />)}
      {!query && (
        <div className="empty-panel">
          Primjer: <code>deploy in:#dev-team from:@marko after:1.10.2026</code>
        </div>
      )}
    </PanelShell>
  )
}

// ---------------- Activity ----------------
const activityLabel: Record<ActivityKind, string> = {
  0: '@ spominjanje',
  1: 'Direktna poruka',
  2: '💬 odgovor u niti',
  3: '🔑 ključna riječ',
  4: 'reakcija',
  5: '⏰ podsjetnik',
  6: 'poruka',
}

export function ActivityPanel({ me }: { me: Me }) {
  const qc = useQueryClient()
  const q = useActivity()
  const [filter, setFilter] = useState<'all' | 'mentions' | 'threads' | 'reactions'>('all')
  const items = (q.data?.items ?? []).filter(
    (i) => filter === 'all' || (filter === 'mentions' ? i.kind === 0 || i.kind === 3 : filter === 'threads' ? i.kind === 2 : i.kind === 4),
  )
  const markAll = () => api.activityRead().then(() => qc.invalidateQueries({ queryKey: keys.activity }))

  return (
    <PanelShell
      title="Aktivnost"
      actions={
        <button className="btn small" onClick={markAll} disabled={!q.data?.unread}>
          Sve pročitano
        </button>
      }
    >
      <div className="tabs">
        {(
          [
            ['all', 'Sve'],
            ['mentions', 'Spominjanja'],
            ['threads', 'Niti'],
            ['reactions', 'Reakcije'],
          ] as const
        ).map(([k, label]) => (
          <button key={k} className={filter === k ? 'on' : ''} onClick={() => setFilter(k)}>
            {label}
          </button>
        ))}
      </div>
      {items.length === 0 && !q.isLoading && <div className="empty-panel">Ovdje će se pojaviti spominjanja, odgovori u nitima i reakcije.</div>}
      {items.map((i) => {
        const read = () => !i.isRead && api.activityRead([i.id]).then(() => qc.invalidateQueries({ queryKey: keys.activity }))
        const meta = (
          <span className={`activity-kind ${i.isRead ? '' : 'unread'}`}>
            {i.kind === 4 && i.actor ? `${i.actor.displayName ?? 'Netko'} je reagirao/la ${i.preview ?? ''}` : activityLabel[i.kind]}
          </span>
        )
        return (
          <div key={i.id} onClickCapture={read}>
            {i.message ? (
              <MessageCard m={i.message} me={me} spaceName={i.spaceName} meta={meta} />
            ) : (
              <div className="card">
                <div className="card-meta">
                  <span className="card-space">{i.spaceName}</span>
                  {meta}
                </div>
                <div className="card-text">{i.preview}</div>
              </div>
            )}
          </div>
        )
      })}
    </PanelShell>
  )
}

// ---------------- Later (saved, scheduled, drafts) ----------------
export function LaterPanel({ me, tab: initialTab, spaces }: { me: Me; tab?: 'saved' | 'scheduled' | 'drafts'; spaces: Space[] }) {
  const qc = useQueryClient()
  const [tab, setTab] = useState(initialTab ?? 'saved')
  const [showDone, setShowDone] = useState(false)
  const saved = useQuery({ queryKey: keys.saved(showDone), queryFn: () => api.saved(showDone) })
  const scheduled = useQuery({ queryKey: keys.scheduled, queryFn: api.scheduled })
  const drafts = useQuery({ queryKey: keys.drafts, queryFn: api.drafts })
  const [pick, setPick] = useState<{ id: string; kind: 'saved' | 'scheduled'; anchor: HTMLElement } | null>(null)
  const spaceName = (id: string) => spaces.find((s) => s.id === id)?.displayName ?? ''

  useEffect(() => {
    setTab(initialTab ?? 'saved')
  }, [initialTab])
  const invalidate = (k: readonly unknown[]) => () => qc.invalidateQueries({ queryKey: k })

  return (
    <PanelShell title="Kasnije">
      <div className="tabs">
        <button className={tab === 'saved' ? 'on' : ''} onClick={() => setTab('saved')}>
          Spremljeno {saved.data && !showDone ? <span className="count">{saved.data.length}</span> : null}
        </button>
        <button className={tab === 'scheduled' ? 'on' : ''} onClick={() => setTab('scheduled')}>
          Zakazano {scheduled.data?.length ? <span className="count">{scheduled.data.length}</span> : null}
        </button>
        <button className={tab === 'drafts' ? 'on' : ''} onClick={() => setTab('drafts')}>
          Skice {drafts.data?.length ? <span className="count">{drafts.data.length}</span> : null}
        </button>
      </div>

      {tab === 'saved' && (
        <>
          <label className="toggle-row">
            <input type="checkbox" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} /> Prikaži završeno
          </label>
          {saved.data?.length === 0 && <div className="empty-panel">Spremi poruku (⋯ → Spremi za kasnije) ili postavi podsjetnik ⏰.</div>}
          {saved.data?.map((s) => (
            <MessageCard
              key={s.id}
              m={s.message}
              me={me}
              spaceName={s.spaceName}
              meta={s.remindAt ? <span className={`remind ${Date.parse(s.remindAt) < Date.now() ? 'due' : ''}`}>⏰ {fmtWhen(s.remindAt)}</span> : undefined}
              extra={
                <div className="card-actions">
                  <button className="btn small" onClick={() => api.updateSaved(s.id, { completed: !s.completedAt }).then(invalidate(['saved']))}>
                    {s.completedAt ? 'Vrati' : '✓ Završeno'}
                  </button>
                  <button className="btn small" onClick={(e) => setPick({ id: s.id, kind: 'saved', anchor: e.currentTarget })}>
                    ⏰ Podsjetnik
                  </button>
                  <button className="btn small" onClick={() => api.removeSaved(s.id).then(invalidate(['saved']))}>
                    Ukloni
                  </button>
                </div>
              }
            />
          ))}
        </>
      )}

      {tab === 'scheduled' && (
        <>
          {scheduled.data?.length === 0 && <div className="empty-panel">Nemaš zakazanih poruka. Zakaži preko ▾ pokraj gumba Pošalji.</div>}
          {scheduled.data?.map((s) => (
            <div key={s.id} className={`card ${s.status === 2 ? 'failed' : ''}`}>
              <div className="card-meta">
                <span className="card-space">{s.spaceName}</span>
                <span>{s.status === 2 ? `⚠️ nije poslano: ${s.error ?? ''}` : `🕒 ${fmtWhen(s.sendAt)}`}</span>
              </div>
              <div className="card-text">{s.text}</div>
              <div className="card-actions">
                <button className="btn small" onClick={() => api.updateScheduled(s.id, { sendAt: new Date().toISOString() }).then(invalidate(keys.scheduled))}>
                  Pošalji sad
                </button>
                <button className="btn small" onClick={(e) => setPick({ id: s.id, kind: 'scheduled', anchor: e.currentTarget })}>
                  Promijeni vrijeme
                </button>
                <button
                  className="btn small"
                  onClick={() => {
                    const text = prompt('Uredi poruku', s.text)
                    if (text?.trim()) api.updateScheduled(s.id, { text }).then(invalidate(keys.scheduled))
                  }}
                >
                  Uredi
                </button>
                <button className="btn small danger" onClick={() => api.cancelScheduled(s.id).then(invalidate(keys.scheduled))}>
                  Otkaži
                </button>
              </div>
            </div>
          ))}
        </>
      )}

      {tab === 'drafts' && (
        <>
          {drafts.data?.length === 0 && <div className="empty-panel">Nema nedovršenih poruka.</div>}
          {drafts.data?.map((d) => (
            <div
              key={`${d.spaceId}:${d.threadName ?? ''}`}
              className="card"
              role="button"
              tabIndex={0}
              onClick={() => {
                location.hash = `#/space/${d.spaceId}${d.threadName ? `/t/${encodeURIComponent(d.threadName)}` : ''}`
              }}
            >
              <div className="card-meta">
                <span className="card-space">{spaceName(d.spaceId)}</span>
                {d.threadName && <span>u niti</span>}
                <span className="muted">{fmtRelative(d.updatedAt)}</span>
              </div>
              <div className="card-text">✏️ {d.text}</div>
            </div>
          ))}
        </>
      )}

      {pick && (
        <Popover anchor={pick.anchor} onClose={() => setPick(null)}>
          <WhenPicker
            presets={reminderPresets}
            onPick={(d) => {
              const id = pick.id
              const kind = pick.kind
              setPick(null)
              if (kind === 'saved') api.updateSaved(id, { remindAt: d.toISOString() }).then(invalidate(['saved']))
              else api.updateScheduled(id, { sendAt: d.toISOString() }).then(invalidate(keys.scheduled))
            }}
          />
        </Popover>
      )}
    </PanelShell>
  )
}

// ---------------- Pins ----------------
export function PinsPanel({ space, me }: { space: Space; me: Me }) {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: keys.pins(space.id), queryFn: () => api.pins(space.id) })
  return (
    <PanelShell title={`Prikvačeno · ${space.displayName}`}>
      {q.data?.length === 0 && <div className="empty-panel">Nema prikvačenih poruka. Prikvači poruku preko ⋯ → Prikvači na kanal.</div>}
      {q.data?.map((m) => (
        <MessageCard
          key={m.id}
          m={m}
          me={me}
          extra={
            <button className="btn small" onClick={() => api.unpin(space.id, m.id).then(() => qc.invalidateQueries({ queryKey: keys.pins(space.id) }))}>
              Otkvači
            </button>
          }
        />
      ))}
      <p className="muted small panel-note">Prikvačene poruke vide korisnici ove aplikacije (ne službeni Google Chat).</p>
    </PanelShell>
  )
}

// ---------------- Members ----------------
export function MembersPanel({ space }: { space: Space }) {
  const qc = useQueryClient()
  const q = useMembers(space.id)
  const presence = usePresence()
  const [adding, setAdding] = useState(false)
  const [selected, setSelected] = useState<Member[]>([])
  const sorted = useMemo(
    () =>
      [...(q.data ?? [])].sort((a, b) => {
        const rank = (m: Member) => ({ active: 0, away: 1, offline: 2 })[presence.get(m.id)?.state ?? 'offline']
        return rank(a) - rank(b) || (a.displayName ?? '').localeCompare(b.displayName ?? '', 'hr')
      }),
    [q.data, presence],
  )

  const add = async () => {
    await api.addMembers(space.id, selected.map((s) => s.id))
    setSelected([])
    setAdding(false)
    qc.invalidateQueries({ queryKey: keys.members(space.id) })
    toast('Članovi dodani.')
  }

  return (
    <PanelShell
      title={`Članovi · ${q.data?.length ?? ''}`}
      actions={
        space.kind !== 2 && (
          <button className="btn small" onClick={() => setAdding((v) => !v)}>
            + Dodaj ljude
          </button>
        )
      }
    >
      {adding && (
        <div className="panel-search">
          <PersonPicker selected={selected} onChange={setSelected} exclude={(q.data ?? []).map((m) => m.id)} />
          <button className="btn primary small" disabled={!selected.length} onClick={add}>
            Dodaj {selected.length || ''}
          </button>
        </div>
      )}
      <ul className="member-list">
        {sorted.map((m) => {
          const p = presence.get(m.id)
          return (
            <li key={m.id}>
              <Avatar name={m.displayName ?? m.email ?? '?'} url={m.avatarUrl} size={32} />
              <div>
                <div>
                  {p && <PresenceDot state={p.state} />} <strong>{m.displayName ?? m.email ?? m.id}</strong> {p?.statusEmoji}
                </div>
                <div className="muted small">{p?.statusText ?? m.email}</div>
              </div>
              {m.isAppUser && <span className="chip small">GoogleSlacking</span>}
            </li>
          )
        })}
      </ul>
    </PanelShell>
  )
}
