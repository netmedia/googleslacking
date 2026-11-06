import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Me, type Message, type Space } from '../api'
import { keys, setSpaceRead, type Pages } from '../cache'
import { useMembers, usePresence, useSettings } from '../hooks'
import { composerBus, setPanel, toast, togglePanel, ui, useUi } from '../store'
import { BookmarksBar } from './BookmarksBar'
import { Composer } from './Composer'
import { MessageItem, type SeenInfo } from './MessageItem'
import { notifyMenu, PresenceDot } from './spaceMenus'
import { MenuButton } from './ui'

interface Props {
  space: Space
  me: Me
  onBack: () => void
}

const GROUP_WINDOW_MS = 5 * 60_000

export function Channel({ space, me, onBack }: Props) {
  const qc = useQueryClient()
  const scroller = useRef<HTMLDivElement>(null)
  const stickToBottom = useRef(true)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [highlightId, setHighlightId] = useState<string | null>(null)
  const [detached, setDetached] = useState(false)
  // "Novo" line: what was unread when the conversation was opened.
  const [readMarker] = useState(space.lastReadTime)
  const jump = useUi((s) => s.jumpTo)
  const presence = usePresence()
  const savedIds = useSavedIds()
  const pins = useQuery({ queryKey: keys.pins(space.id), queryFn: () => api.pins(space.id), staleTime: 60_000 })

  const q = useInfiniteQuery({
    queryKey: keys.messages(space.id),
    queryFn: ({ pageParam }) => api.messages(space.id, pageParam),
    initialPageParam: undefined as string | undefined,
    // pages are newest-first; the next page is "older than the oldest we have"
    getNextPageParam: (last) => (last.length < 50 ? undefined : last[last.length - 1]?.createTime),
  })

  // Oldest-first for display. Thread replies live in the thread panel; the timeline shows reply counts.
  const { timeline, threads } = useMemo(() => {
    const all = (q.data?.pages.flat() ?? []).slice().reverse()
    const t = new Map<string, { count: number; last: string }>()
    for (const m of all)
      if (m.isThreadReply && m.threadName) {
        const cur = t.get(m.threadName)
        t.set(m.threadName, { count: (cur?.count ?? 0) + 1, last: m.createTime })
      }
    return { timeline: all.filter((m) => !m.isThreadReply), threads: t }
  }, [q.data])

  const newest = timeline[timeline.length - 1]
  const seen = useSeen(space, me, timeline)
  const firstUnread = useMemo(
    () => (readMarker ? timeline.find((m) => Date.parse(m.createTime) > Date.parse(readMarker) && m.sender.id !== me.googleUserName && !m.pending) : undefined),
    [timeline, readMarker, me.googleUserName],
  )

  // Initial position: first unread ("Novo") if there is one, otherwise the bottom.
  const positioned = useRef(false)
  useLayoutEffect(() => {
    const el = scroller.current
    if (!el || !q.data || positioned.current) return
    positioned.current = true
    const marker = document.getElementById('new-divider')
    if (marker) {
      el.scrollTop = marker.offsetTop - 60
      stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80
    } else el.scrollTop = el.scrollHeight
  }, [q.data])

  // Keep pinned to the bottom when new messages arrive (unless the user scrolled up).
  useLayoutEffect(() => {
    const el = scroller.current
    if (el && stickToBottom.current && positioned.current && !detached) el.scrollTop = el.scrollHeight
  }, [timeline.length, newest?.googleName, detached])

  // Preserve scroll position when older pages are prepended.
  const prevHeight = useRef(0)
  useLayoutEffect(() => {
    const el = scroller.current
    if (!el || !prevHeight.current) return
    el.scrollTop += el.scrollHeight - prevHeight.current
    prevHeight.current = 0
  }, [q.data?.pages.length])

  const onScroll = () => {
    const el = scroller.current!
    stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80
    if (el.scrollTop < 300 && q.hasNextPage && !q.isFetchingNextPage) {
      prevHeight.current = el.scrollHeight
      q.fetchNextPage()
    }
    if (stickToBottom.current) markRead()
  }

  const markRead = () => {
    if (!newest || newest.pending || document.visibilityState !== 'visible') return
    const current = qc.getQueryData<Space[]>(keys.spaces)?.find((s) => s.id === space.id)
    if (current?.lastReadTime && current.lastReadTime >= newest.createTime && !current.unreadCount) return
    setSpaceRead(qc, space.id, newest.createTime)
    api.markRead(space.id, newest.createTime).catch(() => {})
  }

  // Mark as read when the newest message is visible and the window is focused.
  useEffect(() => {
    if (stickToBottom.current) markRead()
    const onFocus = () => stickToBottom.current && markRead()
    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [newest?.googleName, space.id])

  // Esc = mark read, jump to bottom.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || e.shiftKey || ui.get().modal || (e.target as HTMLElement)?.closest?.('.popover, .inline-editor')) return
      const el = scroller.current
      if (el) el.scrollTop = el.scrollHeight
      stickToBottom.current = true
      markRead()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [newest?.googleName])

  // Jump to a message (search result, link, activity).
  useEffect(() => {
    if (!jump || jump.spaceId !== space.id) return
    ui.set((s) => ({ ...s, jumpTo: null }))
    const reveal = () =>
      requestAnimationFrame(() => {
        const el = document.getElementById(`msg-${jump.messageId}`)
        if (el) {
          stickToBottom.current = false
          el.scrollIntoView({ block: 'center' })
          setHighlightId(jump.messageId)
          setTimeout(() => setHighlightId(null), 2500)
        }
      })
    const loaded = q.data?.pages.flat().find((m) => m.id === jump.messageId)
    if (loaded) {
      if (loaded.isThreadReply && loaded.threadName) setPanel({ type: 'thread', threadName: loaded.threadName })
      else reveal()
      return
    }
    // Not in the loaded window: load messages around it.
    api.messagesAround(space.id, jump.createTime).then((around) => {
      const target = around.find((m) => m.id === jump.messageId)
      if (target?.isThreadReply && target.threadName) {
        setPanel({ type: 'thread', threadName: target.threadName })
        return
      }
      setDetached(true)
      qc.setQueryData<Pages>(keys.messages(space.id), { pages: [around], pageParams: [undefined] })
      reveal()
    }, () => toast('Poruka nije pronađena.', 'error'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jump, space.id])

  const backToLatest = async () => {
    setDetached(false)
    stickToBottom.current = true
    positioned.current = false
    await qc.resetQueries({ queryKey: keys.messages(space.id) })
  }

  const editLast = () => {
    const last = [...(q.data?.pages.flat() ?? [])].find((m) => m.sender.id === me.googleUserName && !m.isDeleted && !m.pending && !m.isThreadReply)
    if (last) setEditingId(last.id)
  }

  useEffect(
    () =>
      composerBus.on((e) => {
        if (e.type === 'editLast' && e.spaceId === space.id) editLast()
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [space.id, q.data],
  )

  const dm = space.dmUserName ? presence.get(space.dmUserName) : undefined

  return (
    <section className="channel">
      <header className="channel-header">
        <h2>
          <button className="back" onClick={onBack} aria-label="Natrag">
            ‹
          </button>
          {space.kind === 0 ? <span className="glyph">#</span> : dm && <PresenceDot state={dm.state} />}
          <span className="title-text">{space.displayName}</span>
          {dm?.statusEmoji && (
            <span className="status-emoji" title={dm.statusText ?? ''}>
              {dm.statusEmoji}
            </span>
          )}
          <button
            className={`icon star ${space.isStarred ? 'on' : ''}`}
            title={space.isStarred ? 'Ukloni zvjezdicu' : 'Označi zvjezdicom'}
            onClick={() => api.spacePrefs(space.id, { isStarred: !space.isStarred }).then(() => qc.invalidateQueries({ queryKey: keys.spaces }))}
          >
            {space.isStarred ? '★' : '☆'}
          </button>
        </h2>
        {dm?.statusText && <span className="header-status muted">{dm.statusText}</span>}
        <div className="header-actions">
          <button className="icon" title="Članovi" onClick={() => togglePanel({ type: 'members' })}>
            👥
          </button>
          <button className="icon" title="Prikvačene poruke" onClick={() => togglePanel({ type: 'pins' })}>
            📌{pins.data?.length ? <span className="count">{pins.data.length}</span> : null}
          </button>
          <button className="icon" title="Pretraži u razgovoru" onClick={() => setPanel({ type: 'search', query: `in:#${space.displayName} ` })}>
            🔍
          </button>
          <MenuButton className="icon" title="Obavijesti" align="right" items={() => notifyMenu(space, qc)}>
            {space.isMuted ? '🔕' : '🔔'}
          </MenuButton>
        </div>
      </header>
      <BookmarksBar spaceId={space.id} />

      <div className="messages" ref={scroller} onScroll={onScroll}>
        {q.isFetchingNextPage && <div className="loading-older">Učitavanje starijih poruka…</div>}
        {!q.hasNextPage && q.data && !detached && <ConversationStart space={space} />}
        {timeline.map((m, i) => {
          const prev = timeline[i - 1]
          const thread = m.threadName ? threads.get(m.threadName) : undefined
          return (
            <MessageItem
              key={m.clientAssignedMessageId ?? m.googleName}
              message={m}
              me={me}
              space={space}
              compact={isContinuation(prev, m)}
              replyCount={thread?.count ?? 0}
              lastReplyTime={thread?.last}
              onOpenThread={m.threadName && !m.pending ? () => setPanel({ type: 'thread', threadName: m.threadName! }) : undefined}
              showDayDivider={!prev || day(prev) !== day(m)}
              showNewDivider={m === firstUnread}
              highlighted={highlightId === m.id}
              editing={editingId === m.id}
              onEdit={(on) => setEditingId(on ? m.id : null)}
              savedId={savedIds.get(m.id)}
              seen={seen && m.id === seen.messageId ? seen.info : undefined}
            />
          )
        })}
        {q.isLoading && <div className="loading">Učitavanje…</div>}
      </div>

      {detached && (
        <button className="jump-latest" onClick={backToLatest}>
          ↓ Prikaži najnovije poruke
        </button>
      )}

      <Composer space={space} me={me} placeholder={`Poruka u ${space.kind === 0 ? '#' : ''}${space.displayName}`} onEditLast={editLast} />
    </section>
  )
}

function ConversationStart({ space }: { space: Space }) {
  return (
    <div className="beginning">
      <strong>{space.kind === 0 ? `#${space.displayName}` : space.displayName}</strong>
      <span>Ovo je početak razgovora.</span>
    </div>
  )
}

/**
 * Read receipts for my latest message (reciprocal opt-in, app users only).
 * DM: "Viđeno ✓✓ 14:32"; groups/spaces: avatars of who has read it.
 */
function useSeen(space: Space, me: Me, timeline: Message[]): { messageId: string; info: SeenInfo } | null {
  const settings = useSettings().data
  const enabled = !!settings?.shareReadReceipts
  const receipts = useQuery({
    queryKey: keys.receipts(space.id),
    queryFn: () => api.readReceipts(space.id),
    enabled,
    staleTime: 30_000,
  })
  const members = useMembers(enabled ? space.id : null).data
  return useMemo(() => {
    const r = receipts.data
    if (!enabled || !r?.enabled) return null
    // Latest of my messages in the main timeline (timeline is oldest-first).
    const mine = [...timeline].reverse().find((m) => m.sender.id === me.googleUserName && !m.pending && !m.isDeleted)
    if (!mine) return null
    const t = Date.parse(mine.createTime)
    const readers = r.readers.filter((x) => Date.parse(x.lastReadTime) >= t)
    // DM where the other person doesn't share: show nothing (don't reveal that they opted out).
    if (space.kind === 2 && r.readers.length === 0) return null
    const byId = new Map((members ?? []).map((m) => [m.id, m]))
    return {
      messageId: mine.id,
      info: {
        dm: space.kind === 2,
        readers: readers.map((x) => ({ name: byId.get(x.userName)?.displayName ?? byId.get(x.userName)?.email ?? 'Netko', avatarUrl: byId.get(x.userName)?.avatarUrl ?? null })),
        pending: r.readers.length - readers.length,
        unknown: r.unknownCount,
      },
    }
  }, [receipts.data, enabled, timeline, me.googleUserName, space.kind, members])
}

/** messageId -> savedItemId for the "Later" list (to show the saved state and toggle it). */
export function useSavedIds() {
  const saved = useQuery({ queryKey: keys.saved(false), queryFn: () => api.saved(false), staleTime: 60_000 })
  return useMemo(() => new Map((saved.data ?? []).map((s) => [s.message.id, s.id] as const)), [saved.data])
}

const day = (m: Message) => new Date(m.createTime).toDateString()

function isContinuation(prev: Message | undefined, m: Message) {
  return (
    !!prev &&
    prev.sender.id === m.sender.id &&
    day(prev) === day(m) &&
    Date.parse(m.createTime) - Date.parse(prev.createTime) < GROUP_WINDOW_MS
  )
}
