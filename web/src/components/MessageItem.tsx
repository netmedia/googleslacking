import { memo, type ReactNode } from 'react'
import type { Message } from '../api'
import { Avatar } from './Avatar'

interface Props {
  message: Message
  compact: boolean
  replyCount: number
  onOpenThread?: () => void
  showDayDivider: boolean
}

const time = (iso: string) => new Date(iso).toLocaleTimeString('hr-HR', { hour: '2-digit', minute: '2-digit' })
const dayLabel = (iso: string) =>
  new Date(iso).toLocaleDateString('hr-HR', { weekday: 'long', day: 'numeric', month: 'long' })

export const MessageItem = memo(function MessageItem({ message: m, compact, replyCount, onOpenThread, showDayDivider }: Props) {
  const name = m.sender.displayName ?? m.sender.email ?? 'Nepoznato'
  return (
    <>
      {showDayDivider && <div className="day-divider"><span>{dayLabel(m.createTime)}</span></div>}
      <article className={`msg ${compact && !showDayDivider ? 'compact' : ''} ${m.pending ? 'pending' : ''} ${m.failed ? 'failed' : ''}`}>
        <div className="msg-gutter">
          {compact && !showDayDivider ? <time>{time(m.createTime)}</time> : <Avatar name={name} url={m.sender.avatarUrl} />}
        </div>
        <div className="msg-body">
          {!(compact && !showDayDivider) && (
            <div className="msg-meta">
              <strong>{name}</strong>
              <time>{time(m.createTime)}</time>
            </div>
          )}
          {m.isDeleted ? (
            <div className="msg-text deleted">Poruka je obrisana</div>
          ) : (
            <div className="msg-text">
              {renderText(m.text ?? '')}
              {m.lastUpdateTime && <span className="edited"> (uređeno)</span>}
            </div>
          )}
          {!!m.attachments?.length && <div className="attachments">📎 {m.attachments.length} privitak(a)</div>}
          {!!m.reactions?.length && (
            <div className="reactions">
              {m.reactions.map((r, i) => (
                <span key={i} className="reaction">
                  {r.emoji?.unicode ?? '🙂'} {r.reactionCount ?? ''}
                </span>
              ))}
            </div>
          )}
          {onOpenThread && (
            <button className="thread-link" onClick={onOpenThread}>
              {replyCount > 0 ? `${replyCount} ${replyCount === 1 ? 'odgovor' : 'odgovora'}` : 'Odgovori u niti'}
            </button>
          )}
          {m.failed && <div className="send-error">Slanje nije uspjelo.</div>}
        </div>
      </article>
    </>
  )
})

const URL_RE = /(https?:\/\/[^\s<]+)/g

/** Minimal Google Chat markup: links, *bold*, _italic_, `code`. */
function renderText(text: string): ReactNode[] {
  return text.split(URL_RE).map((part, i) => {
    if (i % 2 === 1)
      return (
        <a key={i} href={part} target="_blank" rel="noreferrer">
          {part}
        </a>
      )
    return part.split(/(\*[^*\n]+\*|_[^_\n]+_|`[^`\n]+`)/g).map((seg, j) => {
      const k = `${i}-${j}`
      if (/^\*[^*]+\*$/.test(seg)) return <strong key={k}>{seg.slice(1, -1)}</strong>
      if (/^_[^_]+_$/.test(seg)) return <em key={k}>{seg.slice(1, -1)}</em>
      if (/^`[^`]+`$/.test(seg)) return <code key={k}>{seg.slice(1, -1)}</code>
      return seg
    })
  })
}
