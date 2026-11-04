import { useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Me, type Message, type Space } from '../api'
import { keys, setSpaceRead } from '../cache'
import { Composer } from './Composer'
import { MessageItem } from './MessageItem'

interface Props {
  space: Space
  me: Me
  onOpenThread: (threadName: string) => void
}

const GROUP_WINDOW_MS = 5 * 60_000

export function Channel({ space, me, onOpenThread }: Props) {
  const qc = useQueryClient()
  const scroller = useRef<HTMLDivElement>(null)
  const stickToBottom = useRef(true)

  const q = useInfiniteQuery({
    queryKey: keys.messages(space.id),
    queryFn: ({ pageParam }) => api.messages(space.id, pageParam),
    initialPageParam: undefined as string | undefined,
    // pages are newest-first; the next page is "older than the oldest we have"
    getNextPageParam: (last) => (last.length < 50 ? undefined : last[last.length - 1]?.createTime),
  })

  // Oldest-first for display. In threaded spaces the main timeline shows thread starters only.
  const { timeline, replyCounts } = useMemo(() => {
    const all = (q.data?.pages.flat() ?? []).slice().reverse()
    const counts = new Map<string, number>()
    for (const m of all) if (m.isThreadReply && m.threadName) counts.set(m.threadName, (counts.get(m.threadName) ?? 0) + 1)
    return { timeline: space.isThreaded ? all.filter((m) => !m.isThreadReply) : all, replyCounts: counts }
  }, [q.data, space.isThreaded])

  const newest = timeline[timeline.length - 1]

  // Keep the view pinned to the bottom when new messages arrive (unless the user scrolled up).
  useLayoutEffect(() => {
    const el = scroller.current
    if (el && stickToBottom.current) el.scrollTop = el.scrollHeight
  }, [timeline.length, newest?.googleName])

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
    if (el.scrollTop < 200 && q.hasNextPage && !q.isFetchingNextPage) {
      prevHeight.current = el.scrollHeight
      q.fetchNextPage()
    }
  }

  // Mark as read when the newest message is visible and the window is focused.
  useEffect(() => {
    if (!newest || newest.pending) return
    const mark = () => {
      if (document.visibilityState !== 'visible' || !stickToBottom.current) return
      if (space.lastReadTime && space.lastReadTime >= newest.createTime) return
      setSpaceRead(qc, space.id, newest.createTime)
      api.markRead(space.id, newest.createTime).catch(() => {})
    }
    mark()
    window.addEventListener('focus', mark)
    return () => window.removeEventListener('focus', mark)
  }, [newest, space.id, space.lastReadTime, qc])

  return (
    <section className="channel">
      <header className="channel-header">
        <h2>
          <span className="glyph">{space.kind === 0 ? '#' : ''}</span>
          {space.displayName}
        </h2>
      </header>

      <div className="messages" ref={scroller} onScroll={onScroll}>
        {q.isFetchingNextPage && <div className="loading-older">Učitavanje starijih poruka…</div>}
        {!q.hasNextPage && q.data && <div className="beginning">Početak razgovora</div>}
        {timeline.map((m, i) => (
          <MessageItem
            key={m.clientAssignedMessageId ?? m.googleName}
            message={m}
            compact={isContinuation(timeline[i - 1], m)}
            replyCount={m.threadName && space.isThreaded ? replyCounts.get(m.threadName) ?? 0 : 0}
            onOpenThread={space.isThreaded && m.threadName ? () => onOpenThread(m.threadName!) : undefined}
            showDayDivider={!timeline[i - 1] || day(timeline[i - 1]) !== day(m)}
          />
        ))}
        {q.isLoading && <div className="loading">Učitavanje…</div>}
      </div>

      <Composer space={space} me={me} placeholder={`Poruka u ${space.displayName}`} />
    </section>
  )
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
