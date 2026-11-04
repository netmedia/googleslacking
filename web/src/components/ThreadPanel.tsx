import { useQuery } from '@tanstack/react-query'
import { api, type Me, type Space } from '../api'
import { keys } from '../cache'
import { Composer } from './Composer'
import { MessageItem } from './MessageItem'

interface Props {
  space: Space
  me: Me
  threadName: string
  onClose: () => void
}

export function ThreadPanel({ space, me, threadName, onClose }: Props) {
  const q = useQuery({
    queryKey: keys.thread(space.id, threadName),
    queryFn: () => api.messages(space.id, undefined, threadName),
  })
  const list = (q.data ?? []).slice().reverse()

  return (
    <aside className="thread-panel">
      <header className="channel-header">
        <h2>Nit</h2>
        <button className="icon" onClick={onClose} aria-label="Zatvori">✕</button>
      </header>
      <div className="messages">
        {list.map((m, i) => (
          <MessageItem
            key={m.clientAssignedMessageId ?? m.googleName}
            message={m}
            compact={i > 0 && list[i - 1].sender.id === m.sender.id}
            replyCount={0}
            showDayDivider={false}
          />
        ))}
        {q.isLoading && <div className="loading">Učitavanje…</div>}
      </div>
      <Composer space={space} me={me} threadName={threadName} placeholder="Odgovori…" />
    </aside>
  )
}
