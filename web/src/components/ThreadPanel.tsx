import { useEffect, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, type Me, type Space } from '../api'
import { keys } from '../cache'
import { setPanel } from '../store'
import { useSavedIds } from './Channel'
import { Composer } from './Composer'
import { MessageItem } from './MessageItem'

export function ThreadPanel({ space, me, threadName }: { space: Space; me: Me; threadName: string }) {
  const q = useQuery({
    queryKey: keys.thread(space.id, threadName),
    queryFn: () => api.messages(space.id, undefined, threadName),
  })
  const savedIds = useSavedIds()
  const list = (q.data ?? []).slice().reverse()
  const end = useRef<HTMLDivElement>(null)
  useEffect(() => {
    end.current?.scrollIntoView({ block: 'end' }) // newer Chromium returns a Promise: keep the effect body void
  }, [list.length])

  return (
    <aside className="side-panel">
      <header className="channel-header">
        <h2>
          Nit <span className="muted small">· {space.displayName}</span>
        </h2>
        <button className="icon" onClick={() => setPanel(null)} aria-label="Zatvori">
          ✕
        </button>
      </header>
      <div className="messages">
        {list.map((m, i) => (
          <div key={m.clientAssignedMessageId ?? m.googleName} className={i === 0 ? 'thread-root' : ''}>
            <MessageItem
              message={m}
              me={me}
              space={space}
              compact={i > 1 && list[i - 1].sender.id === m.sender.id}
              replyCount={0}
              showDayDivider={false}
              savedId={savedIds.get(m.id)}
              inThread
            />
            {i === 0 && list.length > 1 && <div className="thread-count">{list.length - 1} odgovora</div>}
          </div>
        ))}
        {q.isLoading && <div className="loading">Učitavanje…</div>}
        <div ref={end} />
      </div>
      <Composer space={space} me={me} threadName={threadName} placeholder="Odgovori…" />
    </aside>
  )
}
