import { memo, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, messageLink, type Message, type Me, type Space } from '../api'
import { keys, upsertMessages } from '../cache'
import { composerBus, toast, ui } from '../store'
import { fmtDay, fmtTime, fmtWhen, reminderPresets } from '../time'
import { Avatar } from './Avatar'
import { EmojiPicker, recordRecentEmoji } from './EmojiPicker'
import { LinkPreviewCard } from './LinkPreviewCard'
import { firstUrl, RichText } from './RichText'
import { Menu, Popover, WhenPicker, type MenuItem } from './ui'

export interface MessageItemProps {
  message: Message
  me: Me
  space: Space
  compact: boolean
  replyCount: number
  lastReplyTime?: string
  onOpenThread?: () => void
  showDayDivider: boolean
  showNewDivider?: boolean
  highlighted?: boolean
  editing?: boolean
  onEdit?: (editing: boolean) => void
  savedId?: string
  /** Thread panel: hide "reply in thread" etc. */
  inThread?: boolean
  /** Read receipts, shown under my latest message only. */
  seen?: SeenInfo
}

export interface SeenInfo {
  dm: boolean
  readers: { name: string; avatarUrl: string | null }[]
  /** Sharing members who haven't read it yet. */
  pending: number
  /** Members we can't know about. */
  unknown: number
}

const QUICK = ['✅', '👀', '🙌']

export const MessageItem = memo(function MessageItem(props: MessageItemProps) {
  const { message: m, me, space, compact, replyCount, lastReplyTime, onOpenThread, showDayDivider, showNewDivider, highlighted, editing, onEdit, savedId, inThread, seen } = props
  const qc = useQueryClient()
  const [pop, setPop] = useState<{ kind: 'emoji' | 'more' | 'remind'; anchor: HTMLElement } | null>(null)
  const name = m.sender.displayName ?? m.sender.email ?? 'Nepoznato'
  const mine = m.sender.id === me.googleUserName
  const showHeader = !(compact && !showDayDivider && !showNewDivider)

  const apply = (updated: Message | undefined) => updated && upsertMessages(qc, space.id, [updated])
  const fail = (what: string) => () => toast(`${what} nije uspjelo.`, 'error')

  const toggleReaction = (emoji: string) => {
    if (m.pending) return
    recordRecentEmoji(emoji)
    const has = m.reactions.some((r) => r.emoji === emoji && r.users.includes(me.googleUserName))
    ;(has ? api.unreact(m.id, emoji) : api.react(m.id, emoji)).then(apply, fail('Reakcija'))
  }

  const save = (remindAt?: Date) =>
    api.save(m.id, remindAt?.toISOString()).then(() => {
      qc.invalidateQueries({ queryKey: ['saved'] })
      toast(remindAt ? `Podsjetit ću te ${fmtWhen(remindAt.toISOString())}.` : 'Spremljeno za kasnije.')
    }, fail('Spremanje'))

  const moreItems = (): MenuItem[] => [
    ...(!inThread && onOpenThread ? [{ label: 'Odgovori u niti', onClick: onOpenThread }] : []),
    {
      label: 'Citiraj',
      onClick: () =>
        composerBus.emit({
          type: 'insert',
          spaceId: space.id,
          threadName: inThread ? m.threadName : null,
          text: (m.text ?? '').split('\n').map((l) => `> ${l}`).join('\n') + `\n— ${name}\n`,
        }),
    },
    { label: 'Proslijedi…', onClick: () => ui.set((s) => ({ ...s, forward: m })) },
    { label: 'Kopiraj link', onClick: () => navigator.clipboard.writeText(messageLink(space.id, m.id)).then(() => toast('Link kopiran.')) },
    { label: 'Kopiraj tekst', onClick: () => navigator.clipboard.writeText(m.text ?? '').then(() => toast('Tekst kopiran.')) },
    { divider: true, label: '' },
    savedId
      ? { label: 'Ukloni iz „Kasnije”', onClick: () => api.removeSaved(savedId).then(() => qc.invalidateQueries({ queryKey: ['saved'] })) }
      : { label: 'Spremi za kasnije', onClick: () => save() },
    {
      label: 'Podsjeti me',
      submenu: reminderPresets.map((p) => ({ label: p.label, onClick: () => save(p.at()) })),
    },
    m.isPinned
      ? { label: 'Otkvači s kanala', onClick: () => api.unpin(space.id, m.id).then(() => qc.invalidateQueries({ queryKey: keys.pins(space.id) })) }
      : { label: 'Prikvači na kanal', onClick: () => api.pin(space.id, m.id).then(() => qc.invalidateQueries({ queryKey: keys.pins(space.id) })) },
    ...(mine
      ? [
          { divider: true, label: '' },
          { label: 'Uredi', hint: '↑', onClick: () => onEdit?.(true) },
          {
            label: 'Obriši',
            danger: true,
            onClick: () => {
              if (confirm('Obrisati poruku? Ovo se ne može poništiti.'))
                api.remove(m.id).then(() => apply({ ...m, isDeleted: true, text: null, reactions: [], attachments: null }), fail('Brisanje'))
            },
          },
        ]
      : []),
  ]

  const url = !m.attachments?.length && !m.isDeleted ? firstUrl(m.text) : null

  return (
    <>
      {showDayDivider && (
        <div className="day-divider">
          <span>{fmtDay(m.createTime)}</span>
        </div>
      )}
      {showNewDivider && (
        <div className="new-divider" id="new-divider">
          <span>Novo</span>
        </div>
      )}
      <article
        id={`msg-${m.id}`}
        className={`msg ${showHeader ? '' : 'compact'} ${m.pending ? 'pending' : ''} ${m.failed ? 'failed' : ''} ${highlighted ? 'highlighted' : ''} ${m.isPinned ? 'pinned' : ''} ${pop ? 'menu-open' : ''}`}
      >
        <div className="msg-gutter">{showHeader ? <Avatar name={name} url={m.sender.avatarUrl} /> : <time>{fmtTime(m.createTime)}</time>}</div>
        <div className="msg-body">
          {m.isPinned && <div className="pinned-label">📌 Prikvačeno</div>}
          {savedId && <div className="saved-label">🔖 Spremljeno za kasnije</div>}
          {showHeader && (
            <div className="msg-meta">
              <strong>{name}</strong>
              <time title={new Date(m.createTime).toLocaleString('hr-HR')}>{fmtTime(m.createTime)}</time>
            </div>
          )}

          {m.isDeleted ? (
            <div className="msg-text deleted">Poruka je obrisana</div>
          ) : editing ? (
            <InlineEditor
              initial={m.text ?? ''}
              onCancel={() => onEdit?.(false)}
              onSave={(text) => {
                onEdit?.(false)
                if (text.trim() && text !== m.text) {
                  apply({ ...m, text, lastUpdateTime: new Date().toISOString() })
                  api.edit(m.id, text).then(apply, fail('Uređivanje'))
                }
              }}
            />
          ) : (
            <div className="msg-text">
              {m.text && <RichText text={m.text} annotations={m.annotations} me={me.googleUserName} />}
              {m.lastUpdateTime && <span className="edited"> (uređeno)</span>}
            </div>
          )}

          {!!m.attachments?.length && <Attachments message={m} />}
          {url && <LinkPreviewCard url={url} />}

          {m.reactions.length > 0 && (
            <div className="reactions">
              {m.reactions.map((r) => (
                <button
                  key={r.emoji}
                  className={`reaction ${r.users.includes(me.googleUserName) ? 'mine' : ''}`}
                  onClick={() => toggleReaction(r.emoji)}
                  title={r.users.length ? `${r.users.length} osoba` : undefined}
                >
                  {r.emoji.startsWith('custom:') ? '⭐' : r.emoji} {r.count}
                </button>
              ))}
              <button className="reaction add" onClick={(e) => setPop({ kind: 'emoji', anchor: e.currentTarget })} aria-label="Dodaj reakciju">
                ☺+
              </button>
            </div>
          )}

          {onOpenThread && replyCount > 0 && !inThread && (
            <button className="thread-link" onClick={onOpenThread}>
              {replyCount} {replyCount === 1 ? 'odgovor' : replyCount < 5 ? 'odgovora' : 'odgovora'}
              {lastReplyTime && <span className="muted"> · zadnji {fmtWhen(lastReplyTime)}</span>}
            </button>
          )}
          {m.failed && <div className="send-error">Slanje nije uspjelo.</div>}
          {seen && <SeenLine seen={seen} />}
        </div>

        {!m.isDeleted && !m.pending && !editing && (
          <div className="msg-actions" role="toolbar">
            {QUICK.map((e) => (
              <button key={e} onClick={() => toggleReaction(e)} title={`Reagiraj ${e}`}>
                {e}
              </button>
            ))}
            <button title="Dodaj reakciju" onClick={(e) => setPop({ kind: 'emoji', anchor: e.currentTarget })}>
              ☺
            </button>
            {onOpenThread && !inThread && (
              <button title="Odgovori u niti" onClick={onOpenThread}>
                💬
              </button>
            )}
            <button title="Podsjeti me" onClick={(e) => setPop({ kind: 'remind', anchor: e.currentTarget })}>
              ⏰
            </button>
            <button title="Više" onClick={(e) => setPop({ kind: 'more', anchor: e.currentTarget })}>
              ⋯
            </button>
          </div>
        )}
      </article>

      {pop && (
        <Popover anchor={pop.anchor} onClose={() => setPop(null)} align="right">
          {pop.kind === 'emoji' && (
            <EmojiPicker
              onPick={(e) => {
                setPop(null)
                toggleReaction(e)
              }}
            />
          )}
          {pop.kind === 'more' && <Menu items={moreItems()} onClose={() => setPop(null)} />}
          {pop.kind === 'remind' && (
            <WhenPicker
              presets={reminderPresets}
              onPick={(d) => {
                setPop(null)
                save(d)
              }}
            />
          )}
        </Popover>
      )}
    </>
  )
})

function SeenLine({ seen }: { seen: SeenInfo }) {
  if (seen.dm)
    return (
      <div className={`seen ${seen.readers.length ? 'read' : ''}`}>
        {/* No time: Google's lastReadTime is the time of the last read message, not when it was seen. */}
        {seen.readers.length ? '✓✓ Viđeno' : '✓ Dostavljeno'}
      </div>
    )
  const names = seen.readers.map((r) => r.name)
  const title =
    (names.length ? `Vidjeli: ${names.join(', ')}` : 'Još nitko nije vidio') +
    (seen.pending ? `\nJoš nisu vidjeli: ${seen.pending}` : '') +
    (seen.unknown ? `\nNepoznato (ne dijele ili ne koriste aplikaciju): ${seen.unknown}` : '')
  return (
    <div className="seen group" title={title}>
      {seen.readers.length > 0 ? (
        <>
          <span className="seen-avatars">
            {seen.readers.slice(0, 5).map((r, i) => (
              <Avatar key={i} name={r.name} url={r.avatarUrl} size={16} />
            ))}
          </span>
          {seen.readers.length > 5 ? `+${seen.readers.length - 5} ` : ''}
          Vidjeli {seen.readers.length === 1 ? names[0] : `${seen.readers.length}`}
        </>
      ) : (
        '✓ Dostavljeno'
      )}
      {seen.unknown > 0 && <span className="muted"> · {seen.unknown} nepoznato</span>}
    </div>
  )
}

function InlineEditor({ initial, onSave, onCancel }: { initial: string; onSave: (t: string) => void; onCancel: () => void }) {
  const [text, setText] = useState(initial)
  const ref = useRef<HTMLTextAreaElement>(null)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.focus()
    el.setSelectionRange(el.value.length, el.value.length)
    el.style.height = 'auto'
    el.style.height = el.scrollHeight + 'px'
  }, [])
  return (
    <div className="inline-editor">
      <textarea
        ref={ref}
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          e.target.style.height = 'auto'
          e.target.style.height = e.target.scrollHeight + 'px'
        }}
        onKeyDown={(e) => {
          if (e.key === 'Escape') onCancel()
          if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
            e.preventDefault()
            onSave(text)
          }
        }}
      />
      <div className="inline-editor-actions">
        <span className="muted">Esc za odustajanje · Enter za spremanje</span>
        <button className="btn small" onClick={onCancel}>
          Odustani
        </button>
        <button className="btn primary small" onClick={() => onSave(text)}>
          Spremi
        </button>
      </div>
    </div>
  )
}

function Attachments({ message }: { message: Message }) {
  return (
    <div className="attachments">
      {message.attachments!.map((a, i) => {
        const name = a.contentName ?? 'privitak'
        const isImage = a.contentType?.startsWith('image/') && a.attachmentDataRef
        if (isImage)
          return (
            <a key={i} href={api.attachmentUrl(message.id, i)} target="_blank" rel="noreferrer" className="attachment-image">
              <img src={api.attachmentUrl(message.id, i)} alt={name} loading="lazy" />
            </a>
          )
        const href = a.attachmentDataRef ? api.attachmentUrl(message.id, i, true) : a.downloadUri ?? api.attachmentUrl(message.id, i)
        return (
          <a key={i} href={href} target="_blank" rel="noreferrer" className="attachment-file">
            <span className="file-icon">{a.driveDataRef ? '🗂️' : '📄'}</span>
            <span className="file-name">{name}</span>
            <span className="muted">{a.driveDataRef ? 'Google Drive' : (a.contentType ?? '').split('/').pop()}</span>
          </a>
        )
      })}
    </div>
  )
}
